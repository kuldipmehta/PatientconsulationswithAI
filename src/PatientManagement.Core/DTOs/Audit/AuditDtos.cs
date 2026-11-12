namespace PatientManagement.Core.DTOs.Audit;

public class AuditLogDto
{
    public long Id { get; set; }
    public int? UserId { get; set; }
    public string UserEmail { get; set; } = string.Empty;
    public string UserRole { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string? ChangesJson { get; set; }
    public DateTime TimestampUtc { get; set; }
}

public class AiInteractionLogDto
{
    public long Id { get; set; }
    public int? UserId { get; set; }
    public string UserEmail { get; set; } = string.Empty;
    public string UserRole { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public int? PatientId { get; set; }
    public string SanitizedPrompt { get; set; } = string.Empty;
    public string? ToolCallsJson { get; set; }
    public string ResponseSummary { get; set; } = string.Empty;
    public string ModelUsed { get; set; } = string.Empty;
    public bool IsSimulated { get; set; }
    public long DurationMs { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
