using StudentApp.Api.Data.Entities;

namespace StudentApp.Api.Models.DTOs;

public record UserDto(
    Guid PublicId,
    string Username,
    string DisplayName,
    string Role,
    DateTime CreatedAt,
    DateTime? LastLoginAt
);

public record UserConfigurationDto(
    bool HasGeminiKey,
    bool HasAnthropicKey,
    string? UniversityName,
    string? Faculty,
    string? FieldOfStudy,
    string? AcademicYear,
    int? StudyYear,
    string? DeanGroup,
    string GeminiModel,
    string AnthropicModel
);

public record ChatSessionDto(
    Guid PublicId,
    string Title,
    string Category,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    bool IsPinned,
    int MessageCount,
    string? LastMessage
);

public record ChatMessageDto(
    long Id,
    string Role,
    string Content,
    int? TokenCount,
    DateTime CreatedAt
);

public record ChatSessionDetailDto(
    Guid PublicId,
    string Title,
    string Category,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    List<ChatMessageDto> Messages
);

public static class DtoMappers
{
    public static UserDto ToDto(this User user) => new(
        user.PublicId, user.Username, user.DisplayName,
        user.Role.ToString(), user.CreatedAt, user.LastLoginAt
    );

    public static UserConfigurationDto ToDto(this UserConfiguration cfg) => new(
        !string.IsNullOrEmpty(cfg.GeminiApiKeyEncrypted),
        !string.IsNullOrEmpty(cfg.AnthropicApiKeyEncrypted),
        cfg.UniversityName, cfg.Faculty, cfg.FieldOfStudy,
        cfg.AcademicYear, cfg.StudyYear, cfg.DeanGroup,
        cfg.GeminiModel, cfg.AnthropicModel
    );

    public static ChatSessionDto ToDto(this ChatSession session) => new(
        session.PublicId, session.Title, session.Category.ToString(),
        session.CreatedAt, session.UpdatedAt, session.IsPinned, session.Messages.Count,
        session.Messages.OrderByDescending(m => m.CreatedAt).FirstOrDefault()?.Content
    );

    public static ChatMessageDto ToDto(this ChatMessage msg) => new(
        msg.Id, msg.Role.ToString(), msg.Content, msg.TokenCount, msg.CreatedAt
    );
}
