using Microsoft.EntityFrameworkCore;
using PatientManagement.Core.DTOs.Audit;
using PatientManagement.Core.Interfaces;
using PatientManagement.Infrastructure.Data;

namespace PatientManagement.Infrastructure.Services;

public class AuditService : IAuditService
{
    private readonly ApplicationDbContext _dbContext;

    public AuditService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<AuditLogDto>> GetAuditLogsAsync(int take = 100, CancellationToken cancellationToken = default)
    {
        return await _dbContext.AuditLogs
            .AsNoTracking()
            .OrderByDescending(a => a.TimestampUtc)
            .Take(Math.Clamp(take, 1, 500))
            .Select(a => new AuditLogDto
            {
                Id = a.Id,
                UserId = a.UserId,
                UserEmail = a.UserEmail,
                UserRole = a.UserRole,
                Action = a.Action,
                EntityName = a.EntityName,
                EntityId = a.EntityId,
                ChangesJson = a.ChangesJson,
                TimestampUtc = a.TimestampUtc
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AiInteractionLogDto>> GetAiInteractionLogsAsync(int take = 100, CancellationToken cancellationToken = default)
    {
        return await _dbContext.AiInteractionLogs
            .AsNoTracking()
            .OrderByDescending(a => a.CreatedAtUtc)
            .Take(Math.Clamp(take, 1, 500))
            .Select(a => new AiInteractionLogDto
            {
                Id = a.Id,
                UserId = a.UserId,
                UserEmail = a.UserEmail,
                UserRole = a.UserRole,
                Endpoint = a.Endpoint,
                PatientId = a.PatientId,
                SanitizedPrompt = a.SanitizedPrompt,
                ToolCallsJson = a.ToolCallsJson,
                ResponseSummary = a.ResponseSummary,
                ModelUsed = a.ModelUsed,
                IsSimulated = a.IsSimulated,
                DurationMs = a.DurationMs,
                CreatedAtUtc = a.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);
    }
}
