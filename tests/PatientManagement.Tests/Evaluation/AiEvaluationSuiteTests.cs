using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using PatientManagement.Core.DTOs.Ai;
using PatientManagement.Core.DTOs.Consultations;
using PatientManagement.Core.DTOs.Patients;
using PatientManagement.Core.Enums;
using PatientManagement.Core.Interfaces;
using PatientManagement.Infrastructure.Data;
using PatientManagement.Infrastructure.Services;
using PatientManagement.Infrastructure.Services.Ai;
using Xunit;

namespace PatientManagement.Tests.Evaluation;

public class AiEvaluationSuiteTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly Mock<IPatientService> _patientServiceMock;
    private readonly GeminiPatientAgentService _agentService;

    public AiEvaluationSuiteTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"EvalDb_{Guid.NewGuid():N}")
            .Options;

        _dbContext = new ApplicationDbContext(options);
        _patientServiceMock = new Mock<IPatientService>();
        var consultationMock = new Mock<IConsultationService>();
        var doctorMock = new Mock<IDoctorService>();
        var loggerMock = new Mock<ILogger<GeminiPatientAgentService>>();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Gemini:ApiKey"] = "",
                ["Gemini:Model"] = "gemini-flash-latest"
            })
            .Build();

        _agentService = new GeminiPatientAgentService(
            new HttpClient(),
            _dbContext,
            config,
            _patientServiceMock.Object,
            consultationMock.Object,
            doctorMock.Object,
            loggerMock.Object);
    }

    public static IEnumerable<object[]> LoadTriageEvaluationCases()
    {
        var dataset = LoadDataset();
        foreach (var item in dataset.TriageCases)
        {
            yield return [item.Id, item.Category, item.Symptoms, item.Age, item.Gender, item.ExpectedUrgency, item.ExpectEmergencyRule, item.ExpectedTextContains];
        }
    }

    public static IEnumerable<object[]> LoadSummaryEvaluationCases()
    {
        var dataset = LoadDataset();
        foreach (var item in dataset.SummaryCases)
        {
            yield return [item.Id, item.PatientId, item.FirstName, item.LastName, item.Age, item.Gender, item.MedicalNotes, item.ConsultationId, item.Diagnosis, item.ExpectedMustContain, item.ExpectedMustNotContain];
        }
    }

    [Theory]
    [MemberData(nameof(LoadTriageEvaluationCases))]
    public async Task EvaluateTriageAndPromptInjectionCase(
        string caseId,
        string category,
        string symptoms,
        int age,
        string gender,
        string expectedUrgency,
        bool expectEmergencyRule,
        string expectedTextContains)
    {
        var result = await _agentService.TriageSymptomsAsync(new AiTriageRequestDto
        {
            Symptoms = symptoms,
            PatientAge = age,
            Gender = gender
        });

        result.UrgencyLevel.Should().Be(expectedUrgency, because: $"[{caseId} - {category}] failed urgency classification");
        result.RuleBasedEmergencyTriggered.Should().Be(expectEmergencyRule, because: $"[{caseId}] emergency rule mismatch");
        result.Disclaimer.Should().Contain("AI Clinical Decision Support Only");
        result.TriageNotes.Should().ContainEquivalentOf(expectedTextContains, because: $"[{caseId}] missing expected clinical guidance");

        // Verify prompt injection delimiters sanitize closing tags
        var wrapped = PatientAiSanitizer.WrapUntrustedText(symptoms);
        wrapped.Should().StartWith("<untrusted_clinical_text>");
        wrapped.Should().EndWith("</untrusted_clinical_text>");
        wrapped[25..^26].Should().NotContain("<untrusted_clinical_text>").And.NotContain("</untrusted_clinical_text>");
    }

    [Theory]
    [MemberData(nameof(LoadSummaryEvaluationCases))]
    public async Task EvaluateDeIdentifiedPatientSummaryAndCitationsCase(
        string caseId,
        int patientId,
        string firstName,
        string lastName,
        int age,
        string gender,
        string medicalNotes,
        int consultationId,
        string diagnosis,
        string expectedMustContain,
        string expectedMustNotContain)
    {
        var patient = new PatientResponseDto
        {
            Id = patientId,
            FirstName = firstName,
            LastName = lastName,
            Email = $"{firstName.ToLowerInvariant()}@private.test",
            PhoneNumber = "+1-555-000-1111",
            Address = "999 Private Street",
            Age = age,
            Gender = gender,
            MedicalNotes = medicalNotes,
            Consultations =
            [
                new ConsultationResponseDto
                {
                    Id = consultationId,
                    PatientId = patientId,
                    DoctorName = "Dr. Sarah Jenkins, MD",
                    ConsultationDate = DateTime.UtcNow.AddDays(-5),
                    Diagnosis = diagnosis,
                    Status = ConsultationStatus.Completed
                }
            ]
        };

        _patientServiceMock.Setup(s => s.GetPatientByIdAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(patient);

        var summary = await _agentService.SummarizePatientLiveAsync(patientId);

        summary.PatientLabel.Should().Be($"Patient #{patientId} ({age}y {gender})", because: $"[{caseId}] must de-identify label");
        summary.SourceCitations.Should().ContainSingle(c => c.ConsultationId == consultationId);
        summary.Disclaimer.Should().Contain("AI Clinical Decision Support Only");

        var serializedSanitized = JsonSerializer.Serialize(summary.SanitizedClinicalDataUsed);
        serializedSanitized.Should().NotContain(expectedMustNotContain, because: $"[{caseId}] PII surname leaked into AI payload");
        serializedSanitized.Should().NotContain("@private.test");
        serializedSanitized.Should().NotContain("555-000-1111");
        serializedSanitized.Should().NotContain("999 Private Street");

        var combinedOutput = $"{summary.AiGeneratedSummary} {string.Join(" ", summary.AiIdentifiedRisks)}";
        combinedOutput.Should().ContainEquivalentOf(expectedMustContain, because: $"[{caseId}] missed clinical fact");
    }

    private static EvalDataset LoadDataset()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Evaluation", "ai-evaluation-dataset.json");
        var json = File.ReadAllText(path);
        var root = JsonSerializer.Deserialize<EvalDataset>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Failed to deserialize ai-evaluation-dataset.json");
        return root;
    }

    public void Dispose()
    {
        _dbContext.Database.EnsureDeleted();
        _dbContext.Dispose();
    }

    private sealed class EvalDataset
    {
        public List<TriageEvalCase> TriageCases { get; set; } = [];
        public List<SummaryEvalCase> SummaryCases { get; set; } = [];
    }

    private sealed class TriageEvalCase
    {
        public string Id { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Symptoms { get; set; } = string.Empty;
        public int Age { get; set; }
        public string Gender { get; set; } = string.Empty;
        public string ExpectedUrgency { get; set; } = string.Empty;
        public bool ExpectEmergencyRule { get; set; }
        public string ExpectedTextContains { get; set; } = string.Empty;
    }

    private sealed class SummaryEvalCase
    {
        public string Id { get; set; } = string.Empty;
        public int PatientId { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public int Age { get; set; }
        public string Gender { get; set; } = string.Empty;
        public string MedicalNotes { get; set; } = string.Empty;
        public int ConsultationId { get; set; }
        public string Diagnosis { get; set; } = string.Empty;
        public string ExpectedMustContain { get; set; } = string.Empty;
        public string ExpectedMustNotContain { get; set; } = string.Empty;
    }
}
