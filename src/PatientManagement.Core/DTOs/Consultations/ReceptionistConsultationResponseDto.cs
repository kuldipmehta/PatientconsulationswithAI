using PatientManagement.Core.Enums;

namespace PatientManagement.Core.DTOs.Consultations;

/// <summary>
/// Least-privilege consultation DTO returned to Receptionist users.
/// Omits clinical fields (Diagnosis, Symptoms, Notes, TreatmentPlan, Prescription).
/// </summary>
public class ReceptionistConsultationResponseDto
{
    public int Id { get; set; }
    public int PatientId { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public int? DoctorId { get; set; }
    public string DoctorName { get; set; } = string.Empty;
    public DateTime ConsultationDate { get; set; }
    public int DurationMinutes { get; set; }
    public ConsultationStatus Status { get; set; }
    public string StatusName => Status.ToString();
    public decimal? Fee { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }

    public static ReceptionistConsultationResponseDto FromFull(ConsultationResponseDto c) => new()
    {
        Id = c.Id,
        PatientId = c.PatientId,
        PatientName = c.PatientName,
        DoctorId = c.DoctorId,
        DoctorName = c.DoctorName,
        ConsultationDate = c.ConsultationDate,
        DurationMinutes = c.DurationMinutes,
        Status = c.Status,
        Fee = c.Fee,
        RowVersion = c.RowVersion,
        CreatedAtUtc = c.CreatedAtUtc,
        UpdatedAtUtc = c.UpdatedAtUtc
    };
}
