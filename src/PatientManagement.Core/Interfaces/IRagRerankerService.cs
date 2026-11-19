namespace PatientManagement.Core.Interfaces;

public record RerankCandidate(
    long ChunkId,
    string TextContent,
    double InitialSimilarityScore,
    string SourceType = "",
    string SourceLabel = "",
    int? DocumentId = null,
    int? ConsultationId = null,
    int PatientId = 0,
    DateTime CreatedAtUtc = default
);

public record RerankResultItem(
    long ChunkId,
    int PatientId,
    int? DocumentId,
    int? ConsultationId,
    string SourceType,
    string SourceLabel,
    string TextContent,
    double InitialScore,
    double RelevanceScore,
    bool IsCompressed = false,
    DateTime CreatedAtUtc = default
);

public interface IRagRerankerService
{
    Task<IReadOnlyList<RerankResultItem>> RerankAsync(
        string query,
        IReadOnlyList<RerankCandidate> candidates,
        int topN,
        CancellationToken cancellationToken = default);
}
