namespace StudentApp.Api.Data.Entities;

/// <summary>
/// Stores per-user configuration including encrypted API keys.
/// All sensitive fields are AES-GCM encrypted at rest; decrypted only in-memory when needed.
/// </summary>
public class UserConfiguration
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    
    /// <summary>Encrypted Gemini API key (AES-GCM, base64-encoded ciphertext).</summary>
    public string? GeminiApiKeyEncrypted { get; set; }
    
    /// <summary>Encrypted Anthropic/Claude API key.</summary>
    public string? AnthropicApiKeyEncrypted { get; set; }
    
    /// <summary>University name (e.g. "Politechnika Krakowska").</summary>
    public string? UniversityName { get; set; }
    
    /// <summary>Faculty or department (e.g. "WIiT").</summary>
    public string? Faculty { get; set; }
    
    /// <summary>Field of study (kierunek studiów).</summary>
    public string? FieldOfStudy { get; set; }
    
    /// <summary>Academic year (e.g. "2025/2026").</summary>
    public string? AcademicYear { get; set; }
    
    /// <summary>Year of study (e.g. 2).</summary>
    public int? StudyYear { get; set; }
    
    /// <summary>Dean's group identifier.</summary>
    public string? DeanGroup { get; set; }
    
    /// <summary>Preferred Gemini model name.</summary>
    public string GeminiModel { get; set; } = "gemini-2.5-flash";
    
    /// <summary>Preferred Anthropic model name.</summary>
    public string AnthropicModel { get; set; } = "claude-sonnet-4-20250514";
    
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
