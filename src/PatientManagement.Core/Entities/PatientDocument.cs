namespace PatientManagement.Core.Entities;

public class PatientDocument
{
    public int Id { get; set; }
    public int PatientId { get; set; }
    public Patient Patient { get; set; } = null!;

    public int? ConsultationId { get; set; }
    public Consultation? Consultation { get; set; }

    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string? BlobName { get; set; }
    public string DocumentType { get; set; } = "General";
    public string Status { get; set; } = "Indexed";
    public int TotalChunks { get; set; }
    public string? Summary { get; set; }

    public int UploadedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public int? DeletedByUserId { get; set; }

    public ICollection<PatientDocumentChunk> Chunks { get; set; } = new List<PatientDocumentChunk>();
}
