using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using PatientManagement.Core.DTOs.Consultations;
using PatientManagement.Core.Entities;
using PatientManagement.Core.Enums;
using PatientManagement.Core.Exceptions;
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
            ConsultationDate = DateTime.UtcNow.AddDays(1),
            DurationMinutes = 30,
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
    public async Task CreateConsultationAsync_WithOverlappingDoctorSlot_ThrowsBookingConflictException()
    {
        // Arrange
        var doctor = new Doctor
        {
            FullName = "Dr. Sarah Jenkins",
            Specialty = "Cardiology",
            Email = "sarah.jenkins@patientportal.com"
        };
        var patient = new Patient
        {
            FirstName = "Arthur",
            LastName = "Dent",
            Email = "arthur@example.com",
            Age = 40,
            Gender = "Male"
        };
        _dbContext.Doctors.Add(doctor);
        _dbContext.Patients.Add(patient);
        await _dbContext.SaveChangesAsync();

        var slotStart = new DateTime(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc);
        await _consultationService.CreateConsultationAsync(new CreateConsultationDto
        {
            PatientId = patient.Id,
            DoctorId = doctor.Id,
            DoctorName = doctor.FullName,
            ConsultationDate = slotStart,
            DurationMinutes = 30,
            Diagnosis = "Initial checkup"
        });

        // Act & Assert: 10:15 overlaps 10:00-10:30
        await Assert.ThrowsAsync<BookingConflictException>(() =>
            _consultationService.CreateConsultationAsync(new CreateConsultationDto
            {
                PatientId = patient.Id,
                DoctorId = doctor.Id,
                DoctorName = doctor.FullName,
                ConsultationDate = slotStart.AddMinutes(15),
                DurationMinutes = 30,
                Diagnosis = "Overlapping booking attempt"
            }));
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
            ConsultationDate = DateTime.UtcNow.AddHours(2),
            DurationMinutes = 30,
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

    [Fact]
    public async Task DeleteConsultationAsync_SoftDeletesRecord()
    {
        // Arrange
        var patient = new Patient { FirstName = "Clara", LastName = "Oswald", Email = "clara@example.com", Age = 27, Gender = "Female" };
        _dbContext.Patients.Add(patient);
        await _dbContext.SaveChangesAsync();

        var consultation = new Consultation
        {
            PatientId = patient.Id,
            DoctorName = "Dr. Who",
            ConsultationDate = DateTime.UtcNow.AddDays(1),
            Diagnosis = "Annual Exam",
            Status = ConsultationStatus.Scheduled
        };
        _dbContext.Consultations.Add(consultation);
        await _dbContext.SaveChangesAsync();

        // Act
        var deleted = await _consultationService.DeleteConsultationAsync(consultation.Id);

        // Assert
        deleted.Should().BeTrue();
        (await _dbContext.Consultations.FirstOrDefaultAsync(c => c.Id == consultation.Id)).Should().BeNull();
        var archived = await _dbContext.Consultations.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == consultation.Id);
        archived.Should().NotBeNull();
        archived!.IsDeleted.Should().BeTrue();
    }

    public void Dispose()
    {
        _dbContext.Database.EnsureDeleted();
        _dbContext.Dispose();
    }
}
