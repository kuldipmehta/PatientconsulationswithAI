using System.ComponentModel.DataAnnotations;

namespace PatientManagement.Core.DTOs.Ai;

public class AiChatRequestDto
{
    [Required(ErrorMessage = "Message is required.")]
    [StringLength(2000, ErrorMessage = "Message cannot exceed 2000 characters.")]
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Optional server-side session identifier for maintaining multi-turn history.
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// Optional: Explicitly anchor this conversation to a specific patient ID.
    /// </summary>
    public int? PatientContextId { get; set; }

    /// <summary>
    /// Optional: Gemini model override (defaults to server configuration).
    /// </summary>
    public string? Model { get; set; }
}
