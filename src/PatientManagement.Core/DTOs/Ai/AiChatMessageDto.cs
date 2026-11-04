namespace PatientManagement.Core.DTOs.Ai;

public class AiChatMessageDto
{
    public string Role { get; set; } = "user"; // "user", "model", "system"
    public string Content { get; set; } = string.Empty;
}
