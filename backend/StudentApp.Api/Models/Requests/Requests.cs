using System.ComponentModel.DataAnnotations;

namespace StudentApp.Api.Models.Requests;

public record LoginRequest(
    [Required, StringLength(100)] string Username,
    [Required, StringLength(200)] string Password
);

public record CreateUserRequest(
    [Required, StringLength(100, MinimumLength = 3)] string Username,
    [Required, StringLength(200)] string DisplayName,
    [Required, StringLength(200, MinimumLength = 6)] string Password
);

public record ChangePasswordRequest(
    [Required] string CurrentPassword,
    [Required, StringLength(200, MinimumLength = 6)] string NewPassword
);

public record UpdateConfigurationRequest
{
    public string? GeminiApiKey { get; init; }
    public string? AnthropicApiKey { get; init; }
    public string? UniversityName { get; init; }
    public string? Faculty { get; init; }
    public string? FieldOfStudy { get; init; }
    public string? AcademicYear { get; init; }
    public int? StudyYear { get; init; }
    public string? DeanGroup { get; init; }
    public string? GeminiModel { get; init; }
    public string? AnthropicModel { get; init; }
}

public record SendMessageRequest(
    [Required, StringLength(10000)] string Content
);

public record CreateSessionRequest(
    [Required, StringLength(300)] string Title,
    [Required] string Category
);

public record RefreshTokenRequest(
    [Required] string RefreshToken
);
