using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PatientManagement.Core.Interfaces;
using PatientManagement.Infrastructure.Services.Rag;

namespace PatientManagement.Infrastructure.Services.Ai;

public class MemorySemanticCacheService : ISemanticCacheService
{
    private readonly ITextEmbeddingService _embeddingService;
    private readonly ILogger<MemorySemanticCacheService> _logger;
    private readonly bool _enabled;
    private readonly double _defaultThreshold;
    private readonly TimeSpan _defaultTtl;

    private readonly Lock _syncLock = new();
    private readonly List<InternalCacheEntry> _entries = new();

    public MemorySemanticCacheService(
        ITextEmbeddingService embeddingService,
        IConfiguration configuration,
        ILogger<MemorySemanticCacheService> logger)
    {
        _embeddingService = embeddingService;
        _logger = logger;
        _enabled = configuration.GetValue("Ai:SemanticCache:Enabled", true);
        _defaultThreshold = configuration.GetValue("Ai:SemanticCache:SimilarityThreshold", 0.93);
        var ttlMinutes = configuration.GetValue("Ai:SemanticCache:DefaultTtlMinutes", 120);
        _defaultTtl = TimeSpan.FromMinutes(Math.Max(1, ttlMinutes));
    }

    public int Count
    {
        get
        {
            lock (_syncLock)
            {
                PurgeExpiredUnsafe();
                return _entries.Count;
            }
        }
    }

