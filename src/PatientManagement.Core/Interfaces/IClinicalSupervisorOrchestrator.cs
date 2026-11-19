using PatientManagement.Core.DTOs.Ai;
using PatientManagement.Core.DTOs.Patients;

namespace PatientManagement.Core.Interfaces;

public interface IClinicalSupervisorOrchestrator
{
    /// <summary>
    /// Determines whether the user query is a complex multi-discipline clinical question
    /// that warrants coordination across the specialist agent swarm.
    /// </summary>
    bool ShouldOrchestrate(string userMessage);

    /// <summary>
    /// Coordinates the specialist micro-agents (Pharmacovigilance, Diagnostic Triage, Scheduling)
    /// and synthesizes their independent analyses into a coherent clinical decision support result.
    /// </summary>
    Task<MultiAgentOrchestrationResult> CoordinateAsync(
        string userMessage,
        int? patientId,
        PatientResponseDto? patient,
        IReadOnlyList<string> ragSnippets,
        CancellationToken cancellationToken = default);
}
