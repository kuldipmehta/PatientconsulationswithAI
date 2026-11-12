namespace PatientManagement.Core.Entities;

public class AiChatSession
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N");
    public int UserId { get; set; }
    public int? PatientContextId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastActivityAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<AiChatSessionMessage> Messages { get; set; } = new List<AiChatSessionMessage>();
}

public class AiChatSessionMessage
{
    public long Id { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public AiChatSession Session { get; set; } = null!;
    public string Role { get; set; } = "user"; // user, assistant
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
