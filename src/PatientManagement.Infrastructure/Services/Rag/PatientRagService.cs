using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PatientManagement.Core.DTOs.Rag;
using PatientManagement.Core.Entities;
using PatientManagement.Core.Enums;
using PatientManagement.Core.Exceptions;
using PatientManagement.Core.Interfaces;
using PatientManagement.Infrastructure.Data;

namespace PatientManagement.Infrastructure.Services.Rag;

public class PatientRagService : IPatientRagService
{
    private const long MaxFileSize = 10 * 1024 * 1024; // 10 MB
    private static readonly byte[] PdfMagicBytes = "%PDF-"u8.ToArray();

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "is", "are", "was", "were", "be", "been", "being",
        "in", "on", "at", "to", "for", "with", "from", "by", "about", "of",
        "and", "or", "what", "which", "who", "whom", "this", "that", "these",
        "those", "does", "did", "have", "has", "had", "can", "could", "should",
        "would", "any", "some", "tell", "show", "give", "me", "it", "its",
        "their", "his", "her", "my", "our", "you", "your",
        // Generic clinical-document words: they appear in nearly every chunk and would make any chunk a "match".
        "patient", "patients", "value", "values", "result", "results", "report", "document", "level"
    };

    private readonly ApplicationDbContext _dbContext;
    private readonly IBlobStorageService _blobStorageService;
    private readonly ITextEmbeddingService _embeddingService;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<PatientRagService> _logger;
    private readonly double _minScoreFloor;
    private readonly ISemanticCacheService? _semanticCache;
    private readonly IRagRerankerService? _reranker;
    private readonly bool _rerankerEnabled;
    private readonly int _candidateMultiplier;

    public PatientRagService(
        ApplicationDbContext dbContext,
        IBlobStorageService blobStorageService,
        ITextEmbeddingService embeddingService,
        ICurrentUserService currentUser,
        ILogger<PatientRagService> logger,
        IConfiguration? configuration = null,
        ISemanticCacheService? semanticCache = null,
        IRagRerankerService? reranker = null)
    {
        _dbContext = dbContext;
        _blobStorageService = blobStorageService;
        _embeddingService = embeddingService;
        _currentUser = currentUser;
        _logger = logger;
        _semanticCache = semanticCache;
        _reranker = reranker;
        // Lower bound for relevance. Callers can ask for a stricter minScore but never a looser one.
        _minScoreFloor = configuration?.GetValue("Rag:MinScoreFloor", 0.0) ?? 0.0;
        _rerankerEnabled = configuration?.GetValue("Rag:Reranker:Enabled", true) ?? true;
        _candidateMultiplier = configuration?.GetValue("Rag:Reranker:CandidateMultiplier", 3) ?? 3;
    }

    public async Task<PatientDocumentResponseDto> UploadAndIndexDocumentAsync(
        int patientId,
        IFormFile file,
        UploadPatientDocumentDto dto,
        CancellationToken cancellationToken = default)
    {
        var patient = await EnsurePatientAccessAsync(patientId, cancellationToken);

        if (file == null || file.Length == 0)
        {
            throw new ArgumentException("A valid non-empty file must be provided.");
        }

        if (file.Length > MaxFileSize)
        {
            throw new ArgumentException("File size exceeds maximum allowed limit of 10 MB.");
        }

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext is not (".pdf" or ".txt"))
        {
            throw new ArgumentException("Unsupported file type. Only .pdf and .txt clinical documents are accepted.");
        }

        byte[] fileBytes;
        using (var ms = new MemoryStream())
        {
            await file.CopyToAsync(ms, cancellationToken);
            fileBytes = ms.ToArray();
        }

        ValidateMagicBytes(fileBytes, ext);

        string extractedText;
        if (ext == ".pdf")
        {
            extractedText = PdfTextExtractor.ExtractText(fileBytes);
        }
        else
        {
            extractedText = Encoding.UTF8.GetString(fileBytes);
        }

        if (string.IsNullOrWhiteSpace(extractedText))
        {
            throw new InvalidOperationException("No readable text could be extracted from the document.");
        }

        var chunks = TextChunker.ChunkText(extractedText, maxChunkSize: 500, overlapSize: 100);
        if (chunks.Count == 0)
        {
            chunks.Add(extractedText[..Math.Min(extractedText.Length, 500)]);
        }

        // Embed BEFORE writing anything (blob or database). If the embedding service is unavailable the request
        // fails with 503 and no half-indexed document is left behind.
        var embeddings = await _embeddingService.GenerateBatchEmbeddingsAsync(chunks, cancellationToken);
        EnsureEmbeddingCount(embeddings, chunks.Count);

        // Upload to private blob container
        string? blobName = null;
        using (var uploadStream = new MemoryStream(fileBytes))
        {
            var uniqueFileName = $"doc_{patientId}_{Guid.NewGuid():N}_{Path.GetFileName(file.FileName)}";
            var uploadResult = await _blobStorageService.UploadAsync(
                uploadStream,
                uniqueFileName,
                file.ContentType ?? (ext == ".pdf" ? "application/pdf" : "text/plain"),
                containerName: "patient-documents",
                cancellationToken: cancellationToken);
            blobName = uploadResult.BlobName;
        }

        var document = new PatientDocument
        {
            PatientId = patientId,
            ConsultationId = dto.ConsultationId,
            FileName = Path.GetFileName(file.FileName),
            ContentType = file.ContentType ?? (ext == ".pdf" ? "application/pdf" : "text/plain"),
            FileSizeBytes = file.Length,
            BlobName = blobName,
            DocumentType = string.IsNullOrWhiteSpace(dto.DocumentType) ? "General" : dto.DocumentType,
            Status = "Indexed",
            TotalChunks = chunks.Count,
            Summary = dto.Summary,
            UploadedByUserId = _currentUser.UserId ?? 0,
            CreatedAtUtc = DateTime.UtcNow
        };

        for (int i = 0; i < chunks.Count; i++)
        {
            var emb = embeddings[i];
            document.Chunks.Add(new PatientDocumentChunk
            {
                PatientId = patientId,
                ConsultationId = dto.ConsultationId,
                ChunkIndex = i + 1,
                SourceType = "PatientDocument",
                SourceLabel = $"{document.FileName} (Section {i + 1}/{chunks.Count})",
                TextContent = chunks[i],
                EmbeddingVectorJson = VectorOperations.SerializeVector(emb),
                EmbeddingDimensions = emb.Length,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        _dbContext.PatientDocuments.Add(document);

        try
        {
            // One SaveChanges: the document and all of its chunks are stored together or not at all.
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await TryDeleteBlobAsync(blobName);
            throw;
        }

        _logger.LogInformation(
            "Document #{DocId} '{FileName}' indexed successfully for patient #{PatientId} with {ChunksCount} chunks.",
            document.Id, document.FileName, patientId, chunks.Count);

        _semanticCache?.InvalidateScope($"patient:{patientId}");
        _semanticCache?.InvalidateScope($"patient:{patientId}:summary");

        return MapToResponseDto(document);
    }

    public async Task<List<PatientDocumentResponseDto>> GetPatientDocumentsAsync(
        int patientId,
        CancellationToken cancellationToken = default)
    {
        await EnsurePatientAccessAsync(patientId, cancellationToken);

        var docs = await _dbContext.PatientDocuments
            .Where(d => d.PatientId == patientId)
            .OrderByDescending(d => d.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return docs.Select(MapToResponseDto).ToList();
    }

    public async Task<bool> DeleteDocumentAsync(
        int patientId,
        int documentId,
        CancellationToken cancellationToken = default)
    {
        await EnsurePatientAccessAsync(patientId, cancellationToken);

        var doc = await _dbContext.PatientDocuments
            .Include(d => d.Chunks)
            .FirstOrDefaultAsync(d => d.Id == documentId && d.PatientId == patientId, cancellationToken);

        if (doc == null)
        {
            return false;
        }

        doc.IsDeleted = true;
        doc.DeletedAtUtc = DateTime.UtcNow;
        doc.DeletedByUserId = _currentUser.UserId;

        _dbContext.PatientDocumentChunks.RemoveRange(doc.Chunks);

        if (!string.IsNullOrEmpty(doc.BlobName))
        {
            try
            {
                await _blobStorageService.DeleteAsync(doc.BlobName, containerName: "patient-documents", cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete blob {BlobName} from storage.", doc.BlobName);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<List<RagSearchResultDto>> SearchPatientKnowledgeAsync(
        int patientId,
        string query,
        int topK = 5,
        double minScore = 0.15,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new List<RagSearchResultDto>();
        }

        await EnsurePatientAccessAsync(patientId, cancellationToken);

        var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query, cancellationToken);

        var chunks = await _dbContext.PatientDocumentChunks
            .AsNoTracking()
            .Where(c => c.PatientId == patientId)
            .ToListAsync(cancellationToken);

        var queryWords = query.ToLowerInvariant()
            .Split(new[] { ' ', '\r', '\n', '\t', '.', ',', ';', ':', '(', ')', '[', ']', '-', '/', '\\', '?', '!', '"', '\'' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(w => w.Length >= 2 && !StopWords.Contains(w))
            .Distinct()
            .ToList();

        var effectiveMinScore = Math.Max(minScore, _minScoreFloor);
        var queryMatchers = queryWords.Select(BuildWordMatcher).ToList();
        var staleEmbeddingChunks = 0;

        var scored = new List<(PatientDocumentChunk Chunk, double Score)>();

        foreach (var c in chunks)
        {
            var vec = VectorOperations.DeserializeVector(c.EmbeddingVectorJson);
            double vectorScore = 0.0;

            if (vec.Length > 0 && queryEmbedding.Length > 0)
            {
                if (vec.Length == queryEmbedding.Length)
                {
                    vectorScore = VectorOperations.CosineSimilarity(queryEmbedding, vec);
                }
                else
                {
                    // Embedded with a different model/dimension. Comparing against a made-up vector would be
                    // meaningless, so score this chunk by keywords only and flag it for re-indexing.
                    staleEmbeddingChunks++;
                }
            }

            int matchedKeywords = 0;
            if (queryWords.Count > 0)
            {
                foreach (var matcher in queryMatchers)
                {
                    if (matcher.IsMatch(c.TextContent))
                    {
                        matchedKeywords++;
                    }
                }
            }

            double keywordRatio = queryWords.Count > 0 ? (double)matchedKeywords / queryWords.Count : 0.0;

            double finalScore;
            if (matchedKeywords > 0)
            {
                // Significant clinical keyword boost for specific lab tests (e.g. SGPT, ALT), medications, or symptom terms
                var keywordBoost = 0.35 + (keywordRatio * 0.55);
                finalScore = Math.Max(vectorScore, keywordBoost);
                if (vectorScore > 0)
                {
                    finalScore = Math.Max(finalScore, (vectorScore * 0.4) + (keywordBoost * 0.6));
                }
            }
            else
            {
                finalScore = vectorScore;
            }

            if (finalScore >= effectiveMinScore)
            {
                scored.Add((c, finalScore));
            }
        }

        if (staleEmbeddingChunks > 0)
        {
            _logger.LogWarning(
                "{Count} chunks of patient #{PatientId} use embeddings from a different model or dimension and were scored by keywords only. Run the patient re-index.",
                staleEmbeddingChunks, patientId);
        }

        var candidateMultiplier = (_rerankerEnabled && _reranker != null) ? Math.Max(1, _candidateMultiplier) : 1;
        var candidatePool = scored
            .OrderByDescending(s => s.Score)
            .Take(topK * candidateMultiplier)
            .ToList();

        if (_rerankerEnabled && _reranker != null && candidatePool.Count > 0)
        {
            var candidates = candidatePool.Select(s => new RerankCandidate(
                s.Chunk.Id,
                s.Chunk.TextContent,
                s.Score,
                s.Chunk.SourceType,
                s.Chunk.SourceLabel,
                s.Chunk.PatientDocumentId,
                s.Chunk.ConsultationId,
                s.Chunk.PatientId,
                s.Chunk.CreatedAtUtc
            )).ToList();

            var reranked = await _reranker.RerankAsync(query, candidates, topK, cancellationToken);
            return reranked.Select(r => new RagSearchResultDto
            {
                ChunkId = r.ChunkId,
                PatientId = r.PatientId,
                DocumentId = r.DocumentId,
                ConsultationId = r.ConsultationId,
                SourceType = r.SourceType,
                SourceLabel = r.SourceLabel,
                TextContent = r.TextContent,
                SimilarityScore = Math.Round(r.InitialScore, 4),
                RelevanceScore = Math.Round(r.RelevanceScore, 4),
                IsCompressed = r.IsCompressed,
                CreatedAtUtc = r.CreatedAtUtc
            }).ToList();
        }

        return candidatePool
            .Take(topK)
            .Select(s => new RagSearchResultDto
            {
                ChunkId = s.Chunk.Id,
                PatientId = s.Chunk.PatientId,
                DocumentId = s.Chunk.PatientDocumentId,
                ConsultationId = s.Chunk.ConsultationId,
                SourceType = s.Chunk.SourceType,
                SourceLabel = s.Chunk.SourceLabel,
                TextContent = s.Chunk.TextContent,
                SimilarityScore = Math.Round(s.Score, 4),
                RelevanceScore = Math.Round(s.Score, 4),
                IsCompressed = false,
                CreatedAtUtc = s.Chunk.CreatedAtUtc
            })
            .ToList();
    }

    public async Task IndexConsultationAsync(
        int consultationId,
        CancellationToken cancellationToken = default)
    {
        var consultation = await _dbContext.Consultations
            .Include(c => c.Patient)
            .FirstOrDefaultAsync(c => c.Id == consultationId, cancellationToken);

        if (consultation == null || consultation.IsDeleted)
        {
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Patient #{consultation.PatientId} Consultation on {consultation.ConsultationDate:yyyy-MM-dd}.");
        sb.AppendLine($"Clinician: {consultation.DoctorName}. Status: {consultation.Status}.");
        sb.AppendLine($"Diagnosis: {consultation.Diagnosis}.");

        if (!string.IsNullOrWhiteSpace(consultation.Symptoms))
        {
            sb.AppendLine($"Reported Symptoms: {consultation.Symptoms}.");
        }
        if (!string.IsNullOrWhiteSpace(consultation.Notes))
        {
            sb.AppendLine($"Clinical Notes: {consultation.Notes}.");
        }
        if (!string.IsNullOrWhiteSpace(consultation.TreatmentPlan))
        {
            sb.AppendLine($"Treatment Plan: {consultation.TreatmentPlan}.");
        }
        if (!string.IsNullOrWhiteSpace(consultation.Prescription))
        {
            sb.AppendLine($"Prescriptions / Medication: {consultation.Prescription}.");
        }

        var fullText = sb.ToString().Trim();
        var chunks = TextChunker.ChunkText(fullText, maxChunkSize: 500, overlapSize: 100);
        if (chunks.Count == 0)
        {
            chunks.Add(fullText);
        }

        var existingChunks = await _dbContext.PatientDocumentChunks
            .Where(c => c.ConsultationId == consultationId && c.SourceType == "Consultation")
            .ToListAsync(cancellationToken);

        if (existingChunks.Count > 0)
        {
            _dbContext.PatientDocumentChunks.RemoveRange(existingChunks);
        }

        var embeddings = await _embeddingService.GenerateBatchEmbeddingsAsync(chunks, cancellationToken);
        EnsureEmbeddingCount(embeddings, chunks.Count);

        for (int i = 0; i < chunks.Count; i++)
        {
            var emb = embeddings[i];
            _dbContext.PatientDocumentChunks.Add(new PatientDocumentChunk
            {
                PatientId = consultation.PatientId,
                ConsultationId = consultation.Id,
                ChunkIndex = i + 1,
                SourceType = "Consultation",
                SourceLabel = $"Consultation #{consultation.Id} ({consultation.ConsultationDate:yyyy-MM-dd}) — {consultation.DoctorName}",
                TextContent = chunks[i],
                EmbeddingVectorJson = VectorOperations.SerializeVector(emb),
                EmbeddingDimensions = emb.Length,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Indexed consultation #{ConsultationId} into {Count} RAG chunks.", consultationId, chunks.Count);
    }

    public async Task<int> ReindexPatientConsultationsAsync(
        int patientId,
        CancellationToken cancellationToken = default)
    {
        await EnsurePatientAccessAsync(patientId, cancellationToken);

        var consultations = await _dbContext.Consultations
            .Where(c => c.PatientId == patientId && !c.IsDeleted)
            .ToListAsync(cancellationToken);

        int indexedCount = 0;
        foreach (var c in consultations)
        {
            await IndexConsultationAsync(c.Id, cancellationToken);
            indexedCount++;
        }

        // Also re-embed any existing uploaded document chunks for this patient using current embedding model
        var documentChunks = await _dbContext.PatientDocumentChunks
            .Where(c => c.PatientId == patientId && c.PatientDocumentId != null)
            .ToListAsync(cancellationToken);

        if (documentChunks.Count > 0)
        {
            var chunkTexts = documentChunks.Select(c => c.TextContent).ToList();
            var embeddings = await _embeddingService.GenerateBatchEmbeddingsAsync(chunkTexts, cancellationToken);
            EnsureEmbeddingCount(embeddings, chunkTexts.Count);

            for (int i = 0; i < documentChunks.Count; i++)
            {
                if (i < embeddings.Count)
                {
                    var emb = embeddings[i];
                    documentChunks[i].EmbeddingVectorJson = VectorOperations.SerializeVector(emb);
                    documentChunks[i].EmbeddingDimensions = emb.Length;
                }
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Re-embedded {Count} document chunks for patient #{PatientId}.", documentChunks.Count, patientId);
        }

        _semanticCache?.InvalidateScope($"patient:{patientId}");
        _semanticCache?.InvalidateScope($"patient:{patientId}:summary");

        return indexedCount;
    }

    private async Task<Patient> EnsurePatientAccessAsync(int patientId, CancellationToken cancellationToken)
    {
        if (_currentUser is { IsAuthenticated: true, Role: UserRole.Doctor } && !_currentUser.DoctorId.HasValue)
        {
            throw new ForbiddenAccessException("Doctor account is not linked to a valid DoctorId.");
        }

        var patient = await _dbContext.Patients
            .FirstOrDefaultAsync(p => p.Id == patientId, cancellationToken);

        if (patient == null)
        {
            throw new KeyNotFoundException($"Patient with ID {patientId} not found.");
        }

        if (_currentUser is { IsAuthenticated: true, Role: UserRole.Doctor } &&
            patient.PrimaryDoctorId != _currentUser.DoctorId)
        {
            throw new ForbiddenAccessException("You are not authorized to view or modify records for this patient.");
        }

        return patient;
    }

    /// <summary>Whole-word, case-insensitive match (plain plurals allowed), so "alt" does not match "salt" or "default".</summary>
    private static Regex BuildWordMatcher(string word) =>
        new(@"(?<![\p{L}\p{N}])" + Regex.Escape(word) + @"s?(?![\p{L}\p{N}])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(200));

    private static void EnsureEmbeddingCount(IReadOnlyCollection<float[]> embeddings, int expected)
    {
        if (embeddings.Count != expected || embeddings.Any(e => e.Length == 0))
        {
            throw new EmbeddingUnavailableException(
                $"Expected {expected} embeddings but received {embeddings.Count}. Nothing was indexed.");
        }
    }

    private async Task TryDeleteBlobAsync(string? blobName)
    {
        if (string.IsNullOrWhiteSpace(blobName))
        {
            return;
        }

        try
        {
            await _blobStorageService.DeleteAsync(blobName, containerName: "patient-documents", cancellationToken: CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not clean up blob {BlobName} after a failed document save.", blobName);
        }
    }

    private static void ValidateMagicBytes(byte[] fileBytes, string ext)
    {
        if (ext == ".pdf")
        {
            if (fileBytes.Length < PdfMagicBytes.Length ||
                !fileBytes.AsSpan(0, PdfMagicBytes.Length).SequenceEqual(PdfMagicBytes))
            {
                throw new ArgumentException("Invalid file format. The uploaded file does not match the PDF binary signature.");
            }
        }
        else if (ext == ".txt")
        {
            var checkLength = Math.Min(fileBytes.Length, 512);
            for (int i = 0; i < checkLength; i++)
            {
                if (fileBytes[i] == 0)
                {
                    throw new ArgumentException("Invalid text format. Binary characters detected in text file.");
                }
            }
        }
    }

    private static PatientDocumentResponseDto MapToResponseDto(PatientDocument doc) =>
        new()
        {
            Id = doc.Id,
            PatientId = doc.PatientId,
            ConsultationId = doc.ConsultationId,
            FileName = doc.FileName,
            ContentType = doc.ContentType,
            FileSizeBytes = doc.FileSizeBytes,
            DocumentType = doc.DocumentType,
            Status = doc.Status,
            TotalChunks = doc.TotalChunks,
            Summary = doc.Summary,
            CreatedAtUtc = doc.CreatedAtUtc
        };
}
