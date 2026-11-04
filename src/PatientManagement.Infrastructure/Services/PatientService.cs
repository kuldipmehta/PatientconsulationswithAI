using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PatientManagement.Core.DTOs.Common;
using PatientManagement.Core.DTOs.Consultations;
using PatientManagement.Core.DTOs.Patients;
using PatientManagement.Core.Entities;
using PatientManagement.Core.Interfaces;
using PatientManagement.Infrastructure.Data;

namespace PatientManagement.Infrastructure.Services;

public class PatientService : IPatientService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IBlobStorageService _blobStorageService;
    private readonly IEmailService _emailService;
    private readonly ILogger<PatientService> _logger;

    public PatientService(
        ApplicationDbContext dbContext,
        IBlobStorageService blobStorageService,
        IEmailService emailService,
        ILogger<PatientService> logger)
    {
        _dbContext = dbContext;
        _blobStorageService = blobStorageService;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task<PagedResult<PatientSummaryDto>> GetPatientsAsync(
        PatientFilterParams filter,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Patients
            .AsNoTracking()
            .AsQueryable();

        // 1. Filtering
        if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
        {
            var search = filter.SearchTerm.Trim().ToLower();
            query = query.Where(p =>
                p.FirstName.ToLower().Contains(search) ||
                p.LastName.ToLower().Contains(search) ||
                p.Email.ToLower().Contains(search));
        }

        if (!string.IsNullOrWhiteSpace(filter.Gender))
        {
            query = query.Where(p => p.Gender.ToLower() == filter.Gender.Trim().ToLower());
        }

        if (filter.MinAge.HasValue)
        {
            query = query.Where(p => p.Age >= filter.MinAge.Value);
        }

        if (filter.MaxAge.HasValue)
        {
            query = query.Where(p => p.Age <= filter.MaxAge.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        // 2. Sorting
        query = (filter.SortBy?.ToLower(), filter.SortDescending) switch
        {
            ("firstname", false) => query.OrderBy(p => p.FirstName),
            ("firstname", true) => query.OrderByDescending(p => p.FirstName),
            ("lastname", false) => query.OrderBy(p => p.LastName),
            ("lastname", true) => query.OrderByDescending(p => p.LastName),
            ("age", false) => query.OrderBy(p => p.Age),
            ("age", true) => query.OrderByDescending(p => p.Age),
            ("email", false) => query.OrderBy(p => p.Email),
            ("email", true) => query.OrderByDescending(p => p.Email),
            ("createdat", false) => query.OrderBy(p => p.CreatedAtUtc),
            _ => query.OrderByDescending(p => p.CreatedAtUtc)
        };

        // 3. Paging & Projection
        var items = await query
            .Skip((filter.PageNumber - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(p => new PatientSummaryDto
            {
                Id = p.Id,
                FirstName = p.FirstName,
                LastName = p.LastName,
                Email = p.Email,
                PhoneNumber = p.PhoneNumber,
                Age = p.Age,
                Gender = p.Gender,
                DateOfBirth = p.DateOfBirth,
                ProfilePictureUrl = p.ProfilePictureUrl,
                ConsultationCount = p.Consultations.Count,
                CreatedAtUtc = p.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<PatientSummaryDto>(items, totalCount, filter.PageNumber, filter.PageSize);
    }

    public async Task<PatientResponseDto?> GetPatientByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var patient = await _dbContext.Patients
            .AsNoTracking()
            .Include(p => p.Consultations)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (patient == null)
        {
            return null;
        }

        return MapToResponseDto(patient);
    }

    public async Task<PatientResponseDto> CreatePatientAsync(CreatePatientDto dto, CancellationToken cancellationToken = default)
    {
        string? pictureUrl = null;
        string? blobName = null;

        // 1. Upload picture to Azure Blob Storage if provided
        if (dto.Picture != null && dto.Picture.Length > 0)
        {
            _logger.LogInformation("Uploading picture for new patient {FirstName} {LastName}...", dto.FirstName, dto.LastName);
            using var stream = dto.Picture.OpenReadStream();
            var uploadResult = await _blobStorageService.UploadAsync(
                stream,
                dto.Picture.FileName,
                dto.Picture.ContentType,
                cancellationToken: cancellationToken);

            pictureUrl = uploadResult.BlobUrl;
            blobName = uploadResult.BlobName;
        }

        // 2. Build Patient Entity
        var patient = new Patient
        {
            FirstName = dto.FirstName.Trim(),
            LastName = dto.LastName.Trim(),
            Email = dto.Email.Trim().ToLowerInvariant(),
            PhoneNumber = dto.PhoneNumber?.Trim(),
            Age = dto.Age,
            Gender = dto.Gender.Trim(),
            DateOfBirth = dto.DateOfBirth,
            Address = dto.Address?.Trim(),
            MedicalNotes = dto.MedicalNotes?.Trim(),
            ProfilePictureUrl = pictureUrl,
            ProfilePictureBlobName = blobName,
            CreatedAtUtc = DateTime.UtcNow
        };

        // 3. Save to SQL Server via EF Core
        _dbContext.Patients.Add(patient);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Created new patient in SQL Server database with ID {PatientId}.", patient.Id);

        // 4. Send dummy email after patient is created
        try
        {
            await _emailService.SendPatientWelcomeEmailAsync(patient, cancellationToken);
            _logger.LogInformation("Dispatched dummy welcome email to patient #{PatientId} ({Email}).", patient.Id, patient.Email);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send welcome email for patient ID {PatientId}.", patient.Id);
            // Non-blocking for patient creation
        }

        return MapToResponseDto(patient);
    }

    public async Task<PatientResponseDto?> UpdatePatientAsync(
        int id,
        UpdatePatientDto dto,
        CancellationToken cancellationToken = default)
    {
        var patient = await _dbContext.Patients
            .Include(p => p.Consultations)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (patient == null)
        {
            return null;
        }

        // Handle replacement picture upload if provided
        if (dto.Picture != null && dto.Picture.Length > 0)
        {
            // Optionally delete old blob
            if (!string.IsNullOrEmpty(patient.ProfilePictureBlobName))
            {
                await _blobStorageService.DeleteAsync(patient.ProfilePictureBlobName, cancellationToken: cancellationToken);
            }

            using var stream = dto.Picture.OpenReadStream();
            var uploadResult = await _blobStorageService.UploadAsync(
                stream,
                dto.Picture.FileName,
                dto.Picture.ContentType,
                cancellationToken: cancellationToken);

            patient.ProfilePictureUrl = uploadResult.BlobUrl;
            patient.ProfilePictureBlobName = uploadResult.BlobName;
        }

        patient.FirstName = dto.FirstName.Trim();
        patient.LastName = dto.LastName.Trim();
        patient.Email = dto.Email.Trim().ToLowerInvariant();
        patient.PhoneNumber = dto.PhoneNumber?.Trim();
        patient.Age = dto.Age;
        patient.Gender = dto.Gender.Trim();
        patient.DateOfBirth = dto.DateOfBirth;
        patient.Address = dto.Address?.Trim();
        patient.MedicalNotes = dto.MedicalNotes?.Trim();
        patient.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Updated patient record with ID {PatientId}.", patient.Id);

        return MapToResponseDto(patient);
    }

    public async Task<bool> DeletePatientAsync(int id, CancellationToken cancellationToken = default)
    {
        var patient = await _dbContext.Patients.FindAsync(new object[] { id }, cancellationToken);
        if (patient == null)
        {
            return false;
        }

        // Clean up picture blob if exists
        if (!string.IsNullOrEmpty(patient.ProfilePictureBlobName))
        {
            await _blobStorageService.DeleteAsync(patient.ProfilePictureBlobName, cancellationToken: cancellationToken);
        }

        _dbContext.Patients.Remove(patient);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Deleted patient record with ID {PatientId}.", id);
        return true;
    }

    private static PatientResponseDto MapToResponseDto(Patient patient)
    {
        return new PatientResponseDto
        {
            Id = patient.Id,
            FirstName = patient.FirstName,
            LastName = patient.LastName,
            Email = patient.Email,
            PhoneNumber = patient.PhoneNumber,
            Age = patient.Age,
            Gender = patient.Gender,
            DateOfBirth = patient.DateOfBirth,
            Address = patient.Address,
            MedicalNotes = patient.MedicalNotes,
            ProfilePictureUrl = patient.ProfilePictureUrl,
            CreatedAtUtc = patient.CreatedAtUtc,
            UpdatedAtUtc = patient.UpdatedAtUtc,
            Consultations = patient.Consultations.Select(c => new ConsultationResponseDto
            {
                Id = c.Id,
                PatientId = c.PatientId,
                PatientName = $"{patient.FirstName} {patient.LastName}".Trim(),
                DoctorName = c.DoctorName,
                ConsultationDate = c.ConsultationDate,
                Diagnosis = c.Diagnosis,
                Symptoms = c.Symptoms,
                Notes = c.Notes,
                TreatmentPlan = c.TreatmentPlan,
                Prescription = c.Prescription,
                Status = c.Status,
                Fee = c.Fee,
                CreatedAtUtc = c.CreatedAtUtc,
                UpdatedAtUtc = c.UpdatedAtUtc
            }).OrderByDescending(c => c.ConsultationDate).ToList()
        };
    }
}
