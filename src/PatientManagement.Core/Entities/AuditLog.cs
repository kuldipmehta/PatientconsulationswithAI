namespace PatientManagement.Core.Entities;

public class AuditLog
{
    public long Id { get; set; }
    public int? UserId { get; set; }
    public string UserEmail { get; set; } = "system";
    public string UserRole { get; set; } = "System";
    public string Action { get; set; } = string.Empty; // Created, Updated, SoftDeleted
    public string EntityName { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string? ChangesJson { get; set; }
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
}
