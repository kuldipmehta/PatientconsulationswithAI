using System.Text.Json.Serialization;

namespace PatientManagement.Core.DTOs.Ai;

public sealed class PythonHealthResponseDto
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("service")]
    public string Service { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }
}

public sealed class PythonEngineStatusDto
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("gemini_configured")]
    public bool GeminiConfigured { get; set; }

    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("fastapi_lifespan_ready")]
    public bool FastApiLifespanReady { get; set; }

    [JsonPropertyName("uptime_seconds")]
    public double UptimeSeconds { get; set; }
}

public sealed class PythonVitalSignsDto
{
    [JsonPropertyName("systolic_bp")]
    public int? SystolicBp { get; set; }

    [JsonPropertyName("diastolic_bp")]
    public int? DiastolicBp { get; set; }

    [JsonPropertyName("heart_rate_bpm")]
    public int? HeartRateBpm { get; set; }

    [JsonPropertyName("blood_glucose_mg_dl")]
    public double? BloodGlucoseMgDl { get; set; }

    [JsonPropertyName("hba1c_percent")]
    public double? Hba1cPercent { get; set; }
}

public sealed class PythonMedicationDto
{
    [JsonPropertyName("medication_name")]
    public string MedicationName { get; set; } = string.Empty;

    [JsonPropertyName("dosage")]
    public string? Dosage { get; set; }

    [JsonPropertyName("is_active")]
    public bool IsActive { get; set; } = true;

    [JsonPropertyName("contraindication_warning")]
    public string? ContraindicationWarning { get; set; }
}

public sealed class PythonClinicalRecordDto
{
    [JsonPropertyName("vitals")]
    public PythonVitalSignsDto Vitals { get; set; } = new();

    [JsonPropertyName("diagnosed_conditions")]
    public List<string> DiagnosedConditions { get; set; } = new();

    [JsonPropertyName("medications")]
    public List<PythonMedicationDto> Medications { get; set; } = new();

    [JsonPropertyName("urgency_level")]
    public string UrgencyLevel { get; set; } = "routine";

    [JsonPropertyName("requires_immediate_escalation")]
    public bool RequiresImmediateEscalation { get; set; }

    [JsonPropertyName("extraction_timestamp")]
    public DateTime ExtractionTimestamp { get; set; }
}

public sealed class PythonClinicalExtractRequestDto
{
    [JsonPropertyName("raw_clinical_text")]
    public string RawClinicalText { get; set; } = string.Empty;

    [JsonPropertyName("patient_id")]
    public int? PatientId { get; set; }
}

public sealed class PythonDocumentIngestRequestDto
{
    [JsonPropertyName("document_id")]
    public string DocumentId { get; set; } = string.Empty;

    [JsonPropertyName("patient_id")]
    public int PatientId { get; set; }

    [JsonPropertyName("document_type")]
    public string DocumentType { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("chunk_size")]
    public int ChunkSize { get; set; } = 350;

    [JsonPropertyName("chunk_overlap")]
    public int ChunkOverlap { get; set; } = 50;
}

public sealed class PythonDocumentIngestResponseDto
{
    [JsonPropertyName("job_id")]
    public string JobId { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("document_id")]
    public string DocumentId { get; set; } = string.Empty;

    [JsonPropertyName("patient_id")]
    public int PatientId { get; set; }
}

public sealed class PythonDocumentChunkDto
{
    [JsonPropertyName("chunk_index")]
    public int ChunkIndex { get; set; }

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("char_start")]
    public int CharStart { get; set; }

    [JsonPropertyName("char_end")]
    public int CharEnd { get; set; }

    [JsonPropertyName("embedding_preview")]
    public List<double> EmbeddingPreview { get; set; } = new();
}

public sealed class PythonJobStatusResponseDto
{
    [JsonPropertyName("job_id")]
    public string JobId { get; set; } = string.Empty;

    [JsonPropertyName("document_id")]
    public string DocumentId { get; set; } = string.Empty;

    [JsonPropertyName("patient_id")]
    public int PatientId { get; set; }

    [JsonPropertyName("document_type")]
    public string DocumentType { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("chunks_count")]
    public int ChunksCount { get; set; }

    [JsonPropertyName("chunks")]
    public List<PythonDocumentChunkDto> Chunks { get; set; } = new();

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("completed_at")]
    public DateTime? CompletedAt { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}
