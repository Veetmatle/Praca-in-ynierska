namespace StudentApp.Api.Data.Entities;

/// <summary>
/// Application user with role-based access. Supports soft delete for data integrity.
/// </summary>
public class User
{
    public int Id { get; set; }
    public Guid PublicId { get; set; } = Guid.NewGuid();
    
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    
    public UserRole Role { get; set; } = UserRole.User;
    
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
    
    // Navigation
    public UserConfiguration? Configuration { get; set; }
    public ICollection<ChatSession> ChatSessions { get; set; } = new List<ChatSession>();
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
}

public enum UserRole
{
    User = 0,
    Admin = 1
}
