namespace PatientManagement.Core.Entities;

public class EmailAuditLog
{
    public int Id { get; set; }
    public int? PatientId { get; set; }
    public string RecipientEmail { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string Status { get; set; } = "Sent";
    public DateTime SentAtUtc { get; set; } = DateTime.UtcNow;
}
