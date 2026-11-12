using PatientManagement.Core.DTOs.Doctors;

namespace PatientManagement.Core.Interfaces;

public interface IDoctorService
{
    Task<IReadOnlyList<DoctorDto>> GetDoctorsAsync(CancellationToken cancellationToken = default);
    Task<DoctorAvailabilityResponseDto?> GetAvailabilityAsync(int doctorId, DateTime dateUtc, CancellationToken cancellationToken = default);
}
