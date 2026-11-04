using PatientManagement.Core.DTOs.Consultations;

namespace PatientManagement.Core.Interfaces;

public interface IConsultationService
{
    Task<IEnumerable<ConsultationResponseDto>> GetConsultationsAsync(int? patientId = null, CancellationToken cancellationToken = default);
    Task<ConsultationResponseDto?> GetConsultationByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<ConsultationResponseDto> CreateConsultationAsync(CreateConsultationDto dto, CancellationToken cancellationToken = default);
    Task<ConsultationResponseDto?> UpdateConsultationAsync(int id, UpdateConsultationDto dto, CancellationToken cancellationToken = default);
    Task<bool> DeleteConsultationAsync(int id, CancellationToken cancellationToken = default);
}
