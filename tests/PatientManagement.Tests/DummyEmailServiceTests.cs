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
    public async Task SendPatientWelcomeEmailAsync_DispatchesEmailAndAuditsInDatabase()
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

        // Act
        var result = await _emailService.SendPatientWelcomeEmailAsync(patient);

        // Assert
        result.Should().BeTrue();

        // Verify sent email log in memory
        var sentEmails = await _emailService.GetSentEmailsAsync();
        sentEmails.Should().Contain(e => e.RecipientEmail == "grace.hopper@navy.mil" && e.PatientId == 101);

        // Verify audit log persisted in database
        var auditEntry = await _dbContext.EmailAuditLogs
            .FirstOrDefaultAsync(e => e.RecipientEmail == "grace.hopper@navy.mil");

        auditEntry.Should().NotBeNull();
        auditEntry!.Subject.Should().Contain("Registration Confirmed");
        auditEntry.Body.Should().Contain("Dear Grace Hopper");
        auditEntry.Status.Should().Contain("Delivered");
    }

    public void Dispose()
    {
        _dbContext.Database.EnsureDeleted();
        _dbContext.Dispose();
        _serviceProvider.Dispose();
    }
}
