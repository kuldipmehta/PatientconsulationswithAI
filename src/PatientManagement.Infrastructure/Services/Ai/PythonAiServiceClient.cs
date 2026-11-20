using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PatientManagement.Core.DTOs.Ai;
using PatientManagement.Core.Interfaces;

namespace PatientManagement.Infrastructure.Services.Ai;

/// <summary>
/// HTTP client implementation integrating the .NET 10 CareSync platform with the Python FastAPI AI Microservice.
/// Uses constant-time API-key authentication, snake_case JSON serialization, and structured error propagation.
/// </summary>
public sealed class PythonAiServiceClient : IPythonAiServiceClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<PythonAiServiceClient> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public PythonAiServiceClient(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<PythonAiServiceClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        var baseUrl = configuration["PythonAiService:BaseUrl"] ?? "http://localhost:8000";
        var apiKey = configuration["PythonAiService:ApiKey"] ?? "dev-caresync-ai-secret-key-super-secure-change-in-prod-2026";

        if (_httpClient.BaseAddress == null)
        {
            _httpClient.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        }

        if (!_httpClient.DefaultRequestHeaders.Contains("X-API-Key"))
        {
            _httpClient.DefaultRequestHeaders.Add("X-API-Key", apiKey);
        }

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true,
        };
    }

    public async Task<PythonHealthResponseDto?> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("api/v1/health", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Python AI Microservice health check returned status {StatusCode}", response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<PythonHealthResponseDto>(_jsonOptions, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to Python AI Microservice health probe");
            return null;
        }
    }

    public async Task<PythonEngineStatusDto?> GetEngineStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("api/v1/health/engine", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Python AI Microservice engine status probe returned {StatusCode}", response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<PythonEngineStatusDto>(_jsonOptions, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query Python AI Microservice engine status");
            return null;
        }
    }

    public async Task<PythonClinicalRecordDto> ExtractClinicalRecordAsync(
        PythonClinicalExtractRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("api/v1/extract/clinical-entity", request, _jsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Python AI extraction failed with status {StatusCode}: {Error}", response.StatusCode, errorBody);
            throw new HttpRequestException($"Python AI Microservice extraction failed ({response.StatusCode}): {errorBody}");
        }

        var result = await response.Content.ReadFromJsonAsync<PythonClinicalRecordDto>(_jsonOptions, cancellationToken);
        return result ?? throw new InvalidOperationException("Empty response received from Python AI Microservice extraction.");
    }

    public async Task<PythonDocumentIngestResponseDto> IngestDocumentAsync(
        PythonDocumentIngestRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("api/v1/documents/ingest", request, _jsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.Accepted)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Python AI document ingestion failed with status {StatusCode}: {Error}", response.StatusCode, errorBody);
            throw new HttpRequestException($"Python AI Microservice document ingestion failed ({response.StatusCode}): {errorBody}");
        }

        var result = await response.Content.ReadFromJsonAsync<PythonDocumentIngestResponseDto>(_jsonOptions, cancellationToken);
        return result ?? throw new InvalidOperationException("Empty response received from Python AI document ingestion endpoint.");
    }

    public async Task<PythonJobStatusResponseDto?> GetDocumentJobStatusAsync(
        string jobId,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"api/v1/documents/jobs/{Uri.EscapeDataString(jobId)}", cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Failed to fetch Python AI document job {JobId} ({StatusCode}): {Error}", jobId, response.StatusCode, errorBody);
            throw new HttpRequestException($"Python AI Microservice job status query failed ({response.StatusCode}): {errorBody}");
        }

        return await response.Content.ReadFromJsonAsync<PythonJobStatusResponseDto>(_jsonOptions, cancellationToken);
    }

    public async Task<List<PythonJobStatusResponseDto>> ListDocumentJobsAsync(
        int? patientId = null,
        CancellationToken cancellationToken = default)
    {
        var url = patientId.HasValue
            ? $"api/v1/documents/jobs?patient_id={patientId.Value}"
            : "api/v1/documents/jobs";

        var response = await _httpClient.GetAsync(url, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Failed to list Python AI document jobs ({StatusCode}): {Error}", response.StatusCode, errorBody);
            throw new HttpRequestException($"Python AI Microservice listing jobs failed ({response.StatusCode}): {errorBody}");
        }

        var result = await response.Content.ReadFromJsonAsync<List<PythonJobStatusResponseDto>>(_jsonOptions, cancellationToken);
        return result ?? new List<PythonJobStatusResponseDto>();
    }
}
