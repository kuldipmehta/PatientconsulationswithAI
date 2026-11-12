using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using PatientManagement.Core.Entities;
using PatientManagement.Infrastructure.Data;
using PatientManagement.Infrastructure.Services;
using Xunit;

namespace PatientManagement.Tests;

public class DummyEmailServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ServiceProvider _serviceProvider;
    private readonly DummyEmailService _emailService;

    public DummyEmailServiceTests()
    {
        var services = new ServiceCollection();

        var dbName = Guid.NewGuid().ToString();
        services.AddDbContext<ApplicationDbContext>(opt => opt.UseInMemoryDatabase(dbName));
        services.AddLogging(builder => builder.AddConsole());

        _serviceProvider = services.BuildServiceProvider();
        _dbContext = _serviceProvider.GetRequiredService<ApplicationDbContext>();

        var loggerMock = new Mock<ILogger<DummyEmailService>>();
        var scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();

        _emailService = new DummyEmailService(loggerMock.Object, scopeFactory);
    }

    [Fact]
    public async Task SendPatientWelcomeEmailAsync_DispatchesEmailAndAuditsWithoutLeakingCredentials()
    {
        // Arrange
        var patient = new Patient
        {
            Id = 101,
            FirstName = "Grace",
            LastName = "Hopper",
            Email = "grace.hopper@navy.mil",
            Age = 85,
            Gender = "Female",
            ProfilePictureUrl = "https://example.com/grace.jpg",
            CreatedAtUtc = DateTime.UtcNow
        };
        var setupLink = "https://portal.example.com/set-password?token=one-time-token-123";

        // Act
        var result = await _emailService.SendPatientWelcomeEmailAsync(patient, setupLink);

        // Assert
        result.Should().BeTrue();

        var sentEmails = await _emailService.GetSentEmailsAsync();
        sentEmails.Should().Contain(e => e.RecipientEmail == "grace.hopper@navy.mil" && e.PatientId == 101);

        var auditEntry = await _dbContext.EmailAuditLogs
            .FirstOrDefaultAsync(e => e.RecipientEmail == "grace.hopper@navy.mil");

        auditEntry.Should().NotBeNull();
        auditEntry!.Subject.Should().Contain("Registration Confirmed");
        auditEntry.Body.Should().Contain("Dear Grace Hopper");
        auditEntry.Body.Should().Contain("one-time-token-123");
        auditEntry.Body.Should().NotContain("Temporary Password");
        auditEntry.Body.Should().NotContain("Patient Credentials");
        auditEntry.Status.Should().Contain("Delivered");
    }

    public void Dispose()
    {
        _dbContext.Database.EnsureDeleted();
        _dbContext.Dispose();
        _serviceProvider.Dispose();
    }
}
