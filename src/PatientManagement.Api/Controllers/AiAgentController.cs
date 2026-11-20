using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PatientManagement.Core.DTOs.Ai;
using PatientManagement.Core.DTOs.Common;
using PatientManagement.Core.DTOs.Consultations;
using PatientManagement.Core.Interfaces;

namespace PatientManagement.Api.Controllers;

[ApiController]
[Route("api/ai")]
[Route("api/[controller]")]
[Authorize(Roles = "Admin,Doctor")]
[EnableRateLimiting("ai")]
[Produces("application/json")]
public class AiAgentController : ControllerBase
{
    private readonly IAiPatientAgentService _aiAgentService;
    private readonly IAuditService _auditService;
    private readonly IConfiguration _configuration;
    private readonly ILlmJudgeService _llmJudgeService;
    private readonly IExemplarMemoryService _exemplarMemoryService;

    public AiAgentController(
        IAiPatientAgentService aiAgentService,
        IAuditService auditService,
        IConfiguration configuration,
        ILlmJudgeService llmJudgeService,
        IExemplarMemoryService exemplarMemoryService)
    {
        _aiAgentService = aiAgentService;
        _auditService = auditService;
        _configuration = configuration;
        _llmJudgeService = llmJudgeService;
        _exemplarMemoryService = exemplarMemoryService;
    }

