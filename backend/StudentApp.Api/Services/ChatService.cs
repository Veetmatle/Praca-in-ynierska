using Microsoft.EntityFrameworkCore;
using StudentApp.Api.Data;
using StudentApp.Api.Data.Entities;
using StudentApp.Api.Models.DTOs;
using StudentApp.Api.Models.Requests;

namespace StudentApp.Api.Services;

/// <summary>
/// Manages chat sessions and message persistence.
/// </summary>
public interface IChatService
{
    Task<List<ChatSessionDto>> GetUserSessionsAsync(int userId, ChatCategory? category = null);
    Task<ChatSessionDetailDto?> GetSessionDetailAsync(Guid sessionPublicId, int userId);
    Task<ChatSessionDto> CreateSessionAsync(int userId, CreateSessionRequest request);
    Task<bool> DeleteSessionAsync(Guid sessionPublicId, int userId);
    Task<List<ChatMessage>> GetRecentMessagesAsync(int sessionId, int count = 20);
    Task<ChatMessage> AddMessageAsync(int sessionId, MessageRole role, string content, int? tokenCount = null);
    Task UpdateSessionTitleAsync(int sessionId, string title);
    Task<ChatSessionDto?> TogglePinAsync(Guid sessionPublicId, int userId);
}

public sealed class ChatService : IChatService
{
    private readonly ApplicationDbContext _db;

    public ChatService(ApplicationDbContext db) => _db = db;

    public async Task<List<ChatSessionDto>> GetUserSessionsAsync(int userId, ChatCategory? category = null)
    {
        var query = _db.ChatSessions
            .Include(cs => cs.Messages)
            .Where(cs => cs.UserId == userId);

        if (category.HasValue)
            query = query.Where(cs => cs.Category == category.Value);

        return await query
            .OrderByDescending(cs => cs.IsPinned)
            .ThenByDescending(cs => cs.UpdatedAt)
            .Select(cs => cs.ToDto())
            .ToListAsync();
    }

    public async Task<ChatSessionDetailDto?> GetSessionDetailAsync(Guid sessionPublicId, int userId)
    {
        var session = await _db.ChatSessions
            .Include(cs => cs.Messages.OrderBy(m => m.CreatedAt))
            .FirstOrDefaultAsync(cs => cs.PublicId == sessionPublicId && cs.UserId == userId);

        if (session is null) return null;

        return new ChatSessionDetailDto(
            session.PublicId,
            session.Title,
            session.Category.ToString(),
            session.CreatedAt,
            session.UpdatedAt,
            session.Messages.Select(m => m.ToDto()).ToList()
        );
    }

    public async Task<ChatSessionDto> CreateSessionAsync(int userId, CreateSessionRequest request)
    {
        if (!Enum.TryParse<ChatCategory>(request.Category, true, out var category))
            throw new ArgumentException($"Invalid category: {request.Category}");

        var session = new ChatSession
        {
            UserId = userId,
            Title = request.Title,
            Category = category
        };

        _db.ChatSessions.Add(session);
        await _db.SaveChangesAsync();
        return session.ToDto();
    }

    public async Task<bool> DeleteSessionAsync(Guid sessionPublicId, int userId)
    {
        var session = await _db.ChatSessions
            .FirstOrDefaultAsync(cs => cs.PublicId == sessionPublicId && cs.UserId == userId);
        if (session is null) return false;

        session.IsDeleted = true;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<List<ChatMessage>> GetRecentMessagesAsync(int sessionId, int count = 20)
    {
        return await _db.ChatMessages
            .Where(m => m.ChatSessionId == sessionId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(count)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();
    }

    public async Task<ChatMessage> AddMessageAsync(int sessionId, MessageRole role, string content, int? tokenCount = null)
    {
        var message = new ChatMessage
        {
            ChatSessionId = sessionId,
            Role = role,
            Content = content,
            TokenCount = tokenCount
        };

        _db.ChatMessages.Add(message);

        // Update session timestamp
        var session = await _db.ChatSessions.FindAsync(sessionId);
        if (session is not null)
            session.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return message;
    }

    public async Task UpdateSessionTitleAsync(int sessionId, string title)
    {
        var session = await _db.ChatSessions.FindAsync(sessionId);
        if (session is not null)
        {
            session.Title = title;
            await _db.SaveChangesAsync();
        }
    }

    public async Task<ChatSessionDto?> TogglePinAsync(Guid sessionPublicId, int userId)
    {
        var session = await _db.ChatSessions
            .Include(cs => cs.Messages)
            .FirstOrDefaultAsync(cs => cs.PublicId == sessionPublicId && cs.UserId == userId);
        if (session is null) return null;

        session.IsPinned = !session.IsPinned;
        await _db.SaveChangesAsync();
        return session.ToDto();
    }
}
