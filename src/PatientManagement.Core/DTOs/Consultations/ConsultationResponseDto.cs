using PatientManagement.Core.Enums;

namespace PatientManagement.Core.DTOs.Consultations;

public class ConsultationResponseDto
{
    public int Id { get; set; }
    public int PatientId { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string DoctorName { get; set; } = string.Empty;
    public DateTime ConsultationDate { get; set; }
    public string Diagnosis { get; set; } = string.Empty;
    public string? Symptoms { get; set; }
    public string? Notes { get; set; }
    public string? TreatmentPlan { get; set; }
    public string? Prescription { get; set; }
    public ConsultationStatus Status { get; set; }
    public string StatusName => Status.ToString();
    public decimal? Fee { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}
