namespace PatientManagement.Core.Interfaces;

public record SemanticCacheResult(
    string CachedPrompt,
    string ResponseContent,
    string ModelUsed,
    double SimilarityScore,
    string Scope,
    DateTime CachedAtUtc,
    int? PromptTokens = null,
    int? CompletionTokens = null,
    int? TotalTokens = null,
    string? PromptVersion = null);

public interface ISemanticCacheService
{
    Task<SemanticCacheResult?> GetAsync(
        string prompt,
        string? scope = null,
        double? minSimilarityThreshold = null,
        CancellationToken cancellationToken = default);

    Task SetAsync(
        string prompt,
        string responseContent,
        string modelUsed,
        string? scope = null,
        TimeSpan? ttl = null,
        int? promptTokens = null,
        int? completionTokens = null,
        int? totalTokens = null,
        string? promptVersion = null,
        CancellationToken cancellationToken = default);

    void InvalidateScope(string scope);

    void Clear();

    int Count { get; }
}
