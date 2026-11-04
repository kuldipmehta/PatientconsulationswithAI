namespace PatientManagement.Core.DTOs.Patients;

public class PatientSummaryDto
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
    public string? ProfilePictureUrl { get; set; }
    public int ConsultationCount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
