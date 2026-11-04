using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PatientManagement.Core.DTOs.Consultations;
using PatientManagement.Core.Entities;
using PatientManagement.Core.Interfaces;
using PatientManagement.Infrastructure.Data;

namespace PatientManagement.Infrastructure.Services;

public class ConsultationService : IConsultationService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<ConsultationService> _logger;

    public ConsultationService(ApplicationDbContext dbContext, ILogger<ConsultationService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<IEnumerable<ConsultationResponseDto>> GetConsultationsAsync(
        int? patientId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Consultations
            .AsNoTracking()
            .Include(c => c.Patient)
            .AsQueryable();

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

        var consultation = new Consultation
        {
            PatientId = dto.PatientId,
            DoctorName = dto.DoctorName.Trim(),
            ConsultationDate = dto.ConsultationDate == default ? DateTime.UtcNow : dto.ConsultationDate,
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
        _logger.LogInformation("Created new consultation #{ConsultationId} for patient #{PatientId}.", consultation.Id, dto.PatientId);

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

        consultation.DoctorName = dto.DoctorName.Trim();
        consultation.ConsultationDate = dto.ConsultationDate;
        consultation.Diagnosis = dto.Diagnosis.Trim();
        consultation.Symptoms = dto.Symptoms?.Trim();
        consultation.Notes = dto.Notes?.Trim();
        consultation.TreatmentPlan = dto.TreatmentPlan?.Trim();
        consultation.Prescription = dto.Prescription?.Trim();
        consultation.Status = dto.Status;
        consultation.Fee = dto.Fee;
        consultation.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Updated consultation #{ConsultationId}.", id);

        return MapToResponseDto(consultation);
    }

    public async Task<bool> DeleteConsultationAsync(int id, CancellationToken cancellationToken = default)
    {
        var consultation = await _dbContext.Consultations.FindAsync(new object[] { id }, cancellationToken);
        if (consultation == null)
        {
            return false;
        }

        _dbContext.Consultations.Remove(consultation);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Deleted consultation #{ConsultationId}.", id);

        return true;
    }

    private static ConsultationResponseDto MapToResponseDto(Consultation c)
    {
        return new ConsultationResponseDto
        {
            Id = c.Id,
            PatientId = c.PatientId,
            PatientName = c.Patient != null ? $"{c.Patient.FirstName} {c.Patient.LastName}".Trim() : string.Empty,
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
        };
    }
}
