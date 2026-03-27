namespace StudentApp.Api.Data.Entities;

/// <summary>
/// Represents a single conversation session. Each session belongs to a category
/// (Gemini, OpenClaw, UniScraper) and contains ordered messages.
/// </summary>
public class ChatSession
{
    public int Id { get; set; }
    public Guid PublicId { get; set; } = Guid.NewGuid();
    
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    
    public string Title { get; set; } = "Nowa rozmowa";
    public ChatCategory Category { get; set; }
    
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    public ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
}

public enum ChatCategory
{
    Gemini = 0,
    OpenClaw = 1,
    UniScraper = 2
}
