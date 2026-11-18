using Microsoft.AspNetCore.Http;

namespace PatientManagement.Core.DTOs.Rag;

public class PatientDocumentResponseDto
{
    public int Id { get; set; }
    public int PatientId { get; set; }
    public int? ConsultationId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string DocumentType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int TotalChunks { get; set; }
    public string? Summary { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class UploadPatientDocumentDto
{
    public IFormFile? File { get; set; }
    public string DocumentType { get; set; } = "General";
    public int? ConsultationId { get; set; }
    public string? Summary { get; set; }
}

public class SemanticSearchRequestDto
{
    public string Query { get; set; } = string.Empty;
    public int TopK { get; set; } = 5;
    public double MinScore { get; set; } = 0.15;
}

public class RagSearchResultDto
{
    public long ChunkId { get; set; }
    public int PatientId { get; set; }
    public int? DocumentId { get; set; }
    public int? ConsultationId { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public string SourceLabel { get; set; } = string.Empty;
    public string TextContent { get; set; } = string.Empty;
    public double SimilarityScore { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
