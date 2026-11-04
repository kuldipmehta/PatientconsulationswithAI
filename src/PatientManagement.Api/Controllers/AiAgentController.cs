using Microsoft.AspNetCore.Mvc;
using PatientManagement.Core.DTOs.Ai;
using PatientManagement.Core.DTOs.Common;
using PatientManagement.Core.Interfaces;

namespace PatientManagement.Api.Controllers;

[ApiController]
[Route("api/ai")]
[Route("api/[controller]")]
[Produces("application/json")]
public class AiAgentController : ControllerBase
{
    private readonly IAiPatientAgentService _aiAgentService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AiAgentController> _logger;

    public AiAgentController(
        IAiPatientAgentService aiAgentService,
        IConfiguration configuration,
        ILogger<AiAgentController> logger)
    {
        _aiAgentService = aiAgentService;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Check AI Agent status and configured model
    /// </summary>
    /// <returns>AI Agent operational status and model information</returns>
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
            OperationalMode = hasKey ? $"Live Gemini ({model})" : "Local Intelligent Agent (Simulated / Fallback Mode)",
            AvailableTools = new[]
            {
                "search_patients (Query SQL Server by name/email)",
                "get_patient_details (Fetch full profile and past consultations)",
                "schedule_consultation (Autonomously book doctor appointment)",
                "get_all_consultations (Filter consultations by patient)"
            },
            Timestamp = DateTime.UtcNow
        };

        return Ok(ApiResponse<object>.Ok(statusInfo, "AI Agent service is active and ready"));
    }

    /// <summary>
    /// Lists all Google Gemini models available for generateContent with the configured or provided API key
    /// </summary>
    /// <param name="apiKey">Optional API key passed as query parameter</param>
    /// <param name="apiKeyHeader">Optional API key passed via X-Gemini-ApiKey header</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of supported model identifiers (e.g. gemini-2.5-flash, gemini-2.0-flash)</returns>
    [HttpGet("models")]
    [ProducesResponseType(typeof(ApiResponse<List<string>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetModels(
        [FromQuery] string? apiKey,
        [FromHeader(Name = "X-Gemini-ApiKey")] string? apiKeyHeader,
        CancellationToken cancellationToken)
    {
        var effectiveKey = !string.IsNullOrWhiteSpace(apiKey) ? apiKey : apiKeyHeader;
        var models = await _aiAgentService.GetAvailableModelsAsync(effectiveKey, cancellationToken);
        return Ok(ApiResponse<List<string>>.Ok(models, "Available Gemini models retrieved successfully"));
    }

    /// <summary>
    /// Conversational AI Agent with autonomous tool-calling: queries SQL Server database, retrieves records, schedules consultations, and answers medical questions
    /// </summary>
    /// <param name="request">Chat message, conversation history, optional API key, and optional active patient context ID</param>
    /// <param name="apiKeyHeader">Optional Gemini API Key passed via X-Gemini-ApiKey header</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>AI response along with executed database tools</returns>
    [HttpPost("chat")]
    [ProducesResponseType(typeof(ApiResponse<AiChatResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Chat(
        [FromBody] AiChatRequestDto request,
        [FromHeader(Name = "X-Gemini-ApiKey")] string? apiKeyHeader,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .ToList();
            return BadRequest(ApiResponse<object>.Fail("Validation failed", errors));
        }

        if (string.IsNullOrWhiteSpace(request.ApiKey) && !string.IsNullOrWhiteSpace(apiKeyHeader))
        {
            request.ApiKey = apiKeyHeader;
        }

        var response = await _aiAgentService.ChatAsync(request, cancellationToken);
        return Ok(ApiResponse<AiChatResponseDto>.Ok(response, "AI Agent processed the request successfully"));
    }

    /// <summary>
    /// Fetches live SQL Server patient demographics and historical consultations, feeds the real database data directly to Gemini AI, and returns a live clinical summary, identified risks, and recommended actions.
    /// </summary>
    /// <param name="id">Patient unique identifier in SQL Server database</param>
    /// <param name="request">Optional custom instructions and optional Gemini API Key</param>
    /// <param name="apiKeyHeader">Optional Gemini API Key passed via X-Gemini-ApiKey header</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Live AI summary grounded in real database data</returns>
    [HttpPost("patients/{id:int}/live-summary")]
    [ProducesResponseType(typeof(ApiResponse<LivePatientSummaryResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SummarizePatientLive(
        int id,
        [FromBody] SummarizePatientRequestDto? request,
        [FromHeader(Name = "X-Gemini-ApiKey")] string? apiKeyHeader,
        CancellationToken cancellationToken)
    {
        try
        {
            var effectiveApiKey = !string.IsNullOrWhiteSpace(request?.ApiKey) ? request.ApiKey : apiKeyHeader;
            var summary = await _aiAgentService.SummarizePatientLiveAsync(
                id,
                request?.CustomInstructions,
                effectiveApiKey,
                request?.Model,
                cancellationToken);
            return Ok(ApiResponse<LivePatientSummaryResponseDto>.Ok(summary, "Live patient database summary generated successfully"));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<object>.Fail(ex.Message));
        }
    }

    /// <summary>
    /// Generate a structured clinical summary of a patient's medical history, past diagnoses, risk factors, and actionable recommendations using Gemini
    /// </summary>
    /// <param name="id">Patient unique identifier</param>
    /// <param name="model">Optional Gemini model identifier (e.g. gemini-2.5-flash)</param>
    /// <param name="apiKeyHeader">Optional Gemini API Key passed via X-Gemini-ApiKey header</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Structured clinical summary</returns>
    [HttpPost("patients/{id:int}/clinical-summary")]
    [ProducesResponseType(typeof(ApiResponse<PatientClinicalSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GenerateClinicalSummary(
        int id,
        [FromQuery] string? model,
        [FromHeader(Name = "X-Gemini-ApiKey")] string? apiKeyHeader,
        CancellationToken cancellationToken)
    {
        try
        {
            var summary = await _aiAgentService.GenerateClinicalSummaryAsync(id, apiKeyHeader, model, cancellationToken);
            return Ok(ApiResponse<PatientClinicalSummaryDto>.Ok(summary, "Clinical summary generated successfully"));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<object>.Fail(ex.Message));
        }
    }

    /// <summary>
    /// AI Symptom Triage: analyzes patient symptoms, estimates clinical urgency, and suggests the appropriate medical specialty and next steps
    /// </summary>
    /// <param name="request">Symptom description, optional patient age, gender, and pre-existing conditions</param>
    /// <param name="model">Optional Gemini model identifier (e.g. gemini-2.5-flash)</param>
    /// <param name="apiKeyHeader">Optional Gemini API Key passed via X-Gemini-ApiKey header</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Triage recommendation and red flag screening questions</returns>
    [HttpPost("triage")]
    [ProducesResponseType(typeof(ApiResponse<AiTriageResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Triage(
        [FromBody] AiTriageRequestDto request,
        [FromQuery] string? model,
        [FromHeader(Name = "X-Gemini-ApiKey")] string? apiKeyHeader,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .ToList();
            return BadRequest(ApiResponse<object>.Fail("Validation failed", errors));
        }

        var triageResult = await _aiAgentService.TriageSymptomsAsync(request, apiKeyHeader, model, cancellationToken);
        return Ok(ApiResponse<AiTriageResponseDto>.Ok(triageResult, "Symptom triage completed successfully"));
    }
}
