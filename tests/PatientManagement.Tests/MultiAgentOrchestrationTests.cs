using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PatientManagement.Core.DTOs.Ai;
using PatientManagement.Core.DTOs.Consultations;
using PatientManagement.Core.DTOs.Patients;
using PatientManagement.Core.Enums;
using PatientManagement.Core.Interfaces;
using PatientManagement.Infrastructure.Services.Ai.Agents;
using Xunit;

namespace PatientManagement.Tests;

public class MultiAgentOrchestrationTests
{
    [Fact]
    public async Task PharmacovigilanceAgent_WhenWarfarinAndIbuprofen_FlagsMajorContraindicationAndSuggestsParacetamol()
    {
        // Arrange
        var agent = new PharmacovigilanceAgent(NullLogger<PharmacovigilanceAgent>.Instance);
        var context = new ClinicalAgentContext(
            UserMessage: "Patient reports severe knee pain. Can we prescribe Ibuprofen 400mg TID for 5 days?",
            PatientId: 1,
            Patient: null,
            KnownAllergies: new List<string>(),
            ActiveMedications: new List<string> { "Warfarin 5mg daily", "Lisinopril 10mg daily" },
            PreviousDiagnoses: new List<string> { "Atrial Fibrillation" },
            RagSnippets: new List<string>()
        );

        // Act
        Assert.True(agent.CanHandle(context));
        var result = await agent.AnalyzeAsync(context);

        // Assert
        Assert.NotEmpty(result.Warnings);
        Assert.Contains(result.Warnings, w => w.Contains("MAJOR CONTRAINDICATION", StringComparison.OrdinalIgnoreCase) && w.Contains("Bleeding Risk", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Recommendations, r => r.Contains("Acetaminophen", StringComparison.OrdinalIgnoreCase) || r.Contains("Paracetamol", StringComparison.OrdinalIgnoreCase));
        Assert.True(result.RequiresUrgentEscalation);
    }

    [Fact]
    public async Task PharmacovigilanceAgent_WhenPenicillinAllergyAndAmoxicillin_FlagsCriticalAllergyAlert()
    {
        // Arrange
        var agent = new PharmacovigilanceAgent(NullLogger<PharmacovigilanceAgent>.Instance);
        var context = new ClinicalAgentContext(
            UserMessage: "Can we prescribe Amoxicillin 500mg for respiratory infection?",
            PatientId: 2,
            Patient: null,
            KnownAllergies: new List<string> { "Penicillin anaphylaxis" },
            ActiveMedications: new List<string>(),
            PreviousDiagnoses: new List<string>(),
            RagSnippets: new List<string>()
        );

        // Act
        var result = await agent.AnalyzeAsync(context);

        // Assert
        Assert.NotEmpty(result.Warnings);
        Assert.Contains(result.Warnings, w => w.Contains("CRITICAL ALLERGY ALERT", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Recommendations, r => r.Contains("Avoid all penicillin-derivative", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DiagnosticTriageAgent_WhenAcuteJointSprain_RecommendsRiceAndOttawaRules()
    {
        // Arrange
        var agent = new DiagnosticTriageAgent(NullLogger<DiagnosticTriageAgent>.Instance);
        var context = new ClinicalAgentContext(
            UserMessage: "Patient suffered acute ankle sprain and joint swelling while stepping off curb.",
            PatientId: 3,
            Patient: null,
            KnownAllergies: new List<string>(),
            ActiveMedications: new List<string>(),
            PreviousDiagnoses: new List<string>(),
            RagSnippets: new List<string>()
        );

        // Act
        Assert.True(agent.CanHandle(context));
        var result = await agent.AnalyzeAsync(context);

        // Assert
        Assert.Contains(result.Recommendations, r => r.Contains("RICE therapy", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Recommendations, r => r.Contains("X-ray", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Recommendations, r => r.Contains("orthopedic", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DiagnosticTriageAgent_WhenChestPainEmergency_FlagsUrgentEscalation()
    {
        // Arrange
        var agent = new DiagnosticTriageAgent(NullLogger<DiagnosticTriageAgent>.Instance);
        var context = new ClinicalAgentContext(
            UserMessage: "Crushing chest pain radiating to left arm and jaw.",
            PatientId: 4,
            Patient: null,
            KnownAllergies: new List<string>(),
            ActiveMedications: new List<string>(),
            PreviousDiagnoses: new List<string>(),
            RagSnippets: new List<string>()
        );

        // Act
        var result = await agent.AnalyzeAsync(context);

        // Assert
        Assert.True(result.RequiresUrgentEscalation);
        Assert.Contains(result.Warnings, w => w.Contains("RED FLAG", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SchedulingSpecialistAgent_WhenOrthopedicIssue_ProposesDoctorJamesWilson()
    {
        // Arrange
        var agent = new SchedulingSpecialistAgent(NullLogger<SchedulingSpecialistAgent>.Instance);
        var context = new ClinicalAgentContext(
            UserMessage: "Need to schedule an urgent orthopedic consultation for knee joint assessment.",
            PatientId: 5,
            Patient: null,
            KnownAllergies: new List<string>(),
            ActiveMedications: new List<string>(),
            PreviousDiagnoses: new List<string>(),
            RagSnippets: new List<string>()
        );

        // Act
        Assert.True(agent.CanHandle(context));
        var result = await agent.AnalyzeAsync(context);

        // Assert
        Assert.NotNull(result.ActionProposal);
        Assert.Equal("ScheduleConsultation", result.ActionProposal.ActionType);
        Assert.Contains("Dr. James Wilson", result.ActionProposal.DoctorName);
        Assert.Equal(5, result.ActionProposal.PatientId);
    }

    [Fact]
    public async Task ClinicalSupervisorOrchestrator_WhenMultiDomainCase_DispatchesSwarmAndReconcilesSafetyOverride()
    {
        // Arrange
        var pvAgent = new PharmacovigilanceAgent(NullLogger<PharmacovigilanceAgent>.Instance);
        var triageAgent = new DiagnosticTriageAgent(NullLogger<DiagnosticTriageAgent>.Instance);
        var schedAgent = new SchedulingSpecialistAgent(NullLogger<SchedulingSpecialistAgent>.Instance);

        var agents = new ISpecialistAgent[] { pvAgent, triageAgent, schedAgent };
        var supervisor = new ClinicalSupervisorOrchestrator(agents, NullLogger<ClinicalSupervisorOrchestrator>.Instance);

        var patient = new PatientResponseDto
        {
            Id = 10,
            FirstName = "Arthur",
            LastName = "Pendelton",
            MedicalNotes = "Atrial fibrillation on Warfarin 5mg daily. Severe allergy to penicillin.",
            Consultations = new List<ConsultationResponseDto>()
        };

        var query = "Patient reports acute knee swelling and pain. Can we prescribe Ibuprofen 400mg and book an orthopedic visit?";

        // Act
        Assert.True(supervisor.ShouldOrchestrate(query));
        var result = await supervisor.CoordinateAsync(query, 10, patient, Array.Empty<string>());

        // Assert
        Assert.Contains("Pharmacovigilance Specialist Agent", result.CollaboratingAgents);
        Assert.Contains("Clinical Diagnostic & Triage Agent", result.CollaboratingAgents);
        Assert.Contains("Clinical Scheduling & Operations Agent", result.CollaboratingAgents);

        // Pharmacovigilance warning should be surfaced
        Assert.NotEmpty(result.PharmacovigilanceWarnings);
        Assert.Contains(result.PharmacovigilanceWarnings, w => w.Contains("MAJOR CONTRAINDICATION", StringComparison.OrdinalIgnoreCase));

        // Scheduling proposal should have been reconciled to reflect medication safety review
        Assert.NotEmpty(result.ProposedActions);
        var proposal = result.ProposedActions[0];
        Assert.Contains("Contraindication Detected", proposal.DiagnosisOrReason);

        // Synthesized markdown should feature the specialist swarm analysis
        Assert.Contains("Specialist Swarm Analysis", result.SynthesizedResponse);
        Assert.Contains("Pharmacovigilance", result.SynthesizedResponse);
        Assert.Contains("Diagnostic & Triage", result.SynthesizedResponse);
        Assert.Contains("Scheduling & Operations", result.SynthesizedResponse);
    }
}
