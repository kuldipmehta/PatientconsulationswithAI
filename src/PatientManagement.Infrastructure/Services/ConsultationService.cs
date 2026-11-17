using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PatientManagement.Core.DTOs.Consultations;
using PatientManagement.Core.Entities;
using PatientManagement.Core.Enums;
using PatientManagement.Core.Exceptions;
using PatientManagement.Core.Interfaces;
using PatientManagement.Infrastructure.Data;

namespace PatientManagement.Infrastructure.Services;

public class ConsultationService : IConsultationService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<ConsultationService> _logger;
    private readonly ICurrentUserService? _currentUser;
    private readonly IPatientRagService? _ragService;

    public ConsultationService(
        ApplicationDbContext dbContext,
        ILogger<ConsultationService> logger,
        ICurrentUserService? currentUser = null,
        IPatientRagService? ragService = null)
    {
        _dbContext = dbContext;
        _logger = logger;
        _currentUser = currentUser;
        _ragService = ragService;
    }

    public async Task<IEnumerable<ConsultationResponseDto>> GetConsultationsAsync(
        int? patientId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Consultations
            .AsNoTracking()
            .Include(c => c.Patient)
            .AsQueryable();

        if (_currentUser is { IsAuthenticated: true, Role: UserRole.Doctor })
        {
            query = query.Where(c => c.Patient.PrimaryDoctorId == _currentUser.DoctorId || c.DoctorId == _currentUser.DoctorId);
        }

        if (patientId.HasValue)
        {
            query = query.Where(c => c.PatientId == patientId.Value);
        }

        var list = await query
            .OrderByDescending(c => c.ConsultationDate)
            .ToListAsync(cancellationToken);

        return list.Select(MapToResponseDto);
    }

    public async Task<ConsultationResponseDto?> GetConsultationByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var consultation = await _dbContext.Consultations
            .AsNoTracking()
            .Include(c => c.Patient)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (consultation == null)
        {
            return null;
        }

        EnsureDoctorAccess(consultation.Patient, consultation.DoctorId);
        return MapToResponseDto(consultation);
    }

    public async Task<ConsultationResponseDto> CreateConsultationAsync(
        CreateConsultationDto dto,
        CancellationToken cancellationToken = default)
    {
        var patient = await _dbContext.Patients
            .FirstOrDefaultAsync(p => p.Id == dto.PatientId, cancellationToken);

        if (patient == null)
        {
            throw new KeyNotFoundException($"Patient with ID {dto.PatientId} was not found.");
        }

        var isDoctor = _currentUser is { IsAuthenticated: true, Role: UserRole.Doctor };
        if (isDoctor)
        {
            EnsureDoctorAccess(patient, consultationDoctorId: null);
        }

        // For the Doctor role, force DoctorId to the current doctor, ignoring any value supplied in the request
        var requestedDoctorId = isDoctor ? _currentUser!.DoctorId : dto.DoctorId;
        var resolvedDoctor = await ResolveDoctorAsync(requestedDoctorId, dto.DoctorName, cancellationToken);
        var doctorId = isDoctor ? _currentUser!.DoctorId : (resolvedDoctor?.Id ?? dto.DoctorId);
        var doctorName = resolvedDoctor?.FullName ?? dto.DoctorName.Trim();
        var startUtc = dto.ConsultationDate == default ? DateTime.UtcNow : dto.ConsultationDate;
        var duration = dto.DurationMinutes > 0 ? dto.DurationMinutes : 30;

        if (dto.Status != ConsultationStatus.Cancelled)
        {
            await EnsureNoDoctorConflictAsync(doctorId, doctorName, startUtc, duration, excludeConsultationId: null, cancellationToken);
        }

        var consultation = new Consultation
        {
            PatientId = dto.PatientId,
            DoctorId = doctorId,
            DoctorName = doctorName,
            ConsultationDate = startUtc,
            DurationMinutes = duration,
            Diagnosis = dto.Diagnosis.Trim(),
            Symptoms = dto.Symptoms?.Trim(),
            Notes = dto.Notes?.Trim(),
            TreatmentPlan = dto.TreatmentPlan?.Trim(),
            Prescription = dto.Prescription?.Trim(),
            Status = dto.Status,
            Fee = dto.Fee,
            CreatedAtUtc = DateTime.UtcNow
        };

        _dbContext.Consultations.Add(consultation);
        await _dbContext.SaveChangesAsync(cancellationToken);

        if (_ragService != null)
        {
            try
            {
                await _ragService.IndexConsultationAsync(consultation.Id, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to asynchronously index consultation #{Id} into RAG store.", consultation.Id);
            }
        }

        consultation.Patient = patient;
        return MapToResponseDto(consultation);
    }

    public async Task<ConsultationResponseDto?> UpdateConsultationAsync(
        int id,
        UpdateConsultationDto dto,
        CancellationToken cancellationToken = default)
    {
        var consultation = await _dbContext.Consultations
            .Include(c => c.Patient)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (consultation == null)
        {
            return null;
        }

        EnsureDoctorAccess(consultation.Patient, consultation.DoctorId);

        if (!string.IsNullOrWhiteSpace(dto.RowVersion))
        {
            try
            {
                var clientVersion = Convert.FromBase64String(dto.RowVersion);
                _dbContext.Entry(consultation).Property(c => c.RowVersion).OriginalValue = clientVersion;
            }
            catch (FormatException)
            {
                throw new ArgumentException("Invalid RowVersion concurrency token.");
            }
        }

        var resolvedDoctor = await ResolveDoctorAsync(dto.DoctorId ?? consultation.DoctorId, dto.DoctorName, cancellationToken);
        var doctorId = resolvedDoctor?.Id ?? dto.DoctorId ?? consultation.DoctorId;
        var doctorName = resolvedDoctor?.FullName ?? dto.DoctorName.Trim();
        var duration = dto.DurationMinutes > 0 ? dto.DurationMinutes : 30;

        if (dto.Status != ConsultationStatus.Cancelled)
        {
            await EnsureNoDoctorConflictAsync(doctorId, doctorName, dto.ConsultationDate, duration, excludeConsultationId: id, cancellationToken);
        }

        consultation.DoctorId = doctorId;
        consultation.DoctorName = doctorName;
        consultation.ConsultationDate = dto.ConsultationDate;
        consultation.DurationMinutes = duration;
        consultation.Diagnosis = dto.Diagnosis.Trim();
        consultation.Symptoms = dto.Symptoms?.Trim();
        consultation.Notes = dto.Notes?.Trim();
        consultation.TreatmentPlan = dto.TreatmentPlan?.Trim();
        consultation.Prescription = dto.Prescription?.Trim();
        consultation.Status = dto.Status;
        consultation.Fee = dto.Fee;
        consultation.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        if (_ragService != null)
        {
            try
            {
                await _ragService.IndexConsultationAsync(consultation.Id, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to reindex updated consultation #{Id} into RAG store.", consultation.Id);
            }
        }

        return MapToResponseDto(consultation);
    }

    public async Task<bool> DeleteConsultationAsync(int id, CancellationToken cancellationToken = default)
    {
        var consultation = await _dbContext.Consultations
            .Include(c => c.Patient)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (consultation == null)
        {
            return false;
        }

        EnsureDoctorAccess(consultation.Patient, consultation.DoctorId);

        consultation.IsDeleted = true;
        consultation.DeletedAtUtc = DateTime.UtcNow;
        consultation.DeletedByUserId = _currentUser?.UserId;

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Soft-deleted consultation #{ConsultationId}.", id);

        return true;
    }

    private void EnsureDoctorAccess(Patient patient, int? consultationDoctorId)
    {
        if (_currentUser is { IsAuthenticated: true, Role: UserRole.Doctor })
        {
            var isPrimaryDoctor = _currentUser.DoctorId.HasValue && patient.PrimaryDoctorId == _currentUser.DoctorId;
            var isConsultationDoctor = _currentUser.DoctorId.HasValue && consultationDoctorId == _currentUser.DoctorId;

            if (!isPrimaryDoctor && !isConsultationDoctor)
            {
                throw new ForbiddenAccessException("Doctor does not have access to this patient's consultation.");
            }
        }
    }

    private async Task<Doctor?> ResolveDoctorAsync(int? doctorId, string doctorName, CancellationToken cancellationToken)
    {
        if (doctorId.HasValue)
        {
            return await _dbContext.Doctors.FirstOrDefaultAsync(d => d.Id == doctorId.Value, cancellationToken);
        }

        var cleanName = doctorName.Trim().ToLower();
        return await _dbContext.Doctors
            .FirstOrDefaultAsync(d => d.FullName.ToLower() == cleanName, cancellationToken);
    }

    private async Task EnsureNoDoctorConflictAsync(
        int? doctorId,
        string doctorName,
        DateTime startUtc,
        int durationMinutes,
        int? excludeConsultationId,
        CancellationToken cancellationToken)
    {
        var endUtc = startUtc.AddMinutes(durationMinutes);

        var candidates = await _dbContext.Consultations
            .AsNoTracking()
            .Where(c =>
                c.Status != ConsultationStatus.Cancelled &&
                (!excludeConsultationId.HasValue || c.Id != excludeConsultationId.Value) &&
                ((doctorId.HasValue && c.DoctorId == doctorId.Value) ||
                 (!doctorId.HasValue && c.DoctorName.ToLower() == doctorName.ToLower())))
            .Select(c => new
            {
                c.Id,
                c.ConsultationDate,
                Duration = c.DurationMinutes > 0 ? c.DurationMinutes : 30
            })
            .ToListAsync(cancellationToken);

        var conflict = candidates.FirstOrDefault(c =>
            startUtc < c.ConsultationDate.AddMinutes(c.Duration) &&
            endUtc > c.ConsultationDate);

        if (conflict != null)
        {
            throw new BookingConflictException(
                $"Scheduling conflict: {doctorName} already has an active consultation (#{conflict.Id}) from {conflict.ConsultationDate:yyyy-MM-dd HH:mm} UTC to {conflict.ConsultationDate.AddMinutes(conflict.Duration):HH:mm} UTC.");
        }
    }

    private static ConsultationResponseDto MapToResponseDto(Consultation c)
    {
        return new ConsultationResponseDto
        {
            Id = c.Id,
            PatientId = c.PatientId,
            PatientName = c.Patient != null ? $"{c.Patient.FirstName} {c.Patient.LastName}".Trim() : string.Empty,
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
        };
    }
}
