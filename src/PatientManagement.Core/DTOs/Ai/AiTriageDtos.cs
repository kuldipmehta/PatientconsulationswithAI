using System.ComponentModel.DataAnnotations;

namespace PatientManagement.Core.DTOs.Ai;

public class AiTriageRequestDto
{
    [Required(ErrorMessage = "Symptoms description is required.")]
    [StringLength(2000, ErrorMessage = "Symptoms description cannot exceed 2000 characters.")]
    public string Symptoms { get; set; } = string.Empty;

    [Range(0, 130)]
    public int? PatientAge { get; set; }
    public string? Gender { get; set; }
    public string? ExistingConditions { get; set; }
}

public class AiTriageResponseDto
{
    public string UrgencyLevel { get; set; } = "Low"; // "Low", "Medium", "High", "Emergency"
    public string RecommendedSpecialty { get; set; } = "General Physician";
    public string TriageNotes { get; set; } = string.Empty;
    public List<string> RedFlagQuestions { get; set; } = new();
    public List<string> SuggestedNextSteps { get; set; } = new();
    public bool RuleBasedEmergencyTriggered { get; set; }
    public string? MatchedEmergencyRule { get; set; }
    public bool IsSimulatedMode { get; set; }
    public string? SimulatedModeNotice => IsSimulatedMode ? AiSafetyConstants.SimulatedModeBanner : null;
    public string Disclaimer { get; set; } = AiSafetyConstants.ClinicalDisclaimer;
    public DateTime AnalyzedAtUtc { get; set; } = DateTime.UtcNow;
}
