using PatientManagement.Core.Enums;

namespace PatientManagement.Core.Entities;

public class AppUser
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? PasswordHash { get; set; }
    public UserRole Role { get; set; } = UserRole.Receptionist;
    public int? DoctorId { get; set; }
    public Doctor? Doctor { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<PasswordSetupToken> PasswordSetupTokens { get; set; } = new List<PasswordSetupToken>();
}
