using PatientManagement.Core.Enums;

namespace PatientManagement.Core.Interfaces;

public interface ICurrentUserService
{
    int? UserId { get; }
    string Email { get; }
    UserRole? Role { get; }
    int? DoctorId { get; }
    bool IsAuthenticated { get; }
}
