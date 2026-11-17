using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using PatientManagement.Core.DTOs.Ai;
using PatientManagement.Core.DTOs.Common;
using PatientManagement.Core.DTOs.Consultations;
using PatientManagement.Core.DTOs.Doctors;
using PatientManagement.Core.DTOs.Patients;
using PatientManagement.Core.DTOs.Rag;
using PatientManagement.Core.Enums;
using PatientManagement.Core.Interfaces;
using PatientManagement.Infrastructure.Data;
using PatientManagement.Infrastructure.Services;
using Xunit;

namespace PatientManagement.Tests;

public class GeminiPatientAgentServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly Mock<IPatientService> _patientServiceMock;
    private readonly Mock<IConsultationService> _consultationServiceMock;
    private readonly Mock<IDoctorService> _doctorServiceMock;
    private readonly Mock<ILogger<GeminiPatientAgentService>> _loggerMock;
    private readonly IConfiguration _simulatedConfig;
    private readonly GeminiPatientAgentService _simulatedAgentService;

    public GeminiPatientAgentServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new ApplicationDbContext(options);
        _patientServiceMock = new Mock<IPatientService>();
        _consultationServiceMock = new Mock<IConsultationService>();
        _doctorServiceMock = new Mock<IDoctorService>();
        _loggerMock = new Mock<ILogger<GeminiPatientAgentService>>();

        _doctorServiceMock.Setup(d => d.GetDoctorsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DoctorDto>
            {
                new() { Id = 1, FullName = "Dr. Sarah Jenkins, MD", Specialty = "Cardiology", Email = "sarah.jenkins@patientportal.com" }
            });

        _simulatedConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Gemini:ApiKey", "" }, // Offline Simulated Mode
                { "Gemini:Model", "gemini-flash-latest" },
                { "Gemini:ApiBaseUrl", "https://generativelanguage.googleapis.com/v1beta" }
            })
            .Build();

        _simulatedAgentService = new GeminiPatientAgentService(
            new HttpClient(),
            _dbContext,
            _simulatedConfig,
            _patientServiceMock.Object,
            _consultationServiceMock.Object,
            _doctorServiceMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task ChatAsync_WhenAskedToSearchPatients_ExecutesSearchToolAndReturnsSession()
    {
        // Arrange
        var fakePatients = new List<PatientSummaryDto>
        {
            new() { Id = 1, FirstName = "Eleanor", LastName = "Vance", Email = "eleanor.vance@example.com", Age = 34, Gender = "Female", ConsultationCount = 2 }
        };

        _patientServiceMock.Setup(s => s.GetPatientsAsync(It.IsAny<PatientFilterParams>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<PatientSummaryDto>(fakePatients, 1, 1, 10));

        // Act
        var response = await _simulatedAgentService.ChatAsync(new AiChatRequestDto
        {
            Message = "Search for patient records in database"
        });

        // Assert
        response.Should().NotBeNull();
        response.Success.Should().BeTrue();
        response.SessionId.Should().NotBeNullOrWhiteSpace();
        response.IsSimulatedMode.Should().BeTrue();
        response.SimulatedModeNotice.Should().Contain("SIMULATED MODE ACTIVE");
        response.Disclaimer.Should().Contain("AI Clinical Decision Support Only");
        response.ToolsExecuted.Should().Contain("search_patients");
        response.Reply.Should().Contain("1 patient record");
        _dbContext.AiInteractionLogs.Should().HaveCount(1);
    }

    [Fact]
    public async Task ChatAsync_WhenAskedToScheduleConsultation_ProposesPendingActionAndRequiresConfirmation()
    {
        // Arrange
        var patient = new PatientResponseDto
        {
            Id = 1,
            FirstName = "Eleanor",
            LastName = "Vance",
            Age = 34,
            Gender = "Female",
            MedicalNotes = "Penicillin allergy"
        };

        _patientServiceMock.Setup(s => s.GetPatientByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(patient);

        var createdConsultation = new ConsultationResponseDto
        {
            Id = 99,
            PatientId = 1,
            PatientName = "Eleanor Vance",
            DoctorId = 1,
            DoctorName = "Dr. Sarah Jenkins, MD",
            ConsultationDate = DateTime.UtcNow.Date.AddDays(1).AddHours(10),
            Diagnosis = "Requested AI Follow-up Consultation",
            Status = ConsultationStatus.Scheduled
        };

        _consultationServiceMock.Setup(c => c.CreateConsultationAsync(It.IsAny<CreateConsultationDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdConsultation);

        var request = new AiChatRequestDto
        {
            Message = "Please schedule a consultation appointment for patient #1 with Dr. Jenkins",
            PatientContextId = 1
        };

        // Act 1: Agent creates a pending proposal ONLY (does not mutate consultations yet)
        var chatResponse = await _simulatedAgentService.ChatAsync(request);

        // Assert 1
        chatResponse.ToolsExecuted.Should().Contain("propose_consultation");
        chatResponse.PendingActions.Should().HaveCount(1);
        chatResponse.PendingActions[0].Status.Should().Be("Pending");
        chatResponse.PendingActions[0].ConfirmationPrompt.Should().Contain("Dr. Sarah Jenkins");
        _consultationServiceMock.Verify(c => c.CreateConsultationAsync(It.IsAny<CreateConsultationDto>(), It.IsAny<CancellationToken>()), Times.Never);

        // Act 2: Clinician explicitly confirms the action
        var confirmed = await _simulatedAgentService.ConfirmPendingActionAsync(chatResponse.PendingActions[0].ActionId);

        // Assert 2
        confirmed.Id.Should().Be(99);
        _consultationServiceMock.Verify(c => c.CreateConsultationAsync(It.IsAny<CreateConsultationDto>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GenerateClinicalSummaryAsync_ReturnsDeIdentifiedLabelWithCitations()
    {
        // Arrange
        var patient = new PatientResponseDto
        {
            Id = 1,
            FirstName = "Eleanor",
            LastName = "Vance",
            Age = 34,
            Gender = "Female",
            MedicalNotes = "Penicillin allergy. Mild asthma.",
            Consultations = new List<ConsultationResponseDto>
            {
                new()
                {
                    Id = 10,
                    DoctorName = "Dr. Sarah Jenkins",
                    Diagnosis = "Routine annual physical exam",
                    Status = ConsultationStatus.Completed,
                    ConsultationDate = DateTime.UtcNow.AddDays(-30)
                }
            }
        };

        _patientServiceMock.Setup(s => s.GetPatientByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(patient);

        // Act
        var summary = await _simulatedAgentService.GenerateClinicalSummaryAsync(1);

        // Assert
        summary.Should().NotBeNull();
        summary.PatientId.Should().Be(1);
        summary.PatientLabel.Should().Be("Patient #1 (34y Female)");
        summary.KnownConditionsAndAllergies.Should().Contain(c => c.Contains("Penicillin"));
        summary.SourceCitations.Should().ContainSingle(c => c.ConsultationId == 10);
        summary.ActionableRecommendations.Should().NotBeEmpty();
        summary.IsSimulatedMode.Should().BeTrue();
    }

    [Fact]
    public async Task TriageSymptomsAsync_WhenChestPainReported_TriggersRuleBasedEmergencyBeforeLlm()
    {
        // Arrange
        var request = new AiTriageRequestDto
        {
            Symptoms = "Severe crushing chest pain radiating to left arm with shortness of breath",
            PatientAge = 58,
            Gender = "Male"
        };

        // Act
        var triage = await _simulatedAgentService.TriageSymptomsAsync(request);

        // Assert
        triage.Should().NotBeNull();
        triage.UrgencyLevel.Should().Be("Emergency");
        triage.RuleBasedEmergencyTriggered.Should().BeTrue();
        triage.MatchedEmergencyRule.Should().NotBeNullOrWhiteSpace();
        triage.SuggestedNextSteps.Should().Contain(s => s.Contains("emergency", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SummarizePatientLiveAsync_WithMockedGeminiHttp_ScrubsPiiAndParsesStructuredJson()
    {
        // Arrange: patient with sensitive PII fields that MUST NEVER leave to Gemini
        var patient = new PatientResponseDto
        {
            Id = 42,
            FirstName = "Johnathon",
            LastName = "ConfidentialName",
            Email = "secret.pii@example.com",
            PhoneNumber = "+1-555-999-8888",
            Address = "742 Evergreen Terrace Private Apt",
            Age = 52,
            Gender = "Male",
            MedicalNotes = "Type 2 diabetes, metformin intolerance",
            Consultations = new List<ConsultationResponseDto>
            {
                new()
                {
                    Id = 501,
                    DoctorName = "Dr. Robert Sterling",
                    ConsultationDate = DateTime.UtcNow.AddDays(-7),
                    Diagnosis = "Elevated HbA1c",
                    Symptoms = "Increased thirst"
                }
            }
        };
        _patientServiceMock.Setup(s => s.GetPatientByIdAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(patient);

        var mockedGeminiJson = JsonSerializer.Serialize(new
        {
            candidates = new[]
            {
                new
                {
                    content = new
                    {
                        parts = new[]
                        {
                            new
                            {
                                text = JsonSerializer.Serialize(new
                                {
                                    summary = "Patient #42 shows elevated HbA1c from Consultation #501.",
                                    identifiedRisks = new[] { "Metformin intolerance", "Elevated HbA1c" },
                                    recommendedActions = new[] { "Review alternative glycemic therapy" },
                                    urgencyAssessment = "Moderate"
                                })
                            }
                        }
                    }
                }
            }
        });

        string? capturedOutboundBody = null;
        var handler = new StubHttpMessageHandler(async (req, _) =>
        {
            capturedOutboundBody = await req.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(mockedGeminiJson, Encoding.UTF8, "application/json")
            };
        });

        var liveConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Gemini:ApiKey", "test-mock-key-offline" },
                { "Gemini:Model", "gemini-flash-latest" }
            })
            .Build();

        var liveAgent = new GeminiPatientAgentService(
            new HttpClient(handler),
            _dbContext,
            liveConfig,
            _patientServiceMock.Object,
            _consultationServiceMock.Object,
            _doctorServiceMock.Object,
            _loggerMock.Object);

        // Act
        var result = await liveAgent.SummarizePatientLiveAsync(42);

        // Assert
        result.IsSimulatedMode.Should().BeFalse();
        result.SimulatedModeNotice.Should().BeNull();
        result.PatientLabel.Should().Be("Patient #42 (52y Male)");
        result.UrgencyAssessment.Should().Be("Moderate");
        result.AiIdentifiedRisks.Should().Contain("Metformin intolerance");
        result.SourceCitations.Should().ContainSingle(c => c.ConsultationId == 501);

        // Verify strict PII scrubbing in outbound payload
        capturedOutboundBody.Should().NotBeNull();
        capturedOutboundBody.Should().NotContain("Johnathon");
        capturedOutboundBody.Should().NotContain("ConfidentialName");
        capturedOutboundBody.Should().NotContain("secret.pii@example.com");
        capturedOutboundBody.Should().NotContain("555-999-8888");
        capturedOutboundBody.Should().NotContain("Evergreen Terrace");
        capturedOutboundBody.Should().Contain("Patient #42");
    }

    [Fact]
    public async Task ChatAsync_WithMockedGeminiFunctionCall_ProposesActionWithoutAutoExecuting()
    {
        // Arrange
        var patient = new PatientResponseDto { Id = 7, FirstName = "Jane", LastName = "Roe", Age = 31, Gender = "Female" };
        _patientServiceMock.Setup(s => s.GetPatientByIdAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(patient);

        int callCount = 0;
        string? secondTurnPayload = null;
        var handler = new StubHttpMessageHandler(async (req, ct) =>
        {
            callCount++;
            if (callCount == 2 && req.Content != null)
            {
                secondTurnPayload = await req.Content.ReadAsStringAsync(ct);
            }

            var json = callCount == 1
                ? JsonSerializer.Serialize(new
                {
                    candidates = new[]
                    {
                        new
                        {
                            content = new
                            {
                                parts = new[]
                                {
                                    new
                                    {
                                        functionCall = new
                                        {
                                            id = "call_123",
                                            name = "propose_consultation",
                                            args = new
                                            {
                                                patientId = 7,
                                                doctorName = "Dr. Sarah Jenkins",
                                                diagnosis = "Cardiology follow-up",
                                                consultationDateUtc = "2026-11-25T10:00:00Z"
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                })
                : JsonSerializer.Serialize(new
                {
                    candidates = new[]
                    {
                        new
                        {
                            content = new
                            {
                                parts = new[] { new { text = "I have proposed a follow-up with Dr. Sarah Jenkins for your approval." } }
                            }
                        }
                    }
                });

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });

        var liveConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Gemini:ApiKey", "test-mock-key-offline" },
                { "Gemini:Model", "gemini-flash-latest" }
            })
            .Build();

        var liveAgent = new GeminiPatientAgentService(
            new HttpClient(handler),
            _dbContext,
            liveConfig,
            _patientServiceMock.Object,
            _consultationServiceMock.Object,
            _doctorServiceMock.Object,
            _loggerMock.Object);

        // Act
        var response = await liveAgent.ChatAsync(new AiChatRequestDto { Message = "Book cardiology follow-up", PatientContextId = 7 });

        // Assert
        callCount.Should().Be(2);
        secondTurnPayload.Should().NotBeNull();
        secondTurnPayload.Should().Contain("\"role\":\"user\"");
        secondTurnPayload.Should().NotContain("\"role\":\"function\"");
        secondTurnPayload.Should().Contain("\"id\":\"call_123\"");
        response.ToolsExecuted.Should().Contain("propose_consultation");
        response.PendingActions.Should().HaveCount(1);
        response.Reply.Should().Contain("Dr. Sarah Jenkins");
        _consultationServiceMock.Verify(c => c.CreateConsultationAsync(It.IsAny<CreateConsultationDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ChatAsync_WhenPrimaryModelReturns503_AutomaticallyFallsBackToLiteModel()
    {
        // Arrange
        var seenModels = new List<string>();
        var handler = new StubHttpMessageHandler((req, _) =>
        {
            var url = req.RequestUri!.ToString();
            seenModels.Add(url);

            if (url.Contains("gemini-flash-latest"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    Content = new StringContent("{\"error\":{\"code\":503,\"status\":\"UNAVAILABLE\"}}", Encoding.UTF8, "application/json")
                });
            }

            var okJson = JsonSerializer.Serialize(new
            {
                candidates = new[]
                {
                    new { content = new { parts = new[] { new { text = "Fallback model succeeded cleanly." } } } }
                }
            });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(okJson, Encoding.UTF8, "application/json")
            });
        });

        var liveConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Gemini:ApiKey", "test-mock-key-offline" },
                { "Gemini:Model", "gemini-flash-latest" }
            })
            .Build();

        var liveAgent = new GeminiPatientAgentService(
            new HttpClient(handler),
            _dbContext,
            liveConfig,
            _patientServiceMock.Object,
            _consultationServiceMock.Object,
            _doctorServiceMock.Object,
            _loggerMock.Object);

        // Act
        var response = await liveAgent.ChatAsync(new AiChatRequestDto { Message = "Hello assistant" });

        // Assert
        response.IsSimulatedMode.Should().BeFalse();
        response.ModelUsed.Should().Be("gemini-flash-lite-latest");
        response.Reply.Should().Be("Fallback model succeeded cleanly.");
        seenModels.Should().Contain(u => u.Contains("gemini-flash-latest"));
        seenModels.Should().Contain(u => u.Contains("gemini-flash-lite-latest"));
    }

    [Fact]
    public async Task SummarizePatientLiveAsync_WhenAllModelsReturn503_DegradesGracefullyToSimulatedFallback()
    {
        // Arrange
        _patientServiceMock.Setup(s => s.GetPatientByIdAsync(88, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PatientResponseDto
            {
                Id = 88,
                FirstName = "Test",
                LastName = "Patient",
                Age = 45,
                Gender = "Male",
                MedicalNotes = "Mild hypertension"
            });

        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("{\"error\":{\"code\":503}}", Encoding.UTF8, "application/json")
            }));

        var liveConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Gemini:ApiKey", "test-mock-key-offline" },
                { "Gemini:Model", "gemini-flash-latest" }
            })
            .Build();

        var liveAgent = new GeminiPatientAgentService(
            new HttpClient(handler),
            _dbContext,
            liveConfig,
            _patientServiceMock.Object,
            _consultationServiceMock.Object,
            _doctorServiceMock.Object,
            _loggerMock.Object);

        // Act
        var summary = await liveAgent.SummarizePatientLiveAsync(88);

        // Assert
        summary.IsSimulatedMode.Should().BeTrue();
        summary.ModelUsed.Should().Be("fallback-simulated");
        summary.AiGeneratedSummary.Should().Contain("Fallback Mode");
        summary.SimulatedModeNotice.Should().Contain("SIMULATED MODE ACTIVE");
    }

    [Fact]
    public async Task ChatAsync_WhenPrimaryModelTimesOut_AutomaticallyFallsBackToNextModel()
    {
        // Arrange
        var seenModels = new List<string>();
        var handler = new StubHttpMessageHandler((req, _) =>
        {
            var url = req.RequestUri!.ToString();
            seenModels.Add(url);

            if (url.Contains("gemini-flash-latest"))
            {
                throw new TaskCanceledException("Simulated upstream timeout");
            }

            var okJson = JsonSerializer.Serialize(new
            {
                candidates = new[]
                {
                    new { content = new { parts = new[] { new { text = "Recovered after upstream timeout." } } } }
                }
            });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(okJson, Encoding.UTF8, "application/json")
            });
        });

        var liveConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Gemini:ApiKey", "test-mock-key-offline" },
                { "Gemini:Model", "gemini-flash-latest" }
            })
            .Build();

        var liveAgent = new GeminiPatientAgentService(
            new HttpClient(handler),
            _dbContext,
            liveConfig,
            _patientServiceMock.Object,
            _consultationServiceMock.Object,
            _doctorServiceMock.Object,
            _loggerMock.Object);

        // Act
        var response = await liveAgent.ChatAsync(new AiChatRequestDto { Message = "Hello assistant" });

        // Assert
        response.IsSimulatedMode.Should().BeFalse();
        response.ModelUsed.Should().Be("gemini-flash-lite-latest");
        response.Reply.Should().Be("Recovered after upstream timeout.");
        seenModels.Should().Contain(u => u.Contains("gemini-flash-latest"));
        seenModels.Should().Contain(u => u.Contains("gemini-flash-lite-latest"));
    }

    [Fact]
    public async Task ChatAsync_WhenGeminiCallsSemanticSearchTool_ExecutesRagToolAndGroundsResponse()
    {
        // Arrange
        var patient = new PatientResponseDto { Id = 9, FirstName = "Diana", LastName = "Prince", Age = 28, Gender = "Female" };
        _patientServiceMock.Setup(s => s.GetPatientByIdAsync(9, It.IsAny<CancellationToken>()))
            .ReturnsAsync(patient);

        var ragServiceMock = new Mock<IPatientRagService>();
        ragServiceMock.Setup(r => r.SearchPatientKnowledgeAsync(9, "asthma treatment", It.IsAny<int>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RagSearchResultDto>
            {
                new()
                {
                    ChunkId = 1,
                    PatientId = 9,
                    SourceType = "PatientDocument",
                    SourceLabel = "pulmonology_eval.pdf (Section 1)",
                    TextContent = "Patient prescribed salbutamol inhaler 100mcg and budesonide daily.",
                    SimilarityScore = 0.89,
                    CreatedAtUtc = DateTime.UtcNow
                }
            });

        int callCount = 0;
        var handler = new StubHttpMessageHandler((_, _) =>
        {
            callCount++;
            var json = callCount == 1
                ? JsonSerializer.Serialize(new
                {
                    candidates = new[]
                    {
                        new
                        {
                            content = new
                            {
                                parts = new[]
                                {
                                    new
                                    {
                                        functionCall = new
                                        {
                                            id = "call_rag_1",
                                            name = "semantic_search_patient_history",
                                            args = new { patientId = 9, query = "asthma treatment", topK = 3 }
                                        }
                                    }
                                }
                            }
                        }
                    }
                })
                : JsonSerializer.Serialize(new
                {
                    candidates = new[]
                    {
                        new
                        {
                            content = new
                            {
                                parts = new[] { new { text = "According to patient records, they are prescribed salbutamol 100mcg and budesonide." } }
                            }
                        }
                    }
                });

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        });

        var liveConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Gemini:ApiKey", "test-mock-key-offline" },
                { "Gemini:Model", "gemini-flash-latest" }
            })
            .Build();

        var liveAgent = new GeminiPatientAgentService(
            new HttpClient(handler),
            _dbContext,
            liveConfig,
            _patientServiceMock.Object,
            _consultationServiceMock.Object,
            _doctorServiceMock.Object,
            _loggerMock.Object,
            currentUser: null,
            ragService: ragServiceMock.Object);

        // Act
        var response = await liveAgent.ChatAsync(new AiChatRequestDto { Message = "What is the asthma treatment on file?", PatientContextId = 9 });

        // Assert
        callCount.Should().Be(2);
        response.ToolsExecuted.Should().Contain("semantic_search_patient_history");
        response.Reply.Should().Contain("salbutamol");
        ragServiceMock.Verify(r => r.SearchPatientKnowledgeAsync(9, "asthma treatment", It.IsAny<int>(), It.IsAny<double>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ChatAsync_WithPathTraversalModel_FallsBackToDefaultAndSendsKeyInHeaderNotQueryString()
    {
        // Arrange
        HttpRequestMessage? capturedRequest = null;
        var handler = new StubHttpMessageHandler((req, _) =>
        {
            capturedRequest = req;
            var okJson = JsonSerializer.Serialize(new
            {
                candidates = new[]
                {
                    new { content = new { parts = new[] { new { text = "Safe response using default model." } } } }
                }
            });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(okJson, Encoding.UTF8, "application/json")
            });
        });

        var liveConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Gemini:ApiKey", "test-secret-api-key-999" },
                { "Gemini:Model", "gemini-flash-latest" },
                { "Gemini:AllowedModels:0", "gemini-flash-latest" },
                { "Gemini:AllowedModels:1", "gemini-2.5-pro" }
            })
            .Build();

        var liveAgent = new GeminiPatientAgentService(
            new HttpClient(handler),
            _dbContext,
            liveConfig,
            _patientServiceMock.Object,
            _consultationServiceMock.Object,
            _doctorServiceMock.Object,
            _loggerMock.Object);

        // Act: Attempt path traversal via model=../../files
        var response = await liveAgent.ChatAsync(new AiChatRequestDto
        {
            Message = "Hello",
            Model = "../../files"
        });

        // Assert: Replaced with default model, no traversal in URL, key sent ONLY in x-goog-api-key header
        capturedRequest.Should().NotBeNull();
        var url = capturedRequest!.RequestUri!.ToString();
        url.Should().Be("https://generativelanguage.googleapis.com/v1beta/models/gemini-flash-latest:generateContent");
        url.Should().NotContain("..");
        url.Should().NotContain("files");
        url.Should().NotContain("key=");
        url.Should().NotContain("test-secret-api-key-999");

        capturedRequest.Headers.TryGetValues("x-goog-api-key", out var keyValues).Should().BeTrue();
        keyValues.Should().ContainSingle().Which.Should().Be("test-secret-api-key-999");
        response.ModelUsed.Should().Be("gemini-flash-latest");
    }

    [Fact]
    public async Task TriageAndModels_EnforceAllowlistAndUseHeaderKey()
    {
        // Arrange
        var capturedRequests = new List<HttpRequestMessage>();
        var handler = new StubHttpMessageHandler((req, _) =>
        {
            capturedRequests.Add(req);
            if (req.Method == HttpMethod.Get)
            {
                var listJson = JsonSerializer.Serialize(new
                {
                    models = new[]
                    {
                        new { name = "models/gemini-2.5-pro", supportedGenerationMethods = new[] { "generateContent" } },
                        new { name = "models/unlisted-experimental-model", supportedGenerationMethods = new[] { "generateContent" } }
                    }
                });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(listJson, Encoding.UTF8, "application/json")
                });
            }

            var triageJson = JsonSerializer.Serialize(new
            {
                candidates = new[]
                {
                    new
                    {
                        content = new
                        {
                            parts = new[]
                            {
                                new
                                {
                                    text = JsonSerializer.Serialize(new
                                    {
                                        urgencyLevel = "Low",
                                        triageSummary = "Mild tension headache.",
                                        possibleConditions = new[] { "Tension headache" },
                                        recommendedNextSteps = new[] { "Hydration and rest" },
                                        recommendedSpecialty = "General Practice"
                                    })
                                }
                            }
                        }
                    }
                }
            });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(triageJson, Encoding.UTF8, "application/json")
            });
        });

        var liveConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Gemini:ApiKey", "header-only-secret-key" },
                { "Gemini:Model", "gemini-flash-latest" },
                { "Gemini:AllowedModels:0", "gemini-flash-latest" },
                { "Gemini:AllowedModels:1", "gemini-2.5-pro" }
            })
            .Build();

        var liveAgent = new GeminiPatientAgentService(
            new HttpClient(handler),
            _dbContext,
            liveConfig,
            _patientServiceMock.Object,
            _consultationServiceMock.Object,
            _doctorServiceMock.Object,
            _loggerMock.Object);

        // Act
        var models = await liveAgent.GetAvailableModelsAsync();
        await liveAgent.TriageSymptomsAsync(new AiTriageRequestDto { Symptoms = "mild headache" }, explicitModel: "../../files");
        await liveAgent.TriageSymptomsAsync(new AiTriageRequestDto { Symptoms = "mild headache" }, explicitModel: "gemini-2.5-pro");

        // Assert
        models.Should().Contain("gemini-2.5-pro");
        models.Should().NotContain("unlisted-experimental-model");

        capturedRequests.Should().HaveCount(3);
        foreach (var req in capturedRequests)
        {
            req.RequestUri!.ToString().Should().NotContain("key=");
            req.Headers.GetValues("x-goog-api-key").Should().ContainSingle().Which.Should().Be("header-only-secret-key");
        }

        // First triage with ../../files fell back to default gemini-flash-latest
        capturedRequests[1].RequestUri!.ToString().Should().EndWith("/models/gemini-flash-latest:generateContent");
        // Second triage with allowlisted gemini-2.5-pro used gemini-2.5-pro
        capturedRequests[2].RequestUri!.ToString().Should().EndWith("/models/gemini-2.5-pro:generateContent");
    }

    public void Dispose()
    {
        _dbContext.Database.EnsureDeleted();
        _dbContext.Dispose();
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;

        public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            _responder(request, cancellationToken);
    }
}
