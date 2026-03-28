using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using StudentApp.Api.Data;
using StudentApp.Api.Data.Entities;
using StudentApp.Api.Services;
using Serilog;

namespace StudentApp.Api.Hubs;

/// <summary>
/// SignalR hub for real-time chat with LLM services.
/// Streams token-by-token responses from Gemini, OpenClaw, or UniScraper.
/// </summary>
[Authorize]
public class ChatHub : Hub
{
    // Per-connection message throttle — max 1 message per 2 seconds
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime>
        _lastMessageTime = new();
    private static readonly TimeSpan _minMessageInterval = TimeSpan.FromSeconds(2);

    private readonly IChatService _chatService;
    private readonly IConfigurationService _configService;
    private readonly IGeminiService _geminiService;
    private readonly IOpenClawService _openClawService;
    private readonly IUniScraperService _uniScraperService;
    private readonly ApplicationDbContext _db;

    public ChatHub(
        IChatService chatService,
        IConfigurationService configService,
        IGeminiService geminiService,
        IOpenClawService openClawService,
        IUniScraperService uniScraperService,
        ApplicationDbContext db)
    {
        _chatService = chatService;
        _configService = configService;
        _geminiService = geminiService;
        _openClawService = openClawService;
        _uniScraperService = uniScraperService;
        _db = db;
    }

    /// <summary>
    /// Sends a message to an existing chat session and streams the AI response back.
    /// </summary>
    public async Task SendMessage(string sessionPublicId, string content)
    {
        // Throttle: reject rapid-fire messages
        var connectionId = Context.ConnectionId;
        var now = DateTime.UtcNow;
        if (_lastMessageTime.TryGetValue(connectionId, out var lastTime)
            && (now - lastTime) < _minMessageInterval)
        {
            await Clients.Caller.SendAsync("Error",
                "Zbyt wiele wiadomości. Poczekaj chwilę przed wysłaniem kolejnej.");
            return;
        }
        _lastMessageTime[connectionId] = now;

        var userId = GetUserId();
        if (userId is null) return;

        var session = await _db.ChatSessions
            .FirstOrDefaultAsync(s => s.PublicId == Guid.Parse(sessionPublicId) && s.UserId == userId.Value);

        if (session is null)
        {
            await Clients.Caller.SendAsync("Error", "Sesja nie znaleziona.");
            return;
        }

        // Save user message
        var userMsg = await _chatService.AddMessageAsync(session.Id, MessageRole.User, content);
        await Clients.Caller.SendAsync("MessageSaved", new { id = userMsg.Id, role = "User", content });

        // Get history for context window
        var history = await _chatService.GetRecentMessagesAsync(session.Id, 20);

        // Stream AI response
        var fullResponse = new StringBuilder();

        try
        {
            var stream = session.Category switch
            {
                ChatCategory.Gemini => await StreamGeminiAsync(userId.Value, session, content, history),
                ChatCategory.OpenClaw => await StreamOpenClawAsync(userId.Value, session, content, history),
                ChatCategory.UniScraper => await StreamUniScraperAsync(userId.Value, session, content, history),
                _ => throw new InvalidOperationException($"Unknown category: {session.Category}")
            };

            await foreach (var chunk in stream.WithCancellation(Context.ConnectionAborted))
            {
                fullResponse.Append(chunk);
                await Clients.Caller.SendAsync("StreamChunk", chunk);
            }

            await Clients.Caller.SendAsync("StreamEnd", (string?)null);
        }
        catch (OperationCanceledException)
        {
            Log.Information("Stream cancelled for session {SessionId}", sessionPublicId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error streaming response for session {SessionId}", sessionPublicId);
            var errorMsg = $"[Wystąpił błąd: {ex.Message}]";
            fullResponse.Append(errorMsg);
            await Clients.Caller.SendAsync("StreamEnd", errorMsg);
        }

        // Save assistant response
        if (fullResponse.Length > 0)
        {
            var assistantMsg = await _chatService.AddMessageAsync(
                session.Id, MessageRole.Assistant, fullResponse.ToString());
            await Clients.Caller.SendAsync("MessageSaved", new
            {
                id = assistantMsg.Id,
                role = "Assistant",
                content = fullResponse.ToString()
            });
        }

        // Auto-title on first message
        if (session.Title == "Nowa rozmowa" && content.Length > 3)
        {
            var title = content.Length > 50 ? content[..50] + "..." : content;
            await _chatService.UpdateSessionTitleAsync(session.Id, title);
            await Clients.Caller.SendAsync("SessionTitleUpdated", new { sessionPublicId, title });
        }
    }

    private async Task<IAsyncEnumerable<string>> StreamGeminiAsync(
        int userId, ChatSession session, string prompt, List<ChatMessage> history)
    {
        var apiKey = await _configService.GetDecryptedGeminiKeyAsync(userId)
            ?? throw new InvalidOperationException("Klucz Gemini API nie jest skonfigurowany. Przejdź do Ustawień.");
        var cfg = await _configService.GetRawConfigAsync(userId);
        return _geminiService.StreamResponseAsync(apiKey, cfg?.GeminiModel ?? "gemini-2.5-flash", prompt, history);
    }

    private async Task<IAsyncEnumerable<string>> StreamOpenClawAsync(
        int userId, ChatSession session, string prompt, List<ChatMessage> history)
    {
        var apiKey = await _configService.GetDecryptedAnthropicKeyAsync(userId)
            ?? throw new InvalidOperationException("Klucz Anthropic API nie jest skonfigurowany. Przejdź do Ustawień.");
        var cfg = await _configService.GetRawConfigAsync(userId);
        return _openClawService.StreamChatAsync(apiKey, cfg?.AnthropicModel ?? "claude-sonnet-4-20250514", prompt, history);
    }

    private async Task<IAsyncEnumerable<string>> StreamUniScraperAsync(
        int userId, ChatSession session, string prompt, List<ChatMessage> history)
    {
        var apiKey = await _configService.GetDecryptedGeminiKeyAsync(userId)
            ?? throw new InvalidOperationException("Klucz Gemini API nie jest skonfigurowany. Przejdź do Ustawień.");
        var cfg = await _configService.GetRawConfigAsync(userId);
        if (cfg is null)
            throw new InvalidOperationException("Konfiguracja uczelni nie jest ustawiona. Przejdź do Ustawień.");
        return _uniScraperService.StreamQueryAsync(apiKey, cfg.GeminiModel, prompt, cfg, history);
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _lastMessageTime.TryRemove(Context.ConnectionId, out _);
        return base.OnDisconnectedAsync(exception);
    }

    private int? GetUserId()
    {
        var claim = Context.User?.FindFirst(ClaimTypes.NameIdentifier);
        return claim is not null && int.TryParse(claim.Value, out var id) ? id : null;
    }
}
