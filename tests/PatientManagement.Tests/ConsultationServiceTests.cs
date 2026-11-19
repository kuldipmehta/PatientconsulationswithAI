using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using PatientManagement.Core.DTOs.Consultations;
using PatientManagement.Core.Entities;
using PatientManagement.Core.Enums;
using PatientManagement.Core.Exceptions;
using PatientManagement.Core.Interfaces;
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

    [Fact]
    public async Task CreateConsultationAsync_WhenDoctorBAttemptsToAttachToDoctorAPatientWithOwnDoctorId_ThrowsForbiddenAccessException()
    {
        // Arrange: Doctor A owns the patient (PrimaryDoctorId = 1)
        var doctorA = new Doctor { Id = 1, FullName = "Dr. Sarah Jenkins, MD", Specialty = "Internal Medicine", Email = "sarah@example.com" };
        var doctorB = new Doctor { Id = 2, FullName = "Dr. Robert Sterling", Specialty = "Cardiology", Email = "robert@example.com" };
        var patientOfA = new Patient
        {
            FirstName = "Alice",
            LastName = "PatientA",
            Email = "alice.a@example.com",
            Age = 35,
            Gender = "Female",
            PrimaryDoctorId = doctorA.Id
        };
        _dbContext.Doctors.AddRange(doctorA, doctorB);
        _dbContext.Patients.Add(patientOfA);
        await _dbContext.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.SetupGet(u => u.IsAuthenticated).Returns(true);
        currentUserMock.SetupGet(u => u.Role).Returns(UserRole.Doctor);
        currentUserMock.SetupGet(u => u.DoctorId).Returns(doctorB.Id);

        var sut = new ConsultationService(_dbContext, _loggerMock.Object, currentUserMock.Object);

        // Act & Assert: Doctor B supplies their own DoctorId = 2 to try to attach to Doctor A's patient
        var dto = new CreateConsultationDto
        {
            PatientId = patientOfA.Id,
            DoctorId = doctorB.Id,
            DoctorName = doctorB.FullName,
            ConsultationDate = DateTime.UtcNow.AddDays(2),
            DurationMinutes = 30,
            Diagnosis = "Unauthorized attachment attempt",
            Status = ConsultationStatus.Scheduled
        };

        await Assert.ThrowsAsync<ForbiddenAccessException>(() => sut.CreateConsultationAsync(dto));
    }

    [Fact]
    public async Task CreateConsultationAsync_WhenDoctorSuppliesAnotherDoctorIdForOwnPatient_ForcesCurrentDoctorId()
    {
        // Arrange: Doctor A owns the patient (PrimaryDoctorId = 1)
        var doctorA = new Doctor { Id = 10, FullName = "Dr. Sarah Jenkins, MD", Specialty = "Internal Medicine", Email = "sarah@example.com" };
        var doctorB = new Doctor { Id = 20, FullName = "Dr. Robert Sterling", Specialty = "Cardiology", Email = "robert@example.com" };
        var patientOfA = new Patient
        {
            FirstName = "Bob",
            LastName = "PatientA",
            Email = "bob.a@example.com",
            Age = 44,
            Gender = "Male",
            PrimaryDoctorId = doctorA.Id
        };
        _dbContext.Doctors.AddRange(doctorA, doctorB);
        _dbContext.Patients.Add(patientOfA);
        await _dbContext.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.SetupGet(u => u.IsAuthenticated).Returns(true);
        currentUserMock.SetupGet(u => u.Role).Returns(UserRole.Doctor);
        currentUserMock.SetupGet(u => u.DoctorId).Returns(doctorA.Id);

        var sut = new ConsultationService(_dbContext, _loggerMock.Object, currentUserMock.Object);

        // Act: Doctor A sends Doctor B's ID in the request body
        var dto = new CreateConsultationDto
        {
            PatientId = patientOfA.Id,
            DoctorId = doctorB.Id,
            DoctorName = doctorB.FullName,
            ConsultationDate = DateTime.UtcNow.AddDays(3),
            DurationMinutes = 30,
            Diagnosis = "Routine follow-up",
            Status = ConsultationStatus.Scheduled
        };

        var result = await sut.CreateConsultationAsync(dto);

        // Assert: Consultation is forced to Doctor A (current doctor), ignoring dto.DoctorId
        result.DoctorId.Should().Be(doctorA.Id);
        result.DoctorName.Should().Be(doctorA.FullName);
    }

    [Fact]
    public async Task DeleteConsultationAsync_RemovesItsRagChunks_ButKeepsOtherChunks()
    {
        var patient = new Patient { FirstName = "Rag", LastName = "Cleanup", Email = "ragcleanup@example.com", Age = 40, Gender = "Male", CreatedAtUtc = DateTime.UtcNow };
        _dbContext.Patients.Add(patient);
        await _dbContext.SaveChangesAsync();

        var consultation = new Consultation
        {
            PatientId = patient.Id,
            DoctorName = "Dr. House",
            ConsultationDate = DateTime.UtcNow.AddDays(-1),
            Diagnosis = "Confidential diagnosis text",
            Status = ConsultationStatus.Completed
        };
        _dbContext.Consultations.Add(consultation);
        await _dbContext.SaveChangesAsync();

        _dbContext.PatientDocumentChunks.AddRange(
            new PatientDocumentChunk
            {
                PatientId = patient.Id,
                ConsultationId = consultation.Id,
                SourceType = "Consultation",
                SourceLabel = "Consultation chunk",
                TextContent = "Diagnosis: Confidential diagnosis text",
                EmbeddingVectorJson = "[0.1,0.2]",
                EmbeddingDimensions = 2
            },
            new PatientDocumentChunk
            {
                PatientId = patient.Id,
                SourceType = "PatientDocument",
                SourceLabel = "Lab report chunk",
                TextContent = "SGPT 68 U/L",
                EmbeddingVectorJson = "[0.3,0.4]",
                EmbeddingDimensions = 2
            });
        await _dbContext.SaveChangesAsync();

        var deleted = await _consultationService.DeleteConsultationAsync(consultation.Id);

        deleted.Should().BeTrue();
        var remaining = await _dbContext.PatientDocumentChunks.IgnoreQueryFilters().ToListAsync();
        remaining.Should().ContainSingle();
        remaining[0].SourceType.Should().Be("PatientDocument");
    }

    [Fact]
    public async Task UpdateConsultationAsync_AsReceptionist_ReschedulesButLeavesClinicalFieldsIntact()
    {
        var patient = new Patient { FirstName = "Resched", LastName = "Ule", Email = "resched@example.com", Age = 35, Gender = "Female", CreatedAtUtc = DateTime.UtcNow };
        _dbContext.Patients.Add(patient);
        await _dbContext.SaveChangesAsync();

        var originalDate = DateTime.UtcNow.AddDays(2);
        var consultation = new Consultation
        {
            PatientId = patient.Id,
            DoctorName = "Dr. House",
            ConsultationDate = originalDate,
            DurationMinutes = 30,
            Diagnosis = "Type 2 diabetes",
            Symptoms = "Thirst",
            Notes = "Confidential clinical note",
            TreatmentPlan = "Diet and metformin",
            Prescription = "Metformin 500mg",
            Status = ConsultationStatus.Scheduled
        };
        _dbContext.Consultations.Add(consultation);
        await _dbContext.SaveChangesAsync();

        var receptionist = new Mock<ICurrentUserService>();
        receptionist.Setup(u => u.IsAuthenticated).Returns(true);
        receptionist.Setup(u => u.Role).Returns(UserRole.Receptionist);
        receptionist.Setup(u => u.UserId).Returns(7);
        var sut = new ConsultationService(_dbContext, _loggerMock.Object, receptionist.Object);

        var newDate = originalDate.AddDays(3);
        var result = await sut.UpdateConsultationAsync(consultation.Id, new UpdateConsultationDto
        {
            DoctorName = "Dr. House",
            ConsultationDate = newDate,
            DurationMinutes = 45,
            Status = ConsultationStatus.Scheduled,
            // A receptionist's form sends no clinical content at all
            Diagnosis = string.Empty,
            Symptoms = null,
            Notes = null,
            TreatmentPlan = null,
            Prescription = null
        });

        result.Should().NotBeNull();
        var stored = await _dbContext.Consultations.AsNoTracking().FirstAsync(c => c.Id == consultation.Id);
        stored.ConsultationDate.Should().Be(newDate);
        stored.DurationMinutes.Should().Be(45);
        stored.Diagnosis.Should().Be("Type 2 diabetes");
        stored.Symptoms.Should().Be("Thirst");
        stored.Notes.Should().Be("Confidential clinical note");
        stored.TreatmentPlan.Should().Be("Diet and metformin");
        stored.Prescription.Should().Be("Metformin 500mg");
    }

    [Fact]
    public async Task UpdateConsultationAsync_AsAdmin_WithEmptyDiagnosis_ThrowsArgumentException()
    {
        var patient = new Patient { FirstName = "Empty", LastName = "Diagnosis", Email = "emptydx@example.com", Age = 29, Gender = "Male", CreatedAtUtc = DateTime.UtcNow };
        _dbContext.Patients.Add(patient);
        await _dbContext.SaveChangesAsync();

        var consultation = new Consultation
        {
            PatientId = patient.Id,
            DoctorName = "Dr. House",
            ConsultationDate = DateTime.UtcNow.AddDays(1),
            Diagnosis = "Original diagnosis",
            Status = ConsultationStatus.Scheduled
        };
        _dbContext.Consultations.Add(consultation);
        await _dbContext.SaveChangesAsync();

        var admin = new Mock<ICurrentUserService>();
        admin.Setup(u => u.IsAuthenticated).Returns(true);
        admin.Setup(u => u.Role).Returns(UserRole.Admin);
        var sut = new ConsultationService(_dbContext, _loggerMock.Object, admin.Object);

        var act = async () => await sut.UpdateConsultationAsync(consultation.Id, new UpdateConsultationDto
        {
            DoctorName = "Dr. House",
            ConsultationDate = consultation.ConsultationDate,
            DurationMinutes = 30,
            Status = ConsultationStatus.Scheduled,
            Diagnosis = "   "
        });

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*Diagnosis is required*");
        (await _dbContext.Consultations.AsNoTracking().FirstAsync(c => c.Id == consultation.Id)).Diagnosis.Should().Be("Original diagnosis");
    }

    public void Dispose()
    {
        _dbContext.Database.EnsureDeleted();
        _dbContext.Dispose();
    }
}
