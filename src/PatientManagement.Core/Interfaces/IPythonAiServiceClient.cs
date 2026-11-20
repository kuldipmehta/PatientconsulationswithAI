using PatientManagement.Core.DTOs.Ai;

namespace PatientManagement.Core.Interfaces;

/// <summary>
/// Polyglot HTTP client interface for communicating with the Python FastAPI AI Microservice.
/// Handles high-speed Pydantic structured clinical entity extraction, document chunking,
/// vector embedding previews, and health monitoring with API-key authentication.
/// </summary>
public interface IPythonAiServiceClient
{
    /// <summary>
    /// Probes the Python FastAPI microservice health readiness.
    /// </summary>
    Task<PythonHealthResponseDto?> CheckHealthAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves underlying AI engine status (Gemini model, lifespan ready, uptime).
    /// </summary>
    Task<PythonEngineStatusDto?> GetEngineStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs structured clinical entity extraction with clinical guardrails and contraindication checks.
    /// </summary>
    Task<PythonClinicalRecordDto> ExtractClinicalRecordAsync(
        PythonClinicalExtractRequestDto request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Submits a document for asynchronous background sliding-window chunking and vector indexing.
    /// </summary>
    Task<PythonDocumentIngestResponseDto> IngestDocumentAsync(
        PythonDocumentIngestRequestDto request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves status and extracted chunks of an asynchronous ingestion job.
    /// </summary>
    Task<PythonJobStatusResponseDto?> GetDocumentJobStatusAsync(
        string jobId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all ingestion jobs, optionally filtered by patient ID.
    /// </summary>
    Task<List<PythonJobStatusResponseDto>> ListDocumentJobsAsync(
        int? patientId = null,
        CancellationToken cancellationToken = default);
}
