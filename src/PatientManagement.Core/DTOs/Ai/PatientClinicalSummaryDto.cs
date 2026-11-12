namespace PatientManagement.Core.DTOs.Ai;

public class PatientClinicalSummaryDto
{
    public int PatientId { get; set; }
    public string PatientLabel { get; set; } = string.Empty;
    public int Age { get; set; }
    public string Gender { get; set; } = string.Empty;
    public string ClinicalSummary { get; set; } = string.Empty;
    public List<string> KnownConditionsAndAllergies { get; set; } = new();
    public List<string> RecentConsultationHighlights { get; set; } = new();
    public List<string> ActionableRecommendations { get; set; } = new();
    public List<AiSourceCitationDto> SourceCitations { get; set; } = new();
    public string UrgencyLevel { get; set; } = "Routine"; // "Routine", "Moderate", "Urgent"
    public bool IsSimulatedMode { get; set; }
    public string? SimulatedModeNotice => IsSimulatedMode ? AiSafetyConstants.SimulatedModeBanner : null;
    public string Disclaimer { get; set; } = AiSafetyConstants.ClinicalDisclaimer;
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
}
