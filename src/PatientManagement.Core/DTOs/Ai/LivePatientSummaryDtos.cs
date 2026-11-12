namespace PatientManagement.Core.DTOs.Ai;

public class SummarizePatientRequestDto
{
    /// <summary>
    /// Optional focus instructions (e.g. "Focus on allergies and medication history", "Prepare specialist referral brief")
    /// </summary>
    public string? CustomInstructions { get; set; }

    /// <summary>
    /// Optional: Gemini model override (defaults to server configuration)
    /// </summary>
    public string? Model { get; set; }
}

public class LivePatientSummaryResponseDto
{
    public int PatientId { get; set; }
    public string PatientLabel { get; set; } = string.Empty;
    public int TotalConsultationsFetchedFromDb { get; set; }
    public string AiGeneratedSummary { get; set; } = string.Empty;
    public List<string> AiIdentifiedRisks { get; set; } = new();
    public List<string> AiRecommendedActions { get; set; } = new();
    public string UrgencyAssessment { get; set; } = "Routine";
    public List<AiSourceCitationDto> SourceCitations { get; set; } = new();

    /// <summary>
    /// PII-minimized anonymized clinical data sent to Gemini
    /// </summary>
    public object? SanitizedClinicalDataUsed { get; set; }

    public bool IsSimulatedMode { get; set; }
    public string? SimulatedModeNotice => IsSimulatedMode ? AiSafetyConstants.SimulatedModeBanner : null;
    public string Disclaimer { get; set; } = AiSafetyConstants.ClinicalDisclaimer;
    public string ModelUsed { get; set; } = "gemini-flash-latest";
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
}
