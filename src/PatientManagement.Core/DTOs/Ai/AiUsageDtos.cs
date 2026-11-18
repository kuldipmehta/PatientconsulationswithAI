namespace PatientManagement.Core.DTOs.Ai;

public class AiUsageSummaryDto
{
    public DateTime? FromUtc { get; set; }
    public DateTime? ToUtc { get; set; }
    public int TotalRequests { get; set; }
    public long TotalPromptTokens { get; set; }
    public long TotalCompletionTokens { get; set; }
    public long TotalTokens { get; set; }
    public decimal TotalEstimatedCostUsd { get; set; }
    public long OverallP95LatencyMs { get; set; }
    public List<AiUsageDailyModelRowDto> Breakdown { get; set; } = new();
}

public class AiUsageDailyModelRowDto
{
    public string DayUtc { get; set; } = string.Empty;
    public string ModelUsed { get; set; } = string.Empty;
    public int Requests { get; set; }
    public long PromptTokens { get; set; }
    public long CompletionTokens { get; set; }
    public long TotalTokens { get; set; }
    public decimal EstimatedCostUsd { get; set; }
    public long P95LatencyMs { get; set; }
}
