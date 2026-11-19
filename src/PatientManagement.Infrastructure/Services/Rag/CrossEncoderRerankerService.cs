using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PatientManagement.Core.Interfaces;

namespace PatientManagement.Infrastructure.Services.Rag;

public class CrossEncoderRerankerService : IRagRerankerService
{
    private readonly IRagContextCompressor _compressor;
    private readonly ILogger<CrossEncoderRerankerService> _logger;

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "and", "or", "but", "if", "then", "else", "when",
        "at", "by", "for", "with", "about", "against", "between", "into", "through",
        "during", "before", "after", "above", "below", "to", "from", "up", "down",
        "in", "out", "on", "off", "over", "under", "again", "further", "then", "once",
        "here", "there", "all", "any", "both", "each", "few", "more", "most", "other",
        "some", "such", "no", "nor", "not", "only", "own", "same", "so", "than", "too",
        "very", "s", "t", "can", "will", "just", "don", "should", "now", "is", "are",
        "was", "were", "be", "been", "being", "have", "has", "had", "do", "does", "did",
        "patient", "what", "how", "why", "where", "which"
    };

    public CrossEncoderRerankerService(
        IRagContextCompressor compressor,
        ILogger<CrossEncoderRerankerService> logger)
    {
        _compressor = compressor ?? throw new ArgumentNullException(nameof(compressor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<IReadOnlyList<RerankResultItem>> RerankAsync(
        string query,
        IReadOnlyList<RerankCandidate> candidates,
        int topN,
        CancellationToken cancellationToken = default)
    {
        if (candidates == null || candidates.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<RerankResultItem>>(Array.Empty<RerankResultItem>());
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            var fallback = candidates.Take(topN).Select(c => new RerankResultItem(
                c.ChunkId,
                c.PatientId,
                c.DocumentId,
                c.ConsultationId,
                c.SourceType,
                c.SourceLabel,
                c.TextContent,
                c.InitialSimilarityScore,
                c.InitialSimilarityScore,
                false,
                c.CreatedAtUtc
            )).ToList();
            return Task.FromResult<IReadOnlyList<RerankResultItem>>(fallback);
        }

        var queryTerms = ExtractSignificantTokens(query);
        var scoredList = new List<(RerankCandidate Candidate, double CrossScore)>();

        foreach (var c in candidates)
        {
            var score = CalculateCrossAttentionRelevance(query, queryTerms, c.TextContent, c.InitialSimilarityScore);
            scoredList.Add((c, score));
        }

        var reranked = scoredList
            .OrderByDescending(x => x.CrossScore)
            .Take(topN)
            .Select(x =>
            {
                var candidate = x.Candidate;
                var rawContent = candidate.TextContent;
                var compressedContent = _compressor.Compress(query, rawContent, maxWords: 85);
                var isCompressed = !string.Equals(compressedContent.Trim(), rawContent.Trim(), StringComparison.Ordinal);

                return new RerankResultItem(
                    candidate.ChunkId,
                    candidate.PatientId,
                    candidate.DocumentId,
                    candidate.ConsultationId,
                    candidate.SourceType,
                    candidate.SourceLabel,
                    compressedContent,
                    candidate.InitialSimilarityScore,
                    Math.Round(x.CrossScore, 4),
                    isCompressed,
                    candidate.CreatedAtUtc
                );
            })
            .ToList();

        _logger.LogDebug("Reranked {CandidateCount} candidates down to {TopN} items for query: '{Query}'. Top score: {TopScore}",
            candidates.Count, reranked.Count, query, reranked.FirstOrDefault()?.RelevanceScore ?? 0.0);

        return Task.FromResult<IReadOnlyList<RerankResultItem>>(reranked);
    }

    private static double CalculateCrossAttentionRelevance(
        string query,
        List<string> queryTerms,
        string documentText,
        double initialSimilarityScore)
    {
        if (string.IsNullOrWhiteSpace(documentText)) return 0.0;

        // Baseline weight from Stage 1 Bi-Encoder Cosine Similarity (35% weight)
        var score = Math.Clamp(initialSimilarityScore, 0.0, 1.0) * 0.35;

        // 1. Exact phrase match (High precision signal: 25% weight)
        if (query.Length > 3 && documentText.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            score += 0.25;
        }

        if (queryTerms.Count == 0)
        {
            return Math.Clamp(score, 0.0, 1.0);
        }

        // 2. Query Term Coverage (Fraction of query terms present: up to 25% weight)
        var docWords = Regex.Matches(documentText, @"\b[a-zA-Z0-9_\-\.]{2,}\b")
            .Select(m => m.Value.ToLowerInvariant())
            .ToList();

        var matchedTerms = 0;
        var positions = new List<int>();

        for (var i = 0; i < docWords.Count; i++)
        {
            var word = docWords[i];
            if (queryTerms.Any(qt => word.Contains(qt, StringComparison.OrdinalIgnoreCase) || qt.Contains(word, StringComparison.OrdinalIgnoreCase)))
            {
                matchedTerms++;
                positions.Add(i);
            }
        }

        var uniqueMatchedTerms = queryTerms.Count(qt =>
            documentText.Contains(qt, StringComparison.OrdinalIgnoreCase));

        var termCoverageRatio = (double)uniqueMatchedTerms / queryTerms.Count;
        score += termCoverageRatio * 0.25;

        // 3. Proximity / Density Window (If multiple terms appear close together: up to 10% weight)
        if (positions.Count >= 2)
        {
            var minDistance = int.MaxValue;
            for (var i = 0; i < positions.Count - 1; i++)
            {
                var dist = positions[i + 1] - positions[i];
                if (dist > 0 && dist < minDistance) minDistance = dist;
            }

            if (minDistance <= 5)
            {
                score += 0.10;
            }
            else if (minDistance <= 12)
            {
                score += 0.05;
            }
        }

        // 4. Clinical Evidence Co-occurrence (Presence of numbers, units, dosages near match: up to 5% weight)
        if (Regex.IsMatch(documentText, @"(?i)\b\d+(\.\d+)?\s*(%|mg|mcg|ml|mmhg|bpm|mmol|meq)\b"))
        {
            score += 0.05;
        }

        return Math.Clamp(score, 0.0, 1.0);
    }

    private static List<string> ExtractSignificantTokens(string text)
    {
        var terms = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return terms;

        var tokens = Regex.Matches(text, @"\b[a-zA-Z0-9_\-\.]{2,}\b")
            .Select(m => m.Value.ToLowerInvariant());

        foreach (var t in tokens)
        {
            if (!StopWords.Contains(t) && !terms.Contains(t))
            {
                terms.Add(t);
            }
        }

        return terms;
    }
}
