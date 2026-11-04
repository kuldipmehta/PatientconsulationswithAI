using System.ComponentModel.DataAnnotations;

namespace PatientManagement.Core.DTOs.Ai;

public class AiTriageRequestDto
{
    [Required(ErrorMessage = "Symptoms description is required.")]
    public string Symptoms { get; set; } = string.Empty;

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
    public DateTime AnalyzedAtUtc { get; set; } = DateTime.UtcNow;
}
