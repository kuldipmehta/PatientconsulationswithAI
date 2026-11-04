using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using PatientManagement.Core.DTOs.Ai;
using PatientManagement.Core.DTOs.Common;
using PatientManagement.Core.DTOs.Consultations;
using PatientManagement.Core.DTOs.Patients;
using PatientManagement.Core.Enums;
using PatientManagement.Core.Interfaces;
using PatientManagement.Infrastructure.Services;
using Xunit;

namespace PatientManagement.Tests;

public class GeminiPatientAgentServiceTests
{
    private readonly Mock<IPatientService> _patientServiceMock;
    private readonly Mock<IConsultationService> _consultationServiceMock;
    private readonly Mock<ILogger<GeminiPatientAgentService>> _loggerMock;
    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private readonly GeminiPatientAgentService _agentService;

    public GeminiPatientAgentServiceTests()
    {
        _patientServiceMock = new Mock<IPatientService>();
        _consultationServiceMock = new Mock<IConsultationService>();
        _loggerMock = new Mock<ILogger<GeminiPatientAgentService>>();
        _httpClient = new HttpClient();

        var inMemorySettings = new Dictionary<string, string?>
        {
            { "Gemini:ApiKey", "" }, // Simulated/Fallback Mode for testing
            { "Gemini:Model", "gemini-flash-latest" },
            { "Gemini:ApiBaseUrl", "https://generativelanguage.googleapis.com/v1beta" }
        };

        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        _agentService = new GeminiPatientAgentService(
            _httpClient,
            _configuration,
            _patientServiceMock.Object,
            _consultationServiceMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task ChatAsync_WhenAskedToSearchPatients_ExecutesSearchToolAndReturnsResults()
    {
        // Arrange
        var fakePatients = new List<PatientSummaryDto>
        {
            new() { Id = 1, FirstName = "Eleanor", LastName = "Vance", Email = "eleanor.vance@example.com", Age = 34, Gender = "Female", ConsultationCount = 2 }
        };

        _patientServiceMock.Setup(s => s.GetPatientsAsync(It.IsAny<PatientFilterParams>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<PatientSummaryDto>(fakePatients, 1, 1, 10));

        var request = new AiChatRequestDto
        {
            Message = "Search for patient Eleanor Vance"
        };

        // Act
        var response = await _agentService.ChatAsync(request);

        // Assert
        response.Should().NotBeNull();
        response.Success.Should().BeTrue();
        response.ToolsExecuted.Should().Contain(t => t.Contains("search_patients"));
        response.Reply.Should().Contain("Eleanor Vance");
        _patientServiceMock.Verify(s => s.GetPatientsAsync(It.IsAny<PatientFilterParams>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ChatAsync_WhenAskedToScheduleConsultation_ExecutesScheduleTool()
    {
        // Arrange
        var createdConsultation = new ConsultationResponseDto
        {
            Id = 99,
            PatientId = 1,
            PatientName = "Eleanor Vance",
            DoctorName = "Dr. Sarah Jenkins, MD",
            ConsultationDate = DateTime.UtcNow.AddDays(2),
            Diagnosis = "Follow-up consultation via AI Assistant",
            Status = ConsultationStatus.Scheduled,
            Fee = 150.00m
        };

        _consultationServiceMock.Setup(c => c.CreateConsultationAsync(It.IsAny<CreateConsultationDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdConsultation);

        var request = new AiChatRequestDto
        {
            Message = "Please schedule a consultation appointment for patient #1 with Dr. Jenkins",
            PatientContextId = 1
        };

        // Act
        var response = await _agentService.ChatAsync(request);

        // Assert
        response.Should().NotBeNull();
        response.Success.Should().BeTrue();
        response.ToolsExecuted.Should().Contain(t => t.Contains("schedule_consultation"));
        response.Reply.Should().Contain("Dr. Sarah Jenkins");
        _consultationServiceMock.Verify(c => c.CreateConsultationAsync(It.IsAny<CreateConsultationDto>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GenerateClinicalSummaryAsync_ReturnsStructuredAssessment()
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
        var summary = await _agentService.GenerateClinicalSummaryAsync(1);

        // Assert
        summary.Should().NotBeNull();
        summary.PatientId.Should().Be(1);
        summary.PatientName.Should().Be("Eleanor Vance");
        summary.KnownConditionsAndAllergies.Should().Contain(c => c.Contains("Penicillin"));
        summary.RecentConsultationHighlights.Should().NotBeEmpty();
        summary.ActionableRecommendations.Should().NotBeEmpty();
    }

    [Fact]
    public async Task TriageSymptomsAsync_WhenChestPainReported_AssignsEmergencyUrgency()
    {
        // Arrange
        var request = new AiTriageRequestDto
        {
            Symptoms = "Severe crushing chest pain radiating to left arm with shortness of breath",
            PatientAge = 58,
            Gender = "Male"
        };

        // Act
        var triage = await _agentService.TriageSymptomsAsync(request);

        // Assert
        triage.Should().NotBeNull();
        triage.UrgencyLevel.Should().Be("Emergency");
        triage.RecommendedSpecialty.Should().Contain("Cardiology");
        triage.RedFlagQuestions.Should().NotBeEmpty();
        triage.SuggestedNextSteps.Should().Contain(s => s.Contains("emergency"));
    }
}
