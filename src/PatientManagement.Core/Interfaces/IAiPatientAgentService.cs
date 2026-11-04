using PatientManagement.Core.DTOs.Ai;

namespace PatientManagement.Core.Interfaces;

public interface IAiPatientAgentService
{
    /// <summary>
    /// Multi-turn conversational AI Agent powered by Google Gemini with autonomous tool-calling
    /// capabilities (search patients, retrieve patient records, schedule consultations, etc.)
    /// </summary>
    Task<AiChatResponseDto> ChatAsync(AiChatRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches live patient profile and all consultation records from SQL Server, feeds the real
    /// data directly to Google Gemini, and returns a live clinical summary with risks and recommendations.
    /// </summary>
    Task<LivePatientSummaryResponseDto> SummarizePatientLiveAsync(
        int patientId,
        string? customInstructions = null,
        string? explicitApiKey = null,
        string? explicitModel = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Analyzes a patient's medical record, history, and past consultations to produce a clinical summary
    /// </summary>
    Task<PatientClinicalSummaryDto> GenerateClinicalSummaryAsync(
        int patientId,
        string? explicitApiKey = null,
        string? explicitModel = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs AI symptom triage, assessing urgency and recommending the appropriate doctor specialty
    /// </summary>
    Task<AiTriageResponseDto> TriageSymptomsAsync(
        AiTriageRequestDto request,
        string? explicitApiKey = null,
        string? explicitModel = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries Google Gemini's ListModels endpoint with the provided API key
    /// and returns the list of active models available for generateContent.
    /// </summary>
    Task<List<string>> GetAvailableModelsAsync(
        string? explicitApiKey = null,
        CancellationToken cancellationToken = default);
}
