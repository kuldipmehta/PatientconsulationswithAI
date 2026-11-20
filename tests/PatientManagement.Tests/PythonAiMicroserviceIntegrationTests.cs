using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using PatientManagement.Api.Controllers;
using PatientManagement.Core.DTOs.Ai;
using PatientManagement.Core.DTOs.Common;
using PatientManagement.Core.Interfaces;
using PatientManagement.Infrastructure.Services.Ai;
using Xunit;

namespace PatientManagement.Tests;

public class PythonAiMicroserviceIntegrationTests
{
    private readonly IConfiguration _config;

    public PythonAiMicroserviceIntegrationTests()
    {
        var inMemorySettings = new Dictionary<string, string?>
        {
            ["PythonAiService:BaseUrl"] = "http://localhost:8000",
            ["PythonAiService:ApiKey"] = "test-secret-key-12345",
            ["PythonAiService:TimeoutSeconds"] = "15"
        };
        _config = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();
    }

    private static HttpClient CreateMockHttpClient(HttpResponseMessage mockResponse, Action<HttpRequestMessage>? captureRequest = null)
    {
        var mockHandler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => captureRequest?.Invoke(req))
            .ReturnsAsync(mockResponse);

        return new HttpClient(mockHandler.Object)
        {
            BaseAddress = new Uri("http://localhost:8000/")
        };
    }

    [Fact]
    public async Task PythonAiServiceClient_CheckHealthAsync_ReturnsDeserializedPayload()
    {
        var json = """
        {
            "status": "healthy",
            "service": "CareSync-FastAPI-AI-Microservice",
            "version": "1.0.0",
            "timestamp": "2026-11-20T12:00:00Z"
        }
        """;

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };

        HttpRequestMessage? sentRequest = null;
        var httpClient = CreateMockHttpClient(response, req => sentRequest = req);
        var client = new PythonAiServiceClient(httpClient, _config, NullLogger<PythonAiServiceClient>.Instance);

        var result = await client.CheckHealthAsync();

        Assert.NotNull(result);
        Assert.Equal("healthy", result.Status);
        Assert.Equal("CareSync-FastAPI-AI-Microservice", result.Service);
        Assert.NotNull(sentRequest);
        Assert.True(sentRequest.Headers.Contains("X-API-Key"));
        Assert.Equal("test-secret-key-12345", sentRequest.Headers.GetValues("X-API-Key").First());
    }

    [Fact]
    public async Task PythonAiServiceClient_CheckHealthAsync_WhenUnreachable_ReturnsNull()
    {
        var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        var httpClient = CreateMockHttpClient(response);
        var client = new PythonAiServiceClient(httpClient, _config, NullLogger<PythonAiServiceClient>.Instance);

        var result = await client.CheckHealthAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task PythonAiServiceClient_ExtractClinicalRecordAsync_ParsesEntitiesAndGuardrails()
    {
        var json = """
        {
            "vitals": {
                "systolic_bp": 142,
                "diastolic_bp": 92,
                "heart_rate_bpm": 88,
                "blood_glucose_mg_dl": 140.0,
                "hba1c_percent": 7.1
            },
            "diagnosed_conditions": ["Hypertension", "Type 2 Diabetes"],
            "medications": [
                {
                    "medication_name": "Warfarin",
                    "dosage": "5mg",
                    "is_active": true,
                    "contraindication_warning": "CRITICAL CONTRAINDICATION: Co-administration of Warfarin with an NSAID markedly increases severe hemorrhage risk."
                }
            ],
            "urgency_level": "moderate",
            "requires_immediate_escalation": false,
            "extraction_timestamp": "2026-11-20T12:00:00Z"
        }
        """;

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };

        var httpClient = CreateMockHttpClient(response);
        var client = new PythonAiServiceClient(httpClient, _config, NullLogger<PythonAiServiceClient>.Instance);

        var request = new PythonClinicalExtractRequestDto
        {
            PatientId = 42,
            RawClinicalText = "BP 142/92 mmHg, pulse 88 bpm. Patient taking Warfarin 5mg daily."
        };

        var result = await client.ExtractClinicalRecordAsync(request);

        Assert.NotNull(result);
        Assert.Equal(142, result.Vitals.SystolicBp);
        Assert.Equal(92, result.Vitals.DiastolicBp);
        Assert.Equal(88, result.Vitals.HeartRateBpm);
        Assert.Contains("Hypertension", result.DiagnosedConditions);
        Assert.Single(result.Medications);
        Assert.Equal("Warfarin", result.Medications[0].MedicationName);
        Assert.NotNull(result.Medications[0].ContraindicationWarning);
        Assert.False(result.RequiresImmediateEscalation);
    }

    [Fact]
    public async Task PythonAiServiceClient_IngestDocumentAsync_Returns202JobMetadata()
    {
        var json = """
        {
            "job_id": "job-7a3b4c5d6e7f",
            "status": "queued",
            "message": "Document 'DOC-001' queued for asynchronous chunking and indexing.",
            "document_id": "DOC-001",
            "patient_id": 99
        }
        """;

        var response = new HttpResponseMessage(HttpStatusCode.Accepted)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };

        var httpClient = CreateMockHttpClient(response);
        var client = new PythonAiServiceClient(httpClient, _config, NullLogger<PythonAiServiceClient>.Instance);

        var request = new PythonDocumentIngestRequestDto
        {
            DocumentId = "DOC-001",
            PatientId = 99,
            DocumentType = "DischargeSummary",
            Content = "Clinical report with comprehensive vitals and discharge medications."
        };

        var result = await client.IngestDocumentAsync(request);

        Assert.NotNull(result);
        Assert.Equal("job-7a3b4c5d6e7f", result.JobId);
        Assert.Equal("queued", result.Status);
        Assert.Equal("DOC-001", result.DocumentId);
        Assert.Equal(99, result.PatientId);
    }

    [Fact]
    public async Task PythonAiServiceClient_GetDocumentJobStatusAsync_CompletedWithChunks()
    {
        var json = """
        {
            "job_id": "job-7a3b4c5d6e7f",
            "document_id": "DOC-001",
            "patient_id": 99,
            "document_type": "DischargeSummary",
            "status": "completed",
            "chunks_count": 2,
            "chunks": [
                {
                    "chunk_index": 0,
                    "content": "First chunk text slice",
                    "char_start": 0,
                    "char_end": 22,
                    "embedding_preview": [0.12, -0.34, 0.56, -0.78, 0.90, -0.11, 0.22, -0.33]
                },
                {
                    "chunk_index": 1,
                    "content": "Second chunk text slice",
                    "char_start": 20,
                    "char_end": 43,
                    "embedding_preview": [-0.15, 0.45, -0.65, 0.25, -0.85, 0.35, -0.45, 0.55]
                }
            ],
            "created_at": "2026-11-20T12:00:00Z",
            "completed_at": "2026-11-20T12:00:01Z",
            "error": null
        }
        """;

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };

        var httpClient = CreateMockHttpClient(response);
        var client = new PythonAiServiceClient(httpClient, _config, NullLogger<PythonAiServiceClient>.Instance);

        var result = await client.GetDocumentJobStatusAsync("job-7a3b4c5d6e7f");

        Assert.NotNull(result);
        Assert.Equal("completed", result.Status);
        Assert.Equal(2, result.ChunksCount);
        Assert.Equal(2, result.Chunks.Count);
        Assert.Equal(8, result.Chunks[0].EmbeddingPreview.Count);
    }

    [Fact]
    public async Task PythonAiServiceClient_GetDocumentJobStatusAsync_NonExistent_ReturnsNull()
    {
        var response = new HttpResponseMessage(HttpStatusCode.NotFound);
        var httpClient = CreateMockHttpClient(response);
        var client = new PythonAiServiceClient(httpClient, _config, NullLogger<PythonAiServiceClient>.Instance);

        var result = await client.GetDocumentJobStatusAsync("non-existent-job");

        Assert.Null(result);
    }

    [Fact]
    public async Task PythonAiController_GetHealth_Returns503WhenUnreachable()
    {
        var mockClient = new Mock<IPythonAiServiceClient>();
        mockClient.Setup(c => c.CheckHealthAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((PythonHealthResponseDto?)null);

        var controller = new PythonAiController(mockClient.Object, NullLogger<PythonAiController>.Instance);

        var result = await controller.GetHealth(CancellationToken.None);

        var statusResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(503, statusResult.StatusCode);
    }

    [Fact]
    public async Task PythonAiController_ExtractClinicalEntities_RejectsShortText()
    {
        var mockClient = new Mock<IPythonAiServiceClient>();
        var controller = new PythonAiController(mockClient.Object, NullLogger<PythonAiController>.Instance);

        var request = new PythonClinicalExtractRequestDto
        {
            RawClinicalText = "Short"
        };

        var result = await controller.ExtractClinicalEntities(request, CancellationToken.None);

        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        var apiResponse = Assert.IsAssignableFrom<ApiResponse<object>>(badRequestResult.Value);
        Assert.False(apiResponse.Success);
        Assert.Contains("at least 10 characters", apiResponse.Message);
    }

    [Fact]
    public async Task PythonAiController_IngestDocument_Returns202Accepted()
    {
        var mockClient = new Mock<IPythonAiServiceClient>();
        mockClient.Setup(c => c.IngestDocumentAsync(It.IsAny<PythonDocumentIngestRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PythonDocumentIngestResponseDto
            {
                JobId = "job-abc12345",
                Status = "queued",
                Message = "Document queued successfully.",
                DocumentId = "DOC-99",
                PatientId = 12
            });

        var controller = new PythonAiController(mockClient.Object, NullLogger<PythonAiController>.Instance);

        var request = new PythonDocumentIngestRequestDto
        {
            DocumentId = "DOC-99",
            PatientId = 12,
            DocumentType = "LabReport",
            Content = "Comprehensive laboratory results with complete blood count."
        };

        var result = await controller.IngestDocument(request, CancellationToken.None);

        var acceptedResult = Assert.IsType<AcceptedResult>(result);
        var response = Assert.IsAssignableFrom<ApiResponse<PythonDocumentIngestResponseDto>>(acceptedResult.Value);
        Assert.True(response.Success);
        Assert.Equal("job-abc12345", response.Data?.JobId);
    }
}
