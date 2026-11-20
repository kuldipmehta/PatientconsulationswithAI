using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using PatientManagement.Core.DTOs.Ai;
using PatientManagement.Core.DTOs.Common;
using PatientManagement.Core.Interfaces;

namespace PatientManagement.Api.Controllers;

/// <summary>
/// Polyglot API Gateway exposing Python FastAPI AI Microservice capabilities
/// to authorized clinicians (structured extraction, async document ingestion, health probes).
/// </summary>
[ApiController]
[Route("api/ai/microservice")]
[Authorize(Roles = "Admin,Doctor")]
[EnableRateLimiting("ai")]
[Produces("application/json")]
public class PythonAiController : ControllerBase
{
    private readonly IPythonAiServiceClient _pythonClient;
    private readonly ILogger<PythonAiController> _logger;

    public PythonAiController(
        IPythonAiServiceClient pythonClient,
        ILogger<PythonAiController> logger)
    {
        _pythonClient = pythonClient;
        _logger = logger;
    }

    /// <summary>
    /// Probes Python FastAPI microservice health readiness.
    /// </summary>
    [HttpGet("health")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<PythonHealthResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetHealth(CancellationToken cancellationToken)
    {
        var health = await _pythonClient.CheckHealthAsync(cancellationToken);
        if (health == null)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                ApiResponse<object>.Fail("Python AI Microservice is currently unreachable or offline."));
        }

        return Ok(ApiResponse<PythonHealthResponseDto>.Ok(health, "Python AI Microservice is healthy and ready."));
    }

    /// <summary>
    /// Queries the underlying AI engine execution status and lifespan health.
    /// </summary>
    [HttpGet("engine")]
    [ProducesResponseType(typeof(ApiResponse<PythonEngineStatusDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetEngineStatus(CancellationToken cancellationToken)
    {
        var status = await _pythonClient.GetEngineStatusAsync(cancellationToken);
        if (status == null)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                ApiResponse<object>.Fail("Python AI Microservice engine status unavailable."));
        }

        return Ok(ApiResponse<PythonEngineStatusDto>.Ok(status, "Python AI Microservice engine status retrieved."));
    }

    /// <summary>
    /// Executes structured clinical entity extraction with Pydantic v2 validation and safety guardrails.
    /// </summary>
    [HttpPost("extract")]
    [ProducesResponseType(typeof(ApiResponse<PythonClinicalRecordDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExtractClinicalEntities(
        [FromBody] PythonClinicalExtractRequestDto request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request?.RawClinicalText) || request.RawClinicalText.Length < 10)
        {
            return BadRequest(ApiResponse<object>.Fail("Raw clinical text must be at least 10 characters long."));
        }

        var result = await _pythonClient.ExtractClinicalRecordAsync(request, cancellationToken);

        _logger.LogInformation(
            "Extracted clinical record via Python AI Microservice for patient {PatientId}. Urgency: {Urgency}, Escalation: {Escalation}",
            request.PatientId.HasValue ? request.PatientId.Value.ToString() : "N/A",
            result.UrgencyLevel,
            result.RequiresImmediateEscalation);

        return Ok(ApiResponse<PythonClinicalRecordDto>.Ok(result, "Structured clinical entities extracted successfully."));
    }

    /// <summary>
    /// Enqueues a clinical document for asynchronous background sliding-window chunking and vector indexing.
    /// </summary>
    [HttpPost("documents/ingest")]
    [ProducesResponseType(typeof(ApiResponse<PythonDocumentIngestResponseDto>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> IngestDocument(
        [FromBody] PythonDocumentIngestRequestDto request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request?.Content) || request.Content.Length < 10)
        {
            return BadRequest(ApiResponse<object>.Fail("Document content must be at least 10 characters long."));
        }

        var result = await _pythonClient.IngestDocumentAsync(request, cancellationToken);

        _logger.LogInformation(
            "Enqueued document {DocumentId} for patient {PatientId} into Python AI Microservice. JobId: {JobId}",
            request.DocumentId,
            request.PatientId,
            result.JobId);

        return Accepted(ApiResponse<PythonDocumentIngestResponseDto>.Ok(result, result.Message));
    }

    /// <summary>
    /// Checks the status and retrieves chunks for an asynchronous document ingestion job.
    /// </summary>
    [HttpGet("documents/jobs/{jobId}")]
    [ProducesResponseType(typeof(ApiResponse<PythonJobStatusResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetJobStatus(string jobId, CancellationToken cancellationToken)
    {
        var job = await _pythonClient.GetDocumentJobStatusAsync(jobId, cancellationToken);
        if (job == null)
        {
            return NotFound(ApiResponse<object>.Fail($"Document ingestion job '{jobId}' was not found."));
        }

        return Ok(ApiResponse<PythonJobStatusResponseDto>.Ok(job, $"Job status: {job.Status}"));
    }

    /// <summary>
    /// Lists all document ingestion jobs, optionally filtered by patient ID.
    /// </summary>
    [HttpGet("documents/jobs")]
    [ProducesResponseType(typeof(ApiResponse<List<PythonJobStatusResponseDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListJobs([FromQuery] int? patientId, CancellationToken cancellationToken)
    {
        var jobs = await _pythonClient.ListDocumentJobsAsync(patientId, cancellationToken);
        return Ok(ApiResponse<List<PythonJobStatusResponseDto>>.Ok(jobs, $"Retrieved {jobs.Count} document job(s)."));
    }
}
