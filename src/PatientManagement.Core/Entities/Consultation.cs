using System.ComponentModel.DataAnnotations;
using PatientManagement.Core.Enums;

namespace PatientManagement.Core.Entities;

public class Consultation
{
    public int Id { get; set; }
    public int PatientId { get; set; }
    public Patient Patient { get; set; } = null!;

    public int? DoctorId { get; set; }
    public Doctor? Doctor { get; set; }

    public string DoctorName { get; set; } = string.Empty;
    public DateTime ConsultationDate { get; set; }
    public int DurationMinutes { get; set; } = 30;
    public string Diagnosis { get; set; } = string.Empty;
    public string? Symptoms { get; set; }
    public string? Notes { get; set; }
    public string? TreatmentPlan { get; set; }
    public string? Prescription { get; set; }
    public ConsultationStatus Status { get; set; } = ConsultationStatus.Scheduled;
    public decimal? Fee { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public int? DeletedByUserId { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }

    [Timestamp]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
