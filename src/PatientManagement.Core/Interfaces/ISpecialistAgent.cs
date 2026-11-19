using PatientManagement.Core.DTOs.Ai;
using PatientManagement.Core.Enums;

namespace PatientManagement.Core.Interfaces;

public interface ISpecialistAgent
{
    string AgentName { get; }
    AgentSpecialty Specialty { get; }
    bool CanHandle(ClinicalAgentContext context);
    Task<SpecialistAnalysisResult> AnalyzeAsync(ClinicalAgentContext context, CancellationToken cancellationToken = default);
}
