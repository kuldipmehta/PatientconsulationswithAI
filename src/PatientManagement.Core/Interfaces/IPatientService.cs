using PatientManagement.Core.DTOs.Common;
using PatientManagement.Core.DTOs.Patients;

namespace PatientManagement.Core.Interfaces;

public interface IPatientService
{
    Task<PagedResult<PatientSummaryDto>> GetPatientsAsync(PatientFilterParams filter, CancellationToken cancellationToken = default);
    Task<PatientResponseDto?> GetPatientByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<PatientResponseDto> CreatePatientAsync(CreatePatientDto dto, CancellationToken cancellationToken = default);
    Task<PatientResponseDto?> UpdatePatientAsync(int id, UpdatePatientDto dto, CancellationToken cancellationToken = default);
    Task<bool> DeletePatientAsync(int id, CancellationToken cancellationToken = default);
}
