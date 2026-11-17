using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using PatientManagement.Infrastructure.Data;
using Xunit;

namespace PatientManagement.Tests;

public class JwtSecurityTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string OldPublicDevKey = "DEV_ONLY_REPLACE_IN_PRODUCTION_WITH_32+_CHAR_KEY_1234567890";
    private readonly CustomWebApplicationFactory _factory;

    public JwtSecurityTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task TokenSignedWithOldDevKey_Returns401Unauthorized()
    {
        // Arrange: Forge a valid-looking Admin JWT signed with the removed public dev key
        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(OldPublicDevKey));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, "1"),
            new Claim(ClaimTypes.NameIdentifier, "1"),
            new Claim(JwtRegisteredClaimNames.Email, "admin@patientportal.com"),
            new Claim(ClaimTypes.Role, "Admin")
        };

        var forgedJwt = new JwtSecurityToken(
            issuer: "PatientManagement.Api",
            audience: "PatientManagement.Client",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);

        var forgedToken = new JwtSecurityTokenHandler().WriteToken(forgedJwt);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", forgedToken);

        // Act
        var response = await client.GetAsync("/api/patients");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public void ProductionStartup_WithoutJwtSigningKey_RefusesToStart()
    {
        // Arrange: Production environment with NO Jwt:SigningKey configured (relying on appsettings.json)
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Database:AutoMigrateOnStartup"] = "false",
                        ["Swagger:Enabled"] = "false"
                    });
                });
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
                    services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                    services.RemoveAll<ApplicationDbContext>();
                    services.AddDbContext<ApplicationDbContext>(opt =>
                        opt.UseInMemoryDatabase($"ProdNoKey_{Guid.NewGuid():N}"));
                });
            });

        // Act
        Action act = () => factory.CreateClient();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Jwt:SigningKey must be configured*");
    }

    [Fact]
    public void ProductionStartup_WithShortJwtSigningKey_RefusesToStart()
    {
        // Arrange: Production environment with a key shorter than 32 characters
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Jwt:SigningKey"] = "too_short_key_under_32_chars",
                        ["Database:AutoMigrateOnStartup"] = "false"
                    });
                });
            });

        // Act
        Action act = () => factory.CreateClient();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Jwt:SigningKey must be configured*");
    }

    [Fact]
    public void ProductionStartup_WithDevOnlyPrefixedKey_RefusesToStart()
    {
        // Arrange: Production environment with a key starting with DEV_ONLY (even if >= 32 chars)
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Jwt:SigningKey"] = OldPublicDevKey,
                        ["Database:AutoMigrateOnStartup"] = "false"
                    });
                });
            });

        // Act
        Action act = () => factory.CreateClient();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Jwt:SigningKey must be configured*");
    }
}
