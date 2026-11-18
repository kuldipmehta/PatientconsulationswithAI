using System.Text.Json.Serialization;

namespace PatientManagement.Core.DTOs.Ai;

public enum ClinicalUrgencyLevel
{
    Routine,
    Moderate,
    Urgent,
    Emergency
}

public enum TriageUrgencyLevel
{
    Low,
    Medium,
    High,
    Emergency
}

/// <summary>
/// Strongly-typed structured JSON contract returned by Gemini for patient clinical summaries.
/// </summary>
public sealed class ClinicalSummaryResult
{
    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    [JsonPropertyName("identifiedRisks")]
    public List<string>? IdentifiedRisks { get; set; }

    [JsonPropertyName("recommendedActions")]
    public List<string>? RecommendedActions { get; set; }

    [JsonPropertyName("urgencyAssessment")]
    public ClinicalUrgencyLevel? UrgencyAssessment { get; set; }
}

/// <summary>
/// Strongly-typed structured JSON contract returned by Gemini for symptom triage assessments.
/// </summary>
public sealed class TriageAssessmentResult
{
    [JsonPropertyName("urgencyLevel")]
    public TriageUrgencyLevel? UrgencyLevel { get; set; }

    [JsonPropertyName("recommendedSpecialty")]
    public string? RecommendedSpecialty { get; set; }

    [JsonPropertyName("triageNotes")]
    public string? TriageNotes { get; set; }

    [JsonPropertyName("redFlagQuestions")]
    public List<string>? RedFlagQuestions { get; set; }

    [JsonPropertyName("suggestedNextSteps")]
    public List<string>? SuggestedNextSteps { get; set; }
}