    public async Task<SemanticCacheResult?> GetAsync(
        string prompt,
        string? scope = null,
        double? minSimilarityThreshold = null,
        CancellationToken cancellationToken = default)
    {
        if (!_enabled || string.IsNullOrWhiteSpace(prompt))
        {
            return null;
        }

        var normalizedScope = NormalizeScope(scope);
        var threshold = minSimilarityThreshold ?? _defaultThreshold;

        // Check exact match first under lock (fast path without embedding)
        lock (_syncLock)
        {
            PurgeExpiredUnsafe();
            var exact = _entries.FirstOrDefault(e =>
                string.Equals(e.Scope, normalizedScope, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(e.Prompt.Trim(), prompt.Trim(), StringComparison.OrdinalIgnoreCase) &&
                !e.IsExpired);

            if (exact != null)
            {
                _logger.LogInformation("Semantic cache EXACT hit for scope '{Scope}'.", normalizedScope);
                return new SemanticCacheResult(
                    exact.Prompt,
                    exact.ResponseContent,
                    exact.ModelUsed,
                    1.0,
                    exact.Scope,
                    exact.CreatedAtUtc,
                    exact.PromptTokens,
                    exact.CompletionTokens,
                    exact.TotalTokens,
                    exact.PromptVersion);
            }
        }

        // Semantic embedding lookup
        float[] queryEmbedding;
        try
        {
            queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(prompt, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not generate embedding for semantic cache lookup. Skipping cache.");
            return null;
        }

        if (queryEmbedding.Length == 0)
        {
            return null;
        }

        lock (_syncLock)
        {
            PurgeExpiredUnsafe();

            InternalCacheEntry? bestMatch = null;
            double highestScore = -1.0;

            foreach (var entry in _entries)
            {
                if (!string.Equals(entry.Scope, normalizedScope, StringComparison.OrdinalIgnoreCase) || entry.IsExpired)
                {
                    continue;
                }

                var score = VectorOperations.CosineSimilarity(queryEmbedding, entry.Embedding);
                if (score > highestScore)
                {
                    highestScore = score;
                    bestMatch = entry;
                }
            }

            if (bestMatch != null && highestScore >= threshold)
            {
                _logger.LogInformation(
                    "Semantic cache HIT (Score {Score:F4} >= {Threshold:F4}) in scope '{Scope}'.",
                    highestScore, threshold, normalizedScope);

                return new SemanticCacheResult(
                    bestMatch.Prompt,
                    bestMatch.ResponseContent,
                    bestMatch.ModelUsed,
                    highestScore,
                    bestMatch.Scope,
                    bestMatch.CreatedAtUtc,
                    bestMatch.PromptTokens,
                    bestMatch.CompletionTokens,
                    bestMatch.TotalTokens,
                    bestMatch.PromptVersion);
            }

            _logger.LogDebug(
                "Semantic cache MISS (Highest score {Score:F4} < {Threshold:F4}) in scope '{Scope}'.",
                highestScore < 0 ? 0 : highestScore, threshold, normalizedScope);
            return null;
        }
    }

    public async Task SetAsync(
        string prompt,
        string responseContent,
        string modelUsed,
        string? scope = null,
        TimeSpan? ttl = null,
        int? promptTokens = null,
        int? completionTokens = null,
        int? totalTokens = null,
        string? promptVersion = null,
        CancellationToken cancellationToken = default)
    {
        if (!_enabled || string.IsNullOrWhiteSpace(prompt) || string.IsNullOrWhiteSpace(responseContent))
        {
            return;
        }

        var normalizedScope = NormalizeScope(scope);

        float[] embedding;
        try
        {
            embedding = await _embeddingService.GenerateEmbeddingAsync(prompt, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not generate embedding for semantic cache store. Skipping cache set.");
            return;
        }

        if (embedding.Length == 0)
        {
            return;
        }

        var effectiveTtl = ttl ?? _defaultTtl;
        var now = DateTime.UtcNow;

        var newEntry = new InternalCacheEntry
        {
            Prompt = prompt.Trim(),
            Embedding = embedding,
            ResponseContent = responseContent,
            ModelUsed = modelUsed,
            Scope = normalizedScope,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.Add(effectiveTtl),
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            TotalTokens = totalTokens,
            PromptVersion = promptVersion
        };

        lock (_syncLock)
        {
            PurgeExpiredUnsafe();

            // Replace existing exact prompt in the same scope if present
            _entries.RemoveAll(e =>
                string.Equals(e.Scope, normalizedScope, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(e.Prompt, newEntry.Prompt, StringComparison.OrdinalIgnoreCase));

            _entries.Add(newEntry);
            _logger.LogInformation("Cached response semantically in scope '{Scope}' with TTL {TtlMinutes}m.",
                normalizedScope, effectiveTtl.TotalMinutes);
        }
    }

    public void InvalidateScope(string scope)
    {
        var normalizedScope = NormalizeScope(scope);
        lock (_syncLock)
        {
            var removed = _entries.RemoveAll(e => string.Equals(e.Scope, normalizedScope, StringComparison.OrdinalIgnoreCase));
            if (removed > 0)
            {
                _logger.LogInformation("Invalidated {Count} semantic cache entries in scope '{Scope}'.", removed, normalizedScope);
            }
        }
    }

    public void Clear()
    {
        lock (_syncLock)
        {
            _entries.Clear();
            _logger.LogInformation("Semantic cache cleared completely.");
        }
    }

    private void PurgeExpiredUnsafe()
    {
        var now = DateTime.UtcNow;
        _entries.RemoveAll(e => now >= e.ExpiresAtUtc);
    }

    private static string NormalizeScope(string? scope)
    {
        return string.IsNullOrWhiteSpace(scope) ? "global" : scope.Trim().ToLowerInvariant();
    }

    private sealed class InternalCacheEntry
    {
        public required string Prompt { get; init; }
        public required float[] Embedding { get; init; }
        public required string ResponseContent { get; init; }
        public required string ModelUsed { get; init; }
        public required string Scope { get; init; }
        public required DateTime CreatedAtUtc { get; init; }
        public required DateTime ExpiresAtUtc { get; init; }
        public int? PromptTokens { get; init; }
        public int? CompletionTokens { get; init; }
        public int? TotalTokens { get; init; }
        public string? PromptVersion { get; init; }

        public bool IsExpired => DateTime.UtcNow >= ExpiresAtUtc;
    }
}
