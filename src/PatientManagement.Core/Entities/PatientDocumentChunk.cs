namespace PatientManagement.Core.Entities;

public class PatientDocumentChunk
{
    public long Id { get; set; }

    public int? PatientDocumentId { get; set; }
    public PatientDocument? PatientDocument { get; set; }

    public int PatientId { get; set; }
    public Patient Patient { get; set; } = null!;

    public int? ConsultationId { get; set; }
    public Consultation? Consultation { get; set; }

    public int ChunkIndex { get; set; }
    public string SourceType { get; set; } = "PatientDocument"; // "Consultation", "PatientDocument"
    public string SourceLabel { get; set; } = string.Empty;
    public string TextContent { get; set; } = string.Empty;

    public string EmbeddingVectorJson { get; set; } = "[]";
    public int EmbeddingDimensions { get; set; } = 768;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
