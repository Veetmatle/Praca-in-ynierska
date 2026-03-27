namespace StudentApp.Api.Models.Responses;

public record AuthResponse(
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAt,
    string Username,
    string DisplayName,
    string Role
);

public record ApiError(string Message, IDictionary<string, string[]>? Errors = null);
