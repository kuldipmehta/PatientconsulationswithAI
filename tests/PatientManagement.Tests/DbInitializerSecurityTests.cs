using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using PatientManagement.Core.DTOs.Auth;
using PatientManagement.Infrastructure.Data;
using PatientManagement.Infrastructure.Services;
using Xunit;

namespace PatientManagement.Tests;

public class DbInitializerSecurityTests
{
    private const string ValidProdJwtKey = "PRODUCTION_TEST_SIGNING_KEY_STRONG_RANDOM_48_CHARS_99887766";

    [Fact]
    public async Task ProductionStartup_WithEmptyDatabase_DoesNotSeedAdminAccount()
    {
        // Arrange: Start with ASPNETCORE_ENVIRONMENT=Production and an empty database
        var dbName = $"ProdEmptyDb_{Guid.NewGuid():N}";
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Jwt:SigningKey"] = ValidProdJwtKey,
                        ["Database:AutoMigrateOnStartup"] = "false",
                        ["Swagger:Enabled"] = "false"
                    });
                });
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
                    services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                    services.RemoveAll<ApplicationDbContext>();
                    services.AddDbContext<ApplicationDbContext>(opt => opt.UseInMemoryDatabase(dbName));
                });
            });

        // Act
        var client = factory.CreateClient();
        var healthRes = await client.GetAsync("/health");
        healthRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Assert: No admin@patientportal.com account exists in DB
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var adminExists = await db.AppUsers.AnyAsync(u => u.Email == "admin@patientportal.com");
        adminExists.Should().BeFalse();
        (await db.AppUsers.CountAsync()).Should().Be(0);

        // Assert: Attempting login with old hardcoded password returns 401
        var loginRes = await client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
        {
            Email = "admin@patientportal.com",
            Password = "Admin@12345"
        });
        loginRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ProductionStartup_WithAutoMigrateOnStartupTrue_StillDoesNotSeedUsers()
    {
        // Arrange: Production with AutoMigrateOnStartup=true AND Seed passwords present
        var dbName = $"ProdAutoMigrateDb_{Guid.NewGuid():N}";
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Jwt:SigningKey"] = ValidProdJwtKey,
                        ["Database:AutoMigrateOnStartup"] = "true",
                        ["Seed:AdminPassword"] = "SomeSecretPass#12345",
                        ["Swagger:Enabled"] = "false"
                    });
                });
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
                    services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                    services.RemoveAll<ApplicationDbContext>();
                    services.AddDbContext<ApplicationDbContext>(opt => opt.UseInMemoryDatabase(dbName));
                });
            });

        // Act
        var client = factory.CreateClient();
        var healthRes = await client.GetAsync("/health");
        healthRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Assert: AutoMigrateOnStartup MUST NOT turn seeding on in Production
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.AppUsers.AnyAsync(u => u.Email == "admin@patientportal.com")).Should().BeFalse();
        (await db.AppUsers.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task DbInitializer_OutsideDevelopment_CreatesNoUsersEvenWhenCalledDirectly()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"DirectProdSeed_{Guid.NewGuid():N}")
            .Options;
        await using var db = new ApplicationDbContext(options);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Seed:AdminPassword"] = "CustomAdminPass#999",
                ["Seed:DoctorPassword"] = "CustomDocPass#999",
                ["Seed:ReceptionistPassword"] = "CustomReceptPass#999"
            })
            .Build();

        // Act
        await DbInitializer.SeedAsync(db, NullLogger.Instance, isDevelopment: false, config);

        // Assert
        (await db.AppUsers.CountAsync()).Should().Be(0);
        (await db.AppUsers.AnyAsync(u => u.Email == "admin@patientportal.com")).Should().BeFalse();
    }

    [Fact]
    public async Task DbInitializer_InDevelopment_ReadsPasswordsFromConfigurationNotHardcodedDefaults()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"DevConfigSeed_{Guid.NewGuid():N}")
            .Options;
        await using var db = new ApplicationDbContext(options);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Seed:AdminPassword"] = "UniqueUserSecretAdmin#42",
                ["Seed:DoctorPassword"] = "UniqueUserSecretDoc#42",
                ["Seed:ReceptionistPassword"] = "UniqueUserSecretRecept#42"
            })
            .Build();

        // Act
        await DbInitializer.SeedAsync(db, NullLogger.Instance, isDevelopment: true, config);

        // Assert
        var admin = await db.AppUsers.FirstOrDefaultAsync(u => u.Email == "admin@patientportal.com");
        admin.Should().NotBeNull();
        PasswordHasher.Verify("UniqueUserSecretAdmin#42", admin!.PasswordHash).Should().BeTrue();
        PasswordHasher.Verify("Admin@12345", admin.PasswordHash).Should().BeFalse();
    }

    [Fact]
    public async Task DbInitializer_InDevelopment_WithoutConfiguredPasswords_CreatesNoUsers()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"DevNoSecretSeed_{Guid.NewGuid():N}")
            .Options;
        await using var db = new ApplicationDbContext(options);

        var emptyConfig = new ConfigurationBuilder().Build();

        // Act
        await DbInitializer.SeedAsync(db, NullLogger.Instance, isDevelopment: true, emptyConfig);

        // Assert: Never falls back to hardcoded demo passwords
        (await db.AppUsers.CountAsync()).Should().Be(0);
    }
}
