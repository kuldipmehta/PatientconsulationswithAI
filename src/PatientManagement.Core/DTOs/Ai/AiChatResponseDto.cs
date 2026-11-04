namespace PatientManagement.Core.DTOs.Ai;

public class AiChatResponseDto
{
    public string Reply { get; set; } = string.Empty;
    public List<string> ToolsExecuted { get; set; } = new();
    public object? ExtractedData { get; set; }
    public bool Success { get; set; } = true;
    public string? ModelUsed { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
