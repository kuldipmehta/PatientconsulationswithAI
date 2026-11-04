namespace PatientManagement.Core.DTOs.Ai;

public class SummarizePatientRequestDto
{
    /// <summary>
    /// Optional focus instructions (e.g. "Focus on allergies and medication history", "Prepare specialist referral brief")
    /// </summary>
    public string? CustomInstructions { get; set; }

    /// <summary>
    /// Optional: Your Google Gemini API Key (if not configured in appsettings.json or environment variable)
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Optional: Gemini model override (defaults to gemini-flash-latest or auto-detected active model)
    /// </summary>
    public string? Model { get; set; }
}

public class LivePatientSummaryResponseDto
{
    public int PatientId { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public int TotalConsultationsFetchedFromDb { get; set; }
    public string AiGeneratedSummary { get; set; } = string.Empty;
    public List<string> AiIdentifiedRisks { get; set; } = new();
    public List<string> AiRecommendedActions { get; set; } = new();
    public string UrgencyAssessment { get; set; } = "Routine";
    
    /// <summary>
    /// The actual raw patient and consultation data fetched from SQL Server that was provided to Gemini
    /// </summary>
    public object? LiveDatabaseDataUsed { get; set; }
    
    public string ModelUsed { get; set; } = "gemini-flash-latest";
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
}
