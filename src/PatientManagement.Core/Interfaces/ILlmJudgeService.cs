using PatientManagement.Core.DTOs.Ai;

namespace PatientManagement.Core.Interfaces;

public interface ILlmJudgeService
{
    Task<LlmJudgeEvaluationResult> EvaluateAsync(
        LlmJudgeEvaluationRequest request,
        CancellationToken cancellationToken = default);
}
