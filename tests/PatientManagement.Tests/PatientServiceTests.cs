using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using PatientManagement.Core.DTOs.Patients;
using PatientManagement.Core.Entities;
using PatientManagement.Core.Enums;
using PatientManagement.Core.Exceptions;
using PatientManagement.Core.Interfaces;
using PatientManagement.Infrastructure.Data;
using PatientManagement.Infrastructure.Services;
using Xunit;

namespace PatientManagement.Tests;

public class PatientServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly Mock<IBlobStorageService> _blobStorageMock;
    private readonly Mock<IEmailService> _emailServiceMock;
    private readonly Mock<ILogger<PatientService>> _loggerMock;
    private readonly PatientService _patientService;

    public PatientServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new ApplicationDbContext(options);
        _blobStorageMock = new Mock<IBlobStorageService>();
        _emailServiceMock = new Mock<IEmailService>();
        _loggerMock = new Mock<ILogger<PatientService>>();

        _patientService = new PatientService(
            _dbContext,
            _blobStorageMock.Object,
            _emailServiceMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task CreatePatientAsync_WithValidDataAndPicture_StoresInBlob_SavesToDb_AndSendsEmail()
    {
        // Arrange
        var fakeFileContent = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 };
        var stream = new MemoryStream(fakeFileContent);
        var formFile = new FormFile(stream, 0, fakeFileContent.Length, "Picture", "avatar.jpg")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/jpeg"
        };

        var dto = new CreatePatientDto
        {
            FirstName = "John",
            LastName = "Doe",
            Email = "john.doe@example.com",
            PhoneNumber = "+1-555-1234",
            Age = 35,
            Gender = "Male",
            DateOfBirth = new DateTime(1989, 5, 12, 0, 0, 0, DateTimeKind.Utc),
            Address = "123 Elm St",
            MedicalNotes = "No allergies",
            Picture = formFile
        };

        var expectedBlobUrl = "https://patientstorageaccount.blob.core.windows.net/patient-pictures/avatar-123.jpg?sig=shortlived";
        var expectedBlobName = "avatar-123.jpg";

        _blobStorageMock.Setup(b => b.UploadAsync(
                It.IsAny<Stream>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BlobUploadResult(expectedBlobName, expectedBlobUrl, "image/jpeg", fakeFileContent.Length));

        _blobStorageMock.Setup(b => b.GetReadSasUrl(
                expectedBlobName,
                It.IsAny<TimeSpan?>(),
                It.IsAny<string?>()))
            .Returns(expectedBlobUrl);

        _emailServiceMock.Setup(e => e.SendPatientWelcomeEmailAsync(
                It.IsAny<Patient>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _patientService.CreatePatientAsync(dto);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().BeGreaterThan(0);
        result.FirstName.Should().Be("John");
        result.LastName.Should().Be("Doe");
        result.Email.Should().Be("john.doe@example.com");
        result.ProfilePictureUrl.Should().Be(expectedBlobUrl);

        var savedPatient = await _dbContext.Patients.FindAsync(result.Id);
        savedPatient.Should().NotBeNull();
        savedPatient!.ProfilePictureBlobName.Should().Be(expectedBlobName);

        _blobStorageMock.Verify(b => b.UploadAsync(
            It.IsAny<Stream>(),
            "avatar.jpg",
            "image/jpeg",
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);

        _emailServiceMock.Verify(e => e.SendPatientWelcomeEmailAsync(
            It.Is<Patient>(p => p.Id == result.Id && p.Email == "john.doe@example.com"),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetPatientsAsync_SupportsSearchAndPagination()
    {
        // Arrange
        _dbContext.Patients.AddRange(
            new Patient { FirstName = "Alice", LastName = "Wonderland", Email = "alice@example.com", Age = 25, Gender = "Female", CreatedAtUtc = DateTime.UtcNow.AddMinutes(-10) },
            new Patient { FirstName = "Bob", LastName = "Builder", Email = "bob@example.com", Age = 45, Gender = "Male", CreatedAtUtc = DateTime.UtcNow.AddMinutes(-5) },
            new Patient { FirstName = "Charlie", LastName = "Brown", Email = "charlie@example.com", Age = 30, Gender = "Male", CreatedAtUtc = DateTime.UtcNow }
        );
        await _dbContext.SaveChangesAsync();

        var filter = new PatientFilterParams
        {
            SearchTerm = "Alice",
            PageNumber = 1,
            PageSize = 10
        };

        // Act
        var result = await _patientService.GetPatientsAsync(filter);

        // Assert
        result.TotalCount.Should().Be(1);
        result.Items.Should().HaveCount(1);
        result.Items.First().FirstName.Should().Be("Alice");
    }

    [Fact]
    public async Task UpdatePatientAsync_UpdatesDetailsSuccessfully()
    {
        // Arrange
        var patient = new Patient
        {
            FirstName = "David",
            LastName = "Miller",
            Email = "david.old@example.com",
            Age = 40,
            Gender = "Male",
            CreatedAtUtc = DateTime.UtcNow
        };
        _dbContext.Patients.Add(patient);
        await _dbContext.SaveChangesAsync();

        var updateDto = new UpdatePatientDto
        {
            FirstName = "David",
            LastName = "Miller",
            Email = "david.new@example.com",
            Age = 41,
            Gender = "Male",
            PhoneNumber = "+1-555-9999",
            MedicalNotes = "Updated notes"
        };

        // Act
        var result = await _patientService.UpdatePatientAsync(patient.Id, updateDto);

        // Assert
        result.Should().NotBeNull();
        result!.Email.Should().Be("david.new@example.com");
        result.Age.Should().Be(41);
        result.MedicalNotes.Should().Be("Updated notes");
    }

    [Fact]
    public async Task DeletePatientAsync_SoftDeletesAndPreservesAuditRecord()
    {
        // Arrange
        var patient = new Patient
        {
            FirstName = "Sarah",
            LastName = "Connor",
            Email = "sarah@example.com",
            Age = 38,
            Gender = "Female",
            ProfilePictureBlobName = "sarah.jpg"
        };
        _dbContext.Patients.Add(patient);
        await _dbContext.SaveChangesAsync();

        // Act
        var deleted = await _patientService.DeletePatientAsync(patient.Id);

        // Assert
        deleted.Should().BeTrue();
        (await _dbContext.Patients.FirstOrDefaultAsync(p => p.Id == patient.Id)).Should().BeNull();
        var archived = await _dbContext.Patients.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == patient.Id);
        archived.Should().NotBeNull();
        archived!.IsDeleted.Should().BeTrue();
        archived.DeletedAtUtc.Should().NotBeNull();
        _blobStorageMock.Verify(b => b.DeleteAsync("sarah.jpg", null, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetPatientsAsync_WhenDoctorAuthenticated_ReturnsOnlyOwnAssignedPatients()
    {
        // Arrange
        _dbContext.Patients.AddRange(
            new Patient { FirstName = "Doc1", LastName = "Patient", Email = "d1@example.com", Age = 30, Gender = "Female", PrimaryDoctorId = 1 },
            new Patient { FirstName = "Doc2", LastName = "Patient", Email = "d2@example.com", Age = 40, Gender = "Male", PrimaryDoctorId = 2 }
        );
        await _dbContext.SaveChangesAsync();

        var doctorUser = new Mock<ICurrentUserService>();
        doctorUser.SetupGet(u => u.IsAuthenticated).Returns(true);
        doctorUser.SetupGet(u => u.UserId).Returns(10);
        doctorUser.SetupGet(u => u.Role).Returns(UserRole.Doctor);
        doctorUser.SetupGet(u => u.DoctorId).Returns(1);

        var scopedService = new PatientService(_dbContext, _blobStorageMock.Object, _emailServiceMock.Object, _loggerMock.Object, doctorUser.Object);

        // Act
        var result = await scopedService.GetPatientsAsync(new PatientFilterParams());

        // Assert
        result.TotalCount.Should().Be(1);
        result.Items.Single().FirstName.Should().Be("Doc1");

        // Accessing Doctor #2's patient directly throws ForbiddenAccessException
        var otherPatient = await _dbContext.Patients.FirstAsync(p => p.PrimaryDoctorId == 2);
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => scopedService.GetPatientByIdAsync(otherPatient.Id));
    }

    [Fact]
    public async Task UpdatePatientAsync_WithStaleRowVersion_ThrowsDbUpdateConcurrencyException()
    {
        // Arrange
        var initialVersion = new byte[] { 1, 0, 0, 0, 0, 0, 0, 1 };
        var staleVersion = new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 };

        var patient = new Patient
        {
            FirstName = "Elena",
            LastName = "Gilbert",
            Email = "elena@example.com",
            Age = 29,
            Gender = "Female",
            RowVersion = initialVersion
        };
        _dbContext.Patients.Add(patient);
        await _dbContext.SaveChangesAsync();

        var updateDto = new UpdatePatientDto
        {
            FirstName = "Elena",
            LastName = "Gilbert",
            Email = "elena@example.com",
            Age = 30,
            Gender = "Female",
            RowVersion = Convert.ToBase64String(staleVersion)
        };

        // Act & Assert
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            _patientService.UpdatePatientAsync(patient.Id, updateDto));
    }

    [Fact]
    public async Task DoctorWithNullDoctorId_ThrowsForbiddenAccessException_InsteadOfMatchingUnassignedPatients()
    {
        // Arrange: An unassigned patient (PrimaryDoctorId = null) exists in the database
        var unassignedPatient = new Patient
        {
            FirstName = "Unassigned",
            LastName = "Patient",
            Email = "unassigned@example.com",
            Age = 35,
            Gender = "Other",
            PrimaryDoctorId = null
        };
        _dbContext.Patients.Add(unassignedPatient);
        await _dbContext.SaveChangesAsync();

        var unlinkedDoctor = new Mock<ICurrentUserService>();
        unlinkedDoctor.SetupGet(u => u.IsAuthenticated).Returns(true);
        unlinkedDoctor.SetupGet(u => u.Role).Returns(UserRole.Doctor);
        unlinkedDoctor.SetupGet(u => u.DoctorId).Returns((int?)null);

        var scopedService = new PatientService(
            _dbContext,
            _blobStorageMock.Object,
            _emailServiceMock.Object,
            _loggerMock.Object,
            unlinkedDoctor.Object);

        // Act & Assert: All patient operations throw ForbiddenAccessException instead of matching null == null
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => scopedService.GetPatientsAsync(new PatientFilterParams()));
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => scopedService.GetPatientByIdAsync(unassignedPatient.Id));
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => scopedService.DeletePatientAsync(unassignedPatient.Id));
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => scopedService.GetPatientPictureAsync(unassignedPatient.Id));
    }

    public void Dispose()
    {
        _dbContext.Database.EnsureDeleted();
        _dbContext.Dispose();
    }
}
