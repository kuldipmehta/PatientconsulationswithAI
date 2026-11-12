using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using PatientManagement.Core.DTOs.Auth;
using PatientManagement.Core.Entities;
using PatientManagement.Core.Interfaces;
using PatientManagement.Infrastructure.Data;

namespace PatientManagement.Infrastructure.Services;

public static class PasswordHasher
{
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 100_000;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return $"{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";
    }

    public static bool Verify(string password, string? storedHash)
    {
        if (string.IsNullOrWhiteSpace(storedHash)) return false;
        var parts = storedHash.Split('.');
        if (parts.Length != 2) return false;

        try
        {
            var salt = Convert.FromBase64String(parts[0]);
            var expectedKey = Convert.FromBase64String(parts[1]);
            var actualKey = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
            return CryptographicOperations.FixedTimeEquals(actualKey, expectedKey);
        }
        catch
        {
            return false;
        }
    }

    public static string Sha256Hex(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }
}

public class AuthService : IAuthService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        ApplicationDbContext dbContext,
        IConfiguration configuration,
        ILogger<AuthService> logger)
    {
        _dbContext = dbContext;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<LoginResponseDto?> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await _dbContext.AppUsers
            .FirstOrDefaultAsync(u => u.Email == email && u.IsActive, cancellationToken);

        if (user == null || !PasswordHasher.Verify(request.Password, user.PasswordHash))
        {
            _logger.LogWarning("Failed login attempt for email {Email}.", email);
            return null;
        }

        var (token, expiresAtUtc) = GenerateJwtToken(user);
        _logger.LogInformation("User {Email} ({Role}) authenticated successfully.", user.Email, user.Role);

        return new LoginResponseDto
        {
            AccessToken = token,
            ExpiresAtUtc = expiresAtUtc,
            User = MapProfile(user)
        };
    }

    public async Task<string> IssuePasswordSetupTokenAsync(int userId, CancellationToken cancellationToken = default)
    {
        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');

        var entity = new PasswordSetupToken
        {
            UserId = userId,
            TokenHash = PasswordHasher.Sha256Hex(rawToken),
            ExpiresAtUtc = DateTime.UtcNow.AddHours(24),
            CreatedAtUtc = DateTime.UtcNow
        };

        _dbContext.PasswordSetupTokens.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return rawToken;
    }

    public async Task<bool> SetPasswordAsync(SetPasswordRequestDto request, CancellationToken cancellationToken = default)
    {
        var tokenHash = PasswordHasher.Sha256Hex(request.Token.Trim());
        var tokenEntity = await _dbContext.PasswordSetupTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

        if (tokenEntity == null || tokenEntity.UsedAtUtc != null || tokenEntity.ExpiresAtUtc < DateTime.UtcNow)
        {
            return false;
        }

        tokenEntity.User.PasswordHash = PasswordHasher.Hash(request.NewPassword);
        tokenEntity.UsedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("One-time password setup token redeemed for user {UserId}.", tokenEntity.UserId);
        return true;
    }

    public async Task<UserProfileDto?> GetProfileAsync(int userId, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.AppUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId && u.IsActive, cancellationToken);

        return user == null ? null : MapProfile(user);
    }

    private (string Token, DateTime ExpiresAtUtc) GenerateJwtToken(AppUser user)
    {
        var signingKey = _configuration["Jwt:SigningKey"]
            ?? Environment.GetEnvironmentVariable("JWT_SIGNING_KEY")
            ?? "DEV_ONLY_REPLACE_IN_PRODUCTION_WITH_32+_CHAR_KEY_1234567890";

        var issuer = _configuration["Jwt:Issuer"] ?? "PatientManagement.Api";
        var audience = _configuration["Jwt:Audience"] ?? "PatientManagement.Client";
        var expiryMinutes = int.TryParse(_configuration["Jwt:ExpiryMinutes"], out var mins) ? mins : 480;

        var expiresAt = DateTime.UtcNow.AddMinutes(expiryMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Role, user.Role.ToString())
        };

        if (user.DoctorId.HasValue)
        {
            claims.Add(new Claim("doctor_id", user.DoctorId.Value.ToString()));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var jwt = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(jwt), expiresAt);
    }

    private static UserProfileDto MapProfile(AppUser user) => new()
    {
        Id = user.Id,
        Email = user.Email,
        FullName = user.FullName,
        Role = user.Role,
        DoctorId = user.DoctorId
    };
}
