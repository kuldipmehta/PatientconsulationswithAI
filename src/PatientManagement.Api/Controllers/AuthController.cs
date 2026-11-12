using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PatientManagement.Core.DTOs.Auth;
using PatientManagement.Core.DTOs.Common;
using PatientManagement.Core.Interfaces;

namespace PatientManagement.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ICurrentUserService _currentUser;

    public AuthController(IAuthService authService, ICurrentUserService currentUser)
    {
        _authService = authService;
        _currentUser = currentUser;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(ApiResponse<LoginResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _authService.LoginAsync(request, cancellationToken);
        if (result == null)
        {
            return Unauthorized(ApiResponse<object>.Fail("Invalid email or password."));
        }

        return Ok(ApiResponse<LoginResponseDto>.Ok(result, "Authenticated successfully."));
    }

    [HttpPost("set-password")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SetPassword([FromBody] SetPasswordRequestDto request, CancellationToken cancellationToken)
    {
        var success = await _authService.SetPasswordAsync(request, cancellationToken);
        if (!success)
        {
            return BadRequest(ApiResponse<object>.Fail("Invalid, expired, or already-used password setup token."));
        }

        return Ok(ApiResponse<bool>.Ok(true, "Password configured successfully. You may now log in."));
    }

    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<UserProfileDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        if (!_currentUser.UserId.HasValue)
        {
            return Unauthorized(ApiResponse<object>.Fail("User identity could not be resolved."));
        }

        var profile = await _authService.GetProfileAsync(_currentUser.UserId.Value, cancellationToken);
        return profile == null
            ? Unauthorized(ApiResponse<object>.Fail("User account is inactive or missing."))
            : Ok(ApiResponse<UserProfileDto>.Ok(profile, "Profile retrieved."));
    }
}