    [HttpGet("status")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public IActionResult GetStatus()
    {
        var apiKey = _configuration["Gemini:ApiKey"] ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        bool hasKey = !string.IsNullOrWhiteSpace(apiKey) && !apiKey.Contains("YOUR_GEMINI_API_KEY", StringComparison.OrdinalIgnoreCase);
        var model = _configuration["Gemini:Model"] ?? "gemini-flash-latest";

        var statusInfo = new
        {
            AgentName = "Healthcare Clinical AI Assistant",
            Engine = "Google Gemini",
            Model = model,
            LiveGeminiApiConnected = hasKey,
            IsSimulatedMode = !hasKey,
            SimulatedModeNotice = !hasKey ? AiSafetyConstants.SimulatedModeBanner : null,
            Disclaimer = AiSafetyConstants.ClinicalDisclaimer,
            AvailableTools = new[]
            {
                "search_patients (Read-only de-identified lookup scoped to clinician)",
                "get_patient_details (Read-only de-identified clinical summary)",
                "get_doctor_availability (Read-only schedule slot lookup)",
                "propose_consultation (Human-in-the-loop proposal requiring clinician confirmation)"
            },
            Timestamp = DateTime.UtcNow
        };

        return Ok(ApiResponse<object>.Ok(statusInfo, "AI Agent service is active"));
    }

    [HttpGet("models")]
    [ProducesResponseType(typeof(ApiResponse<List<string>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetModels(CancellationToken cancellationToken)
    {
        var models = await _aiAgentService.GetAvailableModelsAsync(cancellationToken);
        return Ok(ApiResponse<List<string>>.Ok(models, "Available Gemini models retrieved successfully"));
    }

    [HttpPost("chat")]
    [ProducesResponseType(typeof(ApiResponse<AiChatResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Chat([FromBody] AiChatRequestDto request, CancellationToken cancellationToken)
    {
        var response = await _aiAgentService.ChatAsync(request, cancellationToken);
        return Ok(ApiResponse<AiChatResponseDto>.Ok(response, "AI Agent processed the request"));
    }

    [HttpPost("chat/stream")]
    public async Task StreamChat([FromBody] AiChatRequestDto request, CancellationToken cancellationToken)
    {
        Response.StatusCode = StatusCodes.Status200OK;
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";

        await foreach (var sseChunk in _aiAgentService.StreamChatSseAsync(request, cancellationToken))
        {
            var bytes = Encoding.UTF8.GetBytes(sseChunk);
            await Response.Body.WriteAsync(bytes, cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
    }

    [HttpPost("actions/{actionId}/confirm")]
    [ProducesResponseType(typeof(ApiResponse<ConsultationResponseDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ConfirmAction(string actionId, CancellationToken cancellationToken)
    {
        var created = await _aiAgentService.ConfirmPendingActionAsync(actionId, cancellationToken);
        return Ok(ApiResponse<ConsultationResponseDto>.Ok(created, "Proposed consultation confirmed and scheduled."));
    }

    [HttpPost("actions/{actionId}/cancel")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    public async Task<IActionResult> CancelAction(string actionId, CancellationToken cancellationToken)
    {
        var cancelled = await _aiAgentService.CancelPendingActionAsync(actionId, cancellationToken);
        return cancelled
            ? Ok(ApiResponse<bool>.Ok(true, "Proposed AI action cancelled."))
            : NotFound(ApiResponse<object>.Fail($"Pending action '{actionId}' not found."));
    }

    [HttpPost("patients/{id:int}/live-summary")]
    [ProducesResponseType(typeof(ApiResponse<LivePatientSummaryResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SummarizePatientLive(
        int id,
        [FromBody] SummarizePatientRequestDto? request,
        CancellationToken cancellationToken)
    {
        var summary = await _aiAgentService.SummarizePatientLiveAsync(
            id,
            request?.CustomInstructions,
            request?.Model,
            cancellationToken);
        return Ok(ApiResponse<LivePatientSummaryResponseDto>.Ok(summary, "Live de-identified patient clinical summary generated"));
    }

    [HttpPost("patients/{id:int}/clinical-summary")]
    [ProducesResponseType(typeof(ApiResponse<PatientClinicalSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GenerateClinicalSummary(
        int id,
        [FromQuery] string? model,
        CancellationToken cancellationToken)
    {
        var summary = await _aiAgentService.GenerateClinicalSummaryAsync(id, model, cancellationToken);
        return Ok(ApiResponse<PatientClinicalSummaryDto>.Ok(summary, "Clinical summary generated"));
    }

    [HttpPost("triage")]
    [ProducesResponseType(typeof(ApiResponse<AiTriageResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Triage(
        [FromBody] AiTriageRequestDto request,
        [FromQuery] string? model,
        CancellationToken cancellationToken)
    {
        var triageResult = await _aiAgentService.TriageSymptomsAsync(request, model, cancellationToken);
        return Ok(ApiResponse<AiTriageResponseDto>.Ok(triageResult, "Symptom triage completed"));
    }

    [HttpPost("evaluate")]
    [ProducesResponseType(typeof(ApiResponse<LlmJudgeEvaluationResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Evaluate(
        [FromBody] LlmJudgeEvaluationRequest request,
        CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.GeneratedResponse))
        {
            return BadRequest(ApiResponse<object>.Fail("Evaluation request must include GeneratedResponse."));
        }

        var result = await _llmJudgeService.EvaluateAsync(request, cancellationToken);
        return Ok(ApiResponse<LlmJudgeEvaluationResult>.Ok(result, "LLM Judge evaluation completed successfully."));
    }

    [HttpGet("exemplars")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ClinicalExemplarDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetExemplars(
        [FromQuery] string? query,
        [FromQuery] string? domain,
        [FromQuery] int topK = 5,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            var all = _exemplarMemoryService.GetAllExemplars();
            if (!string.IsNullOrWhiteSpace(domain))
            {
                all = all.Where(e => string.Equals(e.Domain, domain, StringComparison.OrdinalIgnoreCase)).ToList();
            }
            return Ok(ApiResponse<IReadOnlyList<ClinicalExemplarDto>>.Ok(all, "Clinical exemplars retrieved successfully."));
        }

        var relevant = await _exemplarMemoryService.GetRelevantExemplarsAsync(query, domain, topK, cancellationToken: cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<ClinicalExemplarDto>>.Ok(relevant, "Relevant clinical exemplars retrieved."));
    }

    [HttpGet("usage")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ApiResponse<AiUsageSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetUsageSummary(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken cancellationToken)
    {
        var summary = await _auditService.GetAiUsageSummaryAsync(from, to, cancellationToken);
        return Ok(ApiResponse<AiUsageSummaryDto>.Ok(summary, "AI usage summary retrieved successfully."));
    }
}
