using PatientManagement.Core.DTOs.Ai;
using PatientManagement.Core.DTOs.Consultations;

namespace PatientManagement.Core.Interfaces;

public interface IAiPatientAgentService
{
    Task<AiChatResponseDto> ChatAsync(AiChatRequestDto request, CancellationToken cancellationToken = default);

    IAsyncEnumerable<string> StreamChatSseAsync(AiChatRequestDto request, CancellationToken cancellationToken = default);

    Task<ConsultationResponseDto> ConfirmPendingActionAsync(string actionId, CancellationToken cancellationToken = default);

    Task<bool> CancelPendingActionAsync(string actionId, CancellationToken cancellationToken = default);

    Task<LivePatientSummaryResponseDto> SummarizePatientLiveAsync(
        int patientId,
        string? customInstructions = null,
        string? explicitModel = null,
        CancellationToken cancellationToken = default);

    Task<PatientClinicalSummaryDto> GenerateClinicalSummaryAsync(
        int patientId,
        string? explicitModel = null,
        CancellationToken cancellationToken = default);

    Task<AiTriageResponseDto> TriageSymptomsAsync(
        AiTriageRequestDto request,
        string? explicitModel = null,
        CancellationToken cancellationToken = default);

    Task<List<string>> GetAvailableModelsAsync(CancellationToken cancellationToken = default);
}
