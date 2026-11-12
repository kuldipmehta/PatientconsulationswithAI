using System.ComponentModel.DataAnnotations;

namespace PatientManagement.Core.Entities;

public class Patient
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public int Age { get; set; }
    public string Gender { get; set; } = string.Empty;
    public DateTime? DateOfBirth { get; set; }
    public string? Address { get; set; }
    public string? MedicalNotes { get; set; }
    public string? ProfilePictureUrl { get; set; }
    public string? ProfilePictureBlobName { get; set; }

    public int? PrimaryDoctorId { get; set; }
    public Doctor? PrimaryDoctor { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public int? DeletedByUserId { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }

    [Timestamp]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public ICollection<Consultation> Consultations { get; set; } = new List<Consultation>();
}
