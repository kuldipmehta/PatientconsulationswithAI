namespace PatientManagement.Core.DTOs.Ai;

public record ClinicalExemplarDto(
    string Id,
    string Domain,
    string ScenarioTitle,
    string Query,
    string IdealOutput,
    string ClinicalRationale,
    double? RelevanceScore = null
);

public record ExemplarSearchRequestDto(
    string Query,
    string? Domain = null,
    int TopK = 2,
    double MinSimilarity = 0.55
);
