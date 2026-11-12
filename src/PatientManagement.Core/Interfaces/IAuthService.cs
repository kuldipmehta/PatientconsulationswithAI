using PatientManagement.Core.DTOs.Auth;

namespace PatientManagement.Core.Interfaces;

public interface IAuthService
{
    Task<LoginResponseDto?> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken = default);
    Task<bool> SetPasswordAsync(SetPasswordRequestDto request, CancellationToken cancellationToken = default);
    Task<UserProfileDto?> GetProfileAsync(int userId, CancellationToken cancellationToken = default);
    Task<string> IssuePasswordSetupTokenAsync(int userId, CancellationToken cancellationToken = default);
}
