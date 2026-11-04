using PatientManagement.Core.DTOs.Consultations;

namespace PatientManagement.Core.DTOs.Patients;

public class PatientResponseDto
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
    public string? MedicalNotes { get; set; }
    public string? ProfilePictureUrl { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public int ConsultationCount => Consultations.Count;
    public List<ConsultationResponseDto> Consultations { get; set; } = new();
}
