using System.Security.Claims;
using PatientManagement.Core.Enums;
using PatientManagement.Core.Interfaces;

namespace PatientManagement.Api.Services;

public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal? Principal => _httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public int? UserId
    {
        get
        {
            var raw = Principal?.FindFirstValue(ClaimTypes.NameIdentifier) ?? Principal?.FindFirstValue("sub");
            return int.TryParse(raw, out var id) ? id : null;
        }
    }

    public string Email =>
        Principal?.FindFirstValue(ClaimTypes.Email)
        ?? Principal?.FindFirstValue("email")
        ?? "system";

    public UserRole? Role
    {
        get
        {
            var raw = Principal?.FindFirstValue(ClaimTypes.Role) ?? Principal?.FindFirstValue("roles");
            return Enum.TryParse<UserRole>(raw, true, out var role) ? role : null;
        }
    }

    public int? DoctorId
    {
        get
        {
            var raw = Principal?.FindFirstValue("doctor_id");
            return int.TryParse(raw, out var id) ? id : null;
        }
    }
}
