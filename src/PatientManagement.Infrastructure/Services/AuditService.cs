using Microsoft.EntityFrameworkCore;
using PatientManagement.Core.DTOs.Ai;
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
                LatencyMs = a.LatencyMs,
                PromptTokens = a.PromptTokens,
                CompletionTokens = a.CompletionTokens,
                TotalTokens = a.TotalTokens,
                EstimatedCostUsd = a.EstimatedCostUsd,
                CreatedAtUtc = a.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<AiUsageSummaryDto> GetAiUsageSummaryAsync(
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.AiInteractionLogs.AsNoTracking().AsQueryable();

        if (fromUtc.HasValue)
        {
            var fromBound = DateTime.SpecifyKind(fromUtc.Value, DateTimeKind.Utc);
            query = query.Where(a => a.CreatedAtUtc >= fromBound);
        }

        if (toUtc.HasValue)
        {
            var toBound = DateTime.SpecifyKind(toUtc.Value, DateTimeKind.Utc);
            if (toBound.TimeOfDay == TimeSpan.Zero)
            {
                var exclusiveEnd = toBound.AddDays(1);
                query = query.Where(a => a.CreatedAtUtc < exclusiveEnd);
            }
            else
            {
                query = query.Where(a => a.CreatedAtUtc <= toBound);
            }
        }

        var rows = await query
            .Select(a => new
            {
                a.CreatedAtUtc,
                a.ModelUsed,
                PromptTokens = (long)(a.PromptTokens ?? 0),
                CompletionTokens = (long)(a.CompletionTokens ?? 0),
                TotalTokens = (long)(a.TotalTokens ?? 0),
                EstimatedCostUsd = a.EstimatedCostUsd ?? 0m,
                LatencyMs = a.LatencyMs > 0 ? a.LatencyMs : a.DurationMs
            })
            .ToListAsync(cancellationToken);

        var breakdown = rows
            .GroupBy(r => new
            {
                DayUtc = r.CreatedAtUtc.ToString("yyyy-MM-dd"),
                ModelUsed = string.IsNullOrWhiteSpace(r.ModelUsed) ? "unknown" : r.ModelUsed
            })
            .Select(g => new AiUsageDailyModelRowDto
            {
                DayUtc = g.Key.DayUtc,
                ModelUsed = g.Key.ModelUsed,
                Requests = g.Count(),
                PromptTokens = g.Sum(x => x.PromptTokens),
                CompletionTokens = g.Sum(x => x.CompletionTokens),
                TotalTokens = g.Sum(x => x.TotalTokens),
                EstimatedCostUsd = Math.Round(g.Sum(x => x.EstimatedCostUsd), 8, MidpointRounding.AwayFromZero),
                P95LatencyMs = CalculateP95(g.Select(x => x.LatencyMs).ToList())
            })
            .OrderByDescending(r => r.DayUtc, StringComparer.Ordinal)
            .ThenBy(r => r.ModelUsed, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new AiUsageSummaryDto
        {
            FromUtc = fromUtc,
            ToUtc = toUtc,
            TotalRequests = rows.Count,
            TotalPromptTokens = rows.Sum(x => x.PromptTokens),
            TotalCompletionTokens = rows.Sum(x => x.CompletionTokens),
            TotalTokens = rows.Sum(x => x.TotalTokens),
            TotalEstimatedCostUsd = Math.Round(rows.Sum(x => x.EstimatedCostUsd), 8, MidpointRounding.AwayFromZero),
            OverallP95LatencyMs = CalculateP95(rows.Select(x => x.LatencyMs).ToList()),
            Breakdown = breakdown
        };
    }

    private static long CalculateP95(IReadOnlyList<long> latencies)
    {
        if (latencies.Count == 0)
        {
            return 0;
        }

        var sorted = latencies.OrderBy(x => x).ToArray();
        var index = (int)Math.Ceiling(0.95 * sorted.Length) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }
}
