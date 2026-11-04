namespace PatientManagement.Core.DTOs.Ai;

public class PatientClinicalSummaryDto
{
    public int PatientId { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public int Age { get; set; }
    public string Gender { get; set; } = string.Empty;
    public string ClinicalSummary { get; set; } = string.Empty;
    public List<string> KnownConditionsAndAllergies { get; set; } = new();
    public List<string> RecentConsultationHighlights { get; set; } = new();
    public List<string> ActionableRecommendations { get; set; } = new();
    public string UrgencyLevel { get; set; } = "Routine"; // "Routine", "Moderate", "Urgent"
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
}
