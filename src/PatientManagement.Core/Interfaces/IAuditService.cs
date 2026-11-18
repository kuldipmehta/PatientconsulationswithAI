using PatientManagement.Core.DTOs.Ai;
using PatientManagement.Core.DTOs.Audit;

namespace PatientManagement.Core.Interfaces;

public interface IAuditService
{
    Task<IReadOnlyList<AuditLogDto>> GetAuditLogsAsync(int take = 100, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AiInteractionLogDto>> GetAiInteractionLogsAsync(int take = 100, CancellationToken cancellationToken = default);
    Task<AiUsageSummaryDto> GetAiUsageSummaryAsync(DateTime? fromUtc = null, DateTime? toUtc = null, CancellationToken cancellationToken = default);
}
