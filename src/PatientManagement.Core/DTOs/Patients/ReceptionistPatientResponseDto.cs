using PatientManagement.Core.DTOs.Consultations;

namespace PatientManagement.Core.DTOs.Patients;

/// <summary>
/// Least-privilege patient detail DTO returned to Receptionist users.
/// Omits MedicalNotes and clinical consultation fields (Diagnosis, Symptoms, Notes, TreatmentPlan, Prescription).
/// </summary>
public class ReceptionistPatientResponseDto
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string FullName => $"{FirstName} {LastName}".Trim();
    public string Email { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public int Age { get; set; }
    public string Gender { get; set; } = string.Empty;
    public DateTime? DateOfBirth { get; set; }
    public string? Address { get; set; }
    public string? ProfilePictureUrl { get; set; }
    public int? PrimaryDoctorId { get; set; }
    public string? PrimaryDoctorName { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public int ConsultationCount => Consultations.Count;
    public List<ReceptionistConsultationResponseDto> Consultations { get; set; } = new();

    public static ReceptionistPatientResponseDto FromFull(PatientResponseDto p) => new()
    {
        Id = p.Id,
        FirstName = p.FirstName,
        LastName = p.LastName,
        Email = p.Email,
        PhoneNumber = p.PhoneNumber,
        Age = p.Age,
        Gender = p.Gender,
        DateOfBirth = p.DateOfBirth,
        Address = p.Address,
        ProfilePictureUrl = p.ProfilePictureUrl,
        PrimaryDoctorId = p.PrimaryDoctorId,
        PrimaryDoctorName = p.PrimaryDoctorName,
        RowVersion = p.RowVersion,
        CreatedAtUtc = p.CreatedAtUtc,
        UpdatedAtUtc = p.UpdatedAtUtc,
        Consultations = p.Consultations.Select(ReceptionistConsultationResponseDto.FromFull).ToList()
    };
}
