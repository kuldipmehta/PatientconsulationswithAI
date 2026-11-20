namespace PatientManagement.Core.DTOs.Ai;

public record LlmJudgeMetricScore(
    string MetricName,
    double Score,
    double MinPassingScore,
    string Explanation,
    bool Passed
);

public record LlmJudgeEvaluationRequest(
    string Query,
    string SourceContext,
    string GeneratedResponse,
    string? FeatureScope = null
);

public record LlmJudgeEvaluationResult(
    double OverallScore,
    bool Passed,
    IReadOnlyList<LlmJudgeMetricScore> Metrics,
    string ChainOfThoughtCritique,
    DateTime EvaluatedAtUtc
);
