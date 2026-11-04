using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using PatientManagement.Core.DTOs.Consultations;
using PatientManagement.Core.Entities;
using PatientManagement.Core.Enums;
using PatientManagement.Infrastructure.Data;
using PatientManagement.Infrastructure.Services;
using Xunit;

namespace PatientManagement.Tests;

public class ConsultationServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly Mock<ILogger<ConsultationService>> _loggerMock;
    private readonly ConsultationService _consultationService;

    public ConsultationServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new ApplicationDbContext(options);
        _loggerMock = new Mock<ILogger<ConsultationService>>();
        _consultationService = new ConsultationService(_dbContext, _loggerMock.Object);
    }

    [Fact]
    public async Task CreateConsultationAsync_WithValidPatient_PersistsSuccessfully()
    {
        // Arrange
        var patient = new Patient
        {
            FirstName = "Emily",
            LastName = "Blunt",
            Email = "emily@example.com",
            Age = 32,
            Gender = "Female",
            CreatedAtUtc = DateTime.UtcNow
        };
        _dbContext.Patients.Add(patient);
        await _dbContext.SaveChangesAsync();

        var dto = new CreateConsultationDto
        {
            PatientId = patient.Id,
            DoctorName = "Dr. House",
            ConsultationDate = DateTime.UtcNow,
            Diagnosis = "Differential diagnosis for chronic fatigue",
            Symptoms = "Fatigue, mild muscle weakness",
            Notes = "Ordered MRI and complete blood panel.",
            Status = ConsultationStatus.Scheduled,
            Fee = 350.00m
        };

        // Act
        var result = await _consultationService.CreateConsultationAsync(dto);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().BeGreaterThan(0);
        result.DoctorName.Should().Be("Dr. House");
        result.PatientId.Should().Be(patient.Id);
        result.PatientName.Should().Be("Emily Blunt");
        result.Fee.Should().Be(350.00m);
    }

    [Fact]
    public async Task CreateConsultationAsync_WithNonExistentPatient_ThrowsKeyNotFoundException()
    {
        // Arrange
        var dto = new CreateConsultationDto
        {
            PatientId = 9999,
            DoctorName = "Dr. House",
            Diagnosis = "Checkup"
        };

        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _consultationService.CreateConsultationAsync(dto));
    }

    [Fact]
    public async Task UpdateConsultationAsync_ModifiesFieldsAndSaves()
    {
        // Arrange
        var patient = new Patient { FirstName = "John", LastName = "Watson", Email = "watson@example.com", Age = 42, Gender = "Male" };
        _dbContext.Patients.Add(patient);
        await _dbContext.SaveChangesAsync();

        var consultation = new Consultation
        {
            PatientId = patient.Id,
            DoctorName = "Dr. Holmes",
            ConsultationDate = DateTime.UtcNow,
            Diagnosis = "Observation",
            Status = ConsultationStatus.Scheduled
        };
        _dbContext.Consultations.Add(consultation);
        await _dbContext.SaveChangesAsync();

        var updateDto = new UpdateConsultationDto
        {
            DoctorName = "Dr. Holmes",
            ConsultationDate = DateTime.UtcNow,
            Diagnosis = "Resolved shoulder sprain",
            Status = ConsultationStatus.Completed,
            Fee = 180.00m,
            Notes = "Recovery verified."
        };

        // Act
        var updated = await _consultationService.UpdateConsultationAsync(consultation.Id, updateDto);

        // Assert
        updated.Should().NotBeNull();
        updated!.Diagnosis.Should().Be("Resolved shoulder sprain");
        updated.Status.Should().Be(ConsultationStatus.Completed);
        updated.Fee.Should().Be(180.00m);
    }

    public void Dispose()
    {
        _dbContext.Database.EnsureDeleted();
        _dbContext.Dispose();
    }
}
