using PatientManagement.Core.DTOs.Ai;

namespace PatientManagement.Core.Interfaces;

public interface IExemplarMemoryService
{
    Task<IReadOnlyList<ClinicalExemplarDto>> GetRelevantExemplarsAsync(
        string query,
        string? domain = null,
        int topK = 2,
        double minSimilarity = 0.55,
        CancellationToken cancellationToken = default);

    string FormatFewShotPrompt(IReadOnlyList<ClinicalExemplarDto> exemplars);

    IReadOnlyList<ClinicalExemplarDto> GetAllExemplars();
}
