using PatientManagement.Core.DTOs.Patients;
using PatientManagement.Core.Enums;

namespace PatientManagement.Core.DTOs.Ai;

public record ClinicalAgentContext(
    string UserMessage,
    int? PatientId,
    PatientResponseDto? Patient,
    IReadOnlyList<string> KnownAllergies,
    IReadOnlyList<string> ActiveMedications,
    IReadOnlyList<string> PreviousDiagnoses,
    IReadOnlyList<string> RagSnippets
);

public record SpecialistAnalysisResult(
    string AgentName,
    AgentSpecialty Specialty,
    string Summary,
    List<string> Recommendations,
    List<string> Warnings,
    bool RequiresUrgentEscalation = false,
    PendingActionProposalDto? ActionProposal = null
);

public record MultiAgentOrchestrationResult(
    List<string> CollaboratingAgents,
    string SynthesizedResponse,
    List<string> PharmacovigilanceWarnings,
    List<PendingActionProposalDto> ProposedActions,
    bool IsSimulated = false
);
