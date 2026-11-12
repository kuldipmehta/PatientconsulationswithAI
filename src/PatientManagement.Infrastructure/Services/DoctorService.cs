using Microsoft.EntityFrameworkCore;
using PatientManagement.Core.DTOs.Doctors;
using PatientManagement.Core.Enums;
using PatientManagement.Core.Interfaces;
using PatientManagement.Infrastructure.Data;

namespace PatientManagement.Infrastructure.Services;

public class DoctorService : IDoctorService
{
    private readonly ApplicationDbContext _dbContext;

    public DoctorService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<DoctorDto>> GetDoctorsAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Doctors
            .AsNoTracking()
            .Where(d => d.IsActive)
            .OrderBy(d => d.FullName)
            .Select(d => new DoctorDto
            {
                Id = d.Id,
                FullName = d.FullName,
                Specialty = d.Specialty,
                Email = d.Email,
                StartHourUtc = d.StartHourUtc,
                EndHourUtc = d.EndHourUtc,
                SlotDurationMinutes = d.SlotDurationMinutes
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<DoctorAvailabilityResponseDto?> GetAvailabilityAsync(
        int doctorId,
        DateTime dateUtc,
        CancellationToken cancellationToken = default)
    {
        var doctor = await _dbContext.Doctors
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == doctorId && d.IsActive, cancellationToken);

        if (doctor == null)
        {
            return null;
        }

        var dayStart = DateTime.SpecifyKind(dateUtc.Date, DateTimeKind.Utc);
        var dayEnd = dayStart.AddDays(1);

        var bookedSlots = await _dbContext.Consultations
            .AsNoTracking()
            .Where(c =>
                (c.DoctorId == doctorId || c.DoctorName == doctor.FullName) &&
                c.Status != ConsultationStatus.Cancelled &&
                c.ConsultationDate >= dayStart &&
                c.ConsultationDate < dayEnd)
            .Select(c => new
            {
                Start = c.ConsultationDate,
                End = c.ConsultationDate.AddMinutes(c.DurationMinutes > 0 ? c.DurationMinutes : 30)
            })
            .ToListAsync(cancellationToken);

        var slots = new List<TimeSlotDto>();
        var slotLength = Math.Max(15, doctor.SlotDurationMinutes);
        var cursor = dayStart.AddHours(doctor.StartHourUtc);
        var workEnd = dayStart.AddHours(doctor.EndHourUtc);

        while (cursor.AddMinutes(slotLength) <= workEnd)
        {
            var slotEnd = cursor.AddMinutes(slotLength);
            bool overlaps = bookedSlots.Any(b => cursor < b.End && slotEnd > b.Start);

            slots.Add(new TimeSlotDto
            {
                StartUtc = cursor,
                EndUtc = slotEnd,
                IsAvailable = !overlaps
            });

            cursor = slotEnd;
        }

        return new DoctorAvailabilityResponseDto
        {
            DoctorId = doctor.Id,
            DoctorName = doctor.FullName,
            Specialty = doctor.Specialty,
            DateUtc = dayStart,
            Slots = slots
        };
    }
}
