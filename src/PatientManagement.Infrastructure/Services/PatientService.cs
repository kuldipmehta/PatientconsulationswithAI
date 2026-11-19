using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PatientManagement.Core.DTOs.Common;
using PatientManagement.Core.DTOs.Consultations;
using PatientManagement.Core.DTOs.Patients;
using PatientManagement.Core.DTOs.Rag;
using PatientManagement.Core.Entities;
using PatientManagement.Core.Enums;
using PatientManagement.Core.Exceptions;
using PatientManagement.Core.Interfaces;
using PatientManagement.Infrastructure.Data;

namespace PatientManagement.Infrastructure.Services;

public class PatientService : IPatientService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IBlobStorageService _blobStorageService;
    private readonly IEmailService _emailService;
    private readonly ILogger<PatientService> _logger;
    private readonly ICurrentUserService? _currentUser;

    public PatientService(
        ApplicationDbContext dbContext,
        IBlobStorageService blobStorageService,
        IEmailService emailService,
        ILogger<PatientService> logger,
        ICurrentUserService? currentUser = null)
    {
        _dbContext = dbContext;
        _blobStorageService = blobStorageService;
        _emailService = emailService;
        _logger = logger;
        _currentUser = currentUser;
    }

    public async Task<PagedResult<PatientSummaryDto>> GetPatientsAsync(
        PatientFilterParams filter,
        CancellationToken cancellationToken = default)
    {
        var doctorId = EnsureValidDoctorIdentity();

        var query = _dbContext.Patients
            .AsNoTracking()
            .Include(p => p.PrimaryDoctor)
            .AsQueryable();

        // Restrict doctors to their own assigned patients
        if (doctorId.HasValue)
        {
            query = query.Where(p => p.PrimaryDoctorId == doctorId.Value);
        }

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

        var rawItems = await query
            .Skip((filter.PageNumber - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(p => new
            {
                p.Id,
                p.FirstName,
                p.LastName,
                p.Email,
                p.PhoneNumber,
                p.Age,
                p.Gender,
                p.DateOfBirth,
                p.ProfilePictureUrl,
                p.ProfilePictureBlobName,
                p.PrimaryDoctorId,
                PrimaryDoctorName = p.PrimaryDoctor != null ? p.PrimaryDoctor.FullName : null,
                p.RowVersion,
                ConsultationCount = p.Consultations.Count,
                p.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        var items = rawItems.Select(p => new PatientSummaryDto
        {
            Id = p.Id,
            FirstName = p.FirstName,
            LastName = p.LastName,
            Email = p.Email,
            PhoneNumber = p.PhoneNumber,
            Age = p.Age,
            Gender = p.Gender,
            DateOfBirth = p.DateOfBirth,
            ProfilePictureUrl = ResolveSecurePictureUrl(p.Id, p.ProfilePictureBlobName, p.ProfilePictureUrl),
            PrimaryDoctorId = p.PrimaryDoctorId,
            PrimaryDoctorName = p.PrimaryDoctorName,
            RowVersion = p.RowVersion.Length > 0 ? Convert.ToBase64String(p.RowVersion) : string.Empty,
            ConsultationCount = p.ConsultationCount,
            CreatedAtUtc = p.CreatedAtUtc
        }).ToList();

        return new PagedResult<PatientSummaryDto>(items, totalCount, filter.PageNumber, filter.PageSize);
    }

    public async Task<PatientResponseDto?> GetPatientByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        EnsureValidDoctorIdentity();

        var patient = await _dbContext.Patients
            .AsNoTracking()
            .Include(p => p.PrimaryDoctor)
            .Include(p => p.Consultations)
            .Include(p => p.Documents)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (patient == null)
        {
            return null;
        }

        EnsureDoctorOwnership(patient);
        return MapToResponseDto(patient);
    }

    public async Task<PatientResponseDto> CreatePatientAsync(CreatePatientDto dto, CancellationToken cancellationToken = default)
    {
        var currentDoctorId = EnsureValidDoctorIdentity();

        string? pictureBlobName = null;
        string? pictureUrl = null;

        if (dto.Picture != null && dto.Picture.Length > 0)
        {
            using var stream = dto.Picture.OpenReadStream();
            var uploadResult = await _blobStorageService.UploadAsync(
                stream,
                dto.Picture.FileName,
                dto.Picture.ContentType,
                cancellationToken: cancellationToken);

            pictureBlobName = uploadResult.BlobName;
            pictureUrl = uploadResult.BlobUrl;
        }

        int? assignedDoctorId = currentDoctorId ?? dto.PrimaryDoctorId;
        var canSetMedicalNotes = _currentUser is not { IsAuthenticated: true }
            || _currentUser.Role is UserRole.Doctor or UserRole.Admin;

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
            MedicalNotes = canSetMedicalNotes ? dto.MedicalNotes?.Trim() : null,
            ProfilePictureUrl = pictureUrl,
            ProfilePictureBlobName = pictureBlobName,
            PrimaryDoctorId = assignedDoctorId,
            CreatedAtUtc = DateTime.UtcNow
        };

        _dbContext.Patients.Add(patient);
        await _dbContext.SaveChangesAsync(cancellationToken);

        if (assignedDoctorId.HasValue)
        {
            await _dbContext.Entry(patient).Reference(p => p.PrimaryDoctor).LoadAsync(cancellationToken);
        }

        try
        {
            // Never email passwords/credentials — send a one-time onboarding link notification
            var setupTokenHint = Guid.NewGuid().ToString("N")[..12];
            var setupLink = $"https://portal.patientmanagement.local/set-password?invite={setupTokenHint}";
            await _emailService.SendPatientWelcomeEmailAsync(patient, setupLink, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to dispatch welcome notification for patient ID {PatientId}.", patient.Id);
        }

        return MapToResponseDto(patient);
    }

    public async Task<PatientResponseDto?> UpdatePatientAsync(
        int id,
        UpdatePatientDto dto,
        CancellationToken cancellationToken = default)
    {
        EnsureValidDoctorIdentity();

        var patient = await _dbContext.Patients
            .Include(p => p.PrimaryDoctor)
            .Include(p => p.Consultations)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (patient == null)
        {
            return null;
        }

        EnsureDoctorOwnership(patient);

        // Apply optimistic concurrency check if RowVersion token was provided
        if (!string.IsNullOrWhiteSpace(dto.RowVersion))
        {
            try
            {
                var clientVersion = Convert.FromBase64String(dto.RowVersion);
                _dbContext.Entry(patient).Property(p => p.RowVersion).OriginalValue = clientVersion;
            }
            catch (FormatException)
            {
                throw new ArgumentException("Invalid RowVersion concurrency token.");
            }
        }

        if (dto.Picture != null && dto.Picture.Length > 0)
        {
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

        // Only Doctor or Admin may modify MedicalNotes, and only when the field was actually sent
        var canModifyMedicalNotes = _currentUser is not { IsAuthenticated: true }
            || _currentUser.Role is UserRole.Doctor or UserRole.Admin;
        if (canModifyMedicalNotes && dto.MedicalNotes is not null)
        {
            patient.MedicalNotes = dto.MedicalNotes.Trim();
        }

        if (dto.PrimaryDoctorId.HasValue && _currentUser?.Role != UserRole.Doctor)
        {
            patient.PrimaryDoctorId = dto.PrimaryDoctorId;
        }

        patient.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        if (patient.PrimaryDoctorId.HasValue)
        {
            await _dbContext.Entry(patient).Reference(p => p.PrimaryDoctor).LoadAsync(cancellationToken);
        }

        return MapToResponseDto(patient);
    }

    public async Task<bool> DeletePatientAsync(int id, CancellationToken cancellationToken = default)
    {
        EnsureValidDoctorIdentity();

        var patient = await _dbContext.Patients
            .Include(p => p.Consultations)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (patient == null)
        {
            return false;
        }

        EnsureDoctorOwnership(patient);

        // Soft delete preserves medical records and audit trail
        var now = DateTime.UtcNow;
        patient.IsDeleted = true;
        patient.DeletedAtUtc = now;
        patient.DeletedByUserId = _currentUser?.UserId;

        foreach (var consultation in patient.Consultations)
        {
            consultation.IsDeleted = true;
            consultation.DeletedAtUtc = now;
            consultation.DeletedByUserId = _currentUser?.UserId;
        }

        // Embeddings and chunk text are derived copies of clinical data: purge them with the patient.
        // Uploaded documents are soft-deleted (the original files stay in private storage for retention).
        var derivedChunks = await _dbContext.PatientDocumentChunks
            .IgnoreQueryFilters()
            .Where(c => c.PatientId == id)
            .ToListAsync(cancellationToken);
        _dbContext.PatientDocumentChunks.RemoveRange(derivedChunks);

        var documents = await _dbContext.PatientDocuments
            .IgnoreQueryFilters()
            .Where(d => d.PatientId == id && !d.IsDeleted)
            .ToListAsync(cancellationToken);
        foreach (var document in documents)
        {
            document.IsDeleted = true;
            document.DeletedAtUtc = now;
            document.DeletedByUserId = _currentUser?.UserId;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Soft-deleted patient #{PatientId} and {Count} consultations by user {UserId}.",
            id, patient.Consultations.Count, _currentUser?.UserId);
        return true;
    }

    public async Task<BlobReadResult?> GetPatientPictureAsync(int id, CancellationToken cancellationToken = default)
    {
        EnsureValidDoctorIdentity();

        var patient = await _dbContext.Patients
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (patient == null)
        {
            return null;
        }

        EnsureDoctorOwnership(patient);

        if (string.IsNullOrWhiteSpace(patient.ProfilePictureBlobName))
        {
            return null;
        }

        return await _blobStorageService.OpenReadAsync(patient.ProfilePictureBlobName, cancellationToken: cancellationToken);
    }

    private int? EnsureValidDoctorIdentity()
    {
        if (_currentUser is { IsAuthenticated: true, Role: UserRole.Doctor })
        {
            if (!_currentUser.DoctorId.HasValue)
            {
                throw new ForbiddenAccessException("Doctor account is not linked to a valid DoctorId.");
            }

            return _currentUser.DoctorId.Value;
        }

        return null;
    }

    private void EnsureDoctorOwnership(Patient patient)
    {
        var doctorId = EnsureValidDoctorIdentity();
        if (doctorId.HasValue && patient.PrimaryDoctorId != doctorId.Value)
        {
            throw new ForbiddenAccessException($"Doctor is not assigned to patient #{patient.Id}.");
        }
    }

    private string? ResolveSecurePictureUrl(int patientId, string? blobName, string? fallbackUrl)
    {
        if (!string.IsNullOrWhiteSpace(blobName))
        {
            var sas = _blobStorageService.GetReadSasUrl(blobName, TimeSpan.FromMinutes(15));
            return string.IsNullOrWhiteSpace(sas)
                ? $"/api/patients/{patientId}/picture"
                : sas;
        }

        return fallbackUrl;
    }

    private PatientResponseDto MapToResponseDto(Patient patient)
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
            ProfilePictureUrl = ResolveSecurePictureUrl(patient.Id, patient.ProfilePictureBlobName, patient.ProfilePictureUrl),
            PrimaryDoctorId = patient.PrimaryDoctorId,
            PrimaryDoctorName = patient.PrimaryDoctor?.FullName,
            RowVersion = patient.RowVersion.Length > 0 ? Convert.ToBase64String(patient.RowVersion) : string.Empty,
            CreatedAtUtc = patient.CreatedAtUtc,
            UpdatedAtUtc = patient.UpdatedAtUtc,
            Consultations = patient.Consultations.Select(c => new ConsultationResponseDto
            {
                Id = c.Id,
                PatientId = c.PatientId,
                PatientName = $"{patient.FirstName} {patient.LastName}".Trim(),
                DoctorId = c.DoctorId,
                DoctorName = c.DoctorName,
                ConsultationDate = c.ConsultationDate,
                DurationMinutes = c.DurationMinutes,
                Diagnosis = c.Diagnosis,
                Symptoms = c.Symptoms,
                Notes = c.Notes,
                TreatmentPlan = c.TreatmentPlan,
                Prescription = c.Prescription,
                Status = c.Status,
                Fee = c.Fee,
                RowVersion = c.RowVersion.Length > 0 ? Convert.ToBase64String(c.RowVersion) : string.Empty,
                CreatedAtUtc = c.CreatedAtUtc,
                UpdatedAtUtc = c.UpdatedAtUtc
            }).OrderByDescending(c => c.ConsultationDate).ToList(),
            Documents = patient.Documents?.Select(d => new PatientDocumentResponseDto
            {
                Id = d.Id,
                PatientId = d.PatientId,
                ConsultationId = d.ConsultationId,
                FileName = d.FileName,
                ContentType = d.ContentType,
                FileSizeBytes = d.FileSizeBytes,
                DocumentType = d.DocumentType,
                Status = d.Status,
                TotalChunks = d.TotalChunks,
                Summary = d.Summary,
                CreatedAtUtc = d.CreatedAtUtc
            }).OrderByDescending(d => d.CreatedAtUtc).ToList() ?? new()
        };
    }
}
