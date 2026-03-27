namespace StudentApp.Api.Data.Entities;

/// <summary>
/// Single message within a chat session. Stores both user prompts and AI responses.
/// </summary>
public class ChatMessage
{
    public long Id { get; set; }
    
    public int ChatSessionId { get; set; }
    public ChatSession ChatSession { get; set; } = null!;
    
    public MessageRole Role { get; set; }
    public string Content { get; set; } = string.Empty;
    
    /// <summary>Optional token count for usage tracking.</summary>
    public int? TokenCount { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum MessageRole
{
    User = 0,
    Assistant = 1,
    System = 2
}
