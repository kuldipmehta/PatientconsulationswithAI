namespace PatientManagement.Core.Entities;

public class PendingAiAction
{
    public string ActionId { get; set; } = Guid.NewGuid().ToString("N");
    public int UserId { get; set; }
    public int PatientId { get; set; }
    public int? DoctorId { get; set; }
    public string ActionType { get; set; } = "ScheduleConsultation";
    public string Summary { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending"; // Pending, Confirmed, Cancelled, Expired
    public DateTime ExpiresAtUtc { get; set; } = DateTime.UtcNow.AddMinutes(30);
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAtUtc { get; set; }
}
