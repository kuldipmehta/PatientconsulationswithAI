using System.ComponentModel.DataAnnotations;

namespace PatientManagement.Core.DTOs.Ai;

public class AiChatRequestDto
{
    [Required(ErrorMessage = "Message is required.")]
    public string Message { get; set; } = string.Empty;

    public List<AiChatMessageDto>? ConversationHistory { get; set; }

    /// <summary>
    /// Optional: Explicitly anchor this conversation to a specific patient ID.
    /// </summary>
    public int? PatientContextId { get; set; }

    /// <summary>
    /// Optional: Your Google Gemini API Key. If provided, overrides any key in appsettings.json.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Optional: Gemini model override (defaults to gemini-2.5-flash or auto-detected active model).
    /// </summary>
    public string? Model { get; set; }
}
