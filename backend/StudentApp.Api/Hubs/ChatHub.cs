using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using StudentApp.Api.Data;
using StudentApp.Api.Data.Entities;
using StudentApp.Api.Models.DTOs;
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

        // ── Deliver agent output files to frontend ──
        if (session.Category == ChatCategory.OpenClaw)
        {
            var taskId = _openClawService.LastTaskId;
            if (!string.IsNullOrEmpty(taskId))
            {
                _ = DeliverAgentFilesAsync(taskId, Context.ConnectionAborted);
            }
        }

        // Auto-title on first message
        if (session.Title == "Nowa rozmowa" && content.Length > 3)
        {
            var title = content.Length > 50 ? content[..50] + "..." : content;
            await _chatService.UpdateSessionTitleAsync(session.Id, title);
            await Clients.Caller.SendAsync("SessionTitleUpdated", new { sessionPublicId, title });
        }
    }

    public async Task SendMessageWithAttachments(
        string sessionPublicId, string content, List<AttachmentPayload> attachments)
    {
        var connectionId = Context.ConnectionId;
        var now = DateTime.UtcNow;
        if (_lastMessageTime.TryGetValue(connectionId, out var lastTime)
            && (now - lastTime) < _minMessageInterval)
        {
            await Clients.Caller.SendAsync("Error", "Zbyt wiele wiadomości.");
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

        if (session.Category != ChatCategory.Gemini)
        {
            await Clients.Caller.SendAsync("Error", "Załączniki obsługiwane tylko w Gemini.");
            return;
        }

        var fileNames = string.Join(", ", attachments.Select(a => a.FileName));
        var displayContent = string.IsNullOrWhiteSpace(content)
            ? $"[Załączniki: {fileNames}]"
            : $"{content}\n📎 {fileNames}";

        var userMsg = await _chatService.AddMessageAsync(session.Id, MessageRole.User, displayContent);
        await Clients.Caller.SendAsync("MessageSaved", new { id = userMsg.Id, role = "User", content = displayContent });

        var history = await _chatService.GetRecentMessagesAsync(session.Id, 20);
        var fullResponse = new StringBuilder();

        string? apiKey = null;
        string? initError = null;
        try
        {
            apiKey = await _configService.GetDecryptedGeminiKeyAsync(userId.Value);
        }
        catch (Exception ex)
        {
            initError = $"[Błąd deszyfrowania klucza: {ex.Message}]";
        }

        if (apiKey is null && initError is null)
            initError = "Klucz Gemini API nie jest skonfigurowany. Przejdź do Ustawień.";

        if (initError is not null)
        {
            await Clients.Caller.SendAsync("StreamEnd", initError);
            return;
        }

        var cfg = await _configService.GetRawConfigAsync(userId.Value);
        var geminiAttachments = attachments.Select(a => new GeminiAttachment
        {
            FileName = a.FileName,
            MimeType = a.MimeType,
            Base64Data = a.Base64Data
        }).ToList();

        try
        {
            var prompt = string.IsNullOrWhiteSpace(content) ? "Przeanalizuj załączone pliki." : content;
            await foreach (var chunk in _geminiService.StreamResponseAsync(
                apiKey!, cfg?.GeminiModel ?? "gemini-2.5-flash",
                prompt, history, geminiAttachments, Context.ConnectionAborted))
            {
                fullResponse.Append(chunk);
                await Clients.Caller.SendAsync("StreamChunk", chunk);
            }
            await Clients.Caller.SendAsync("StreamEnd", (string?)null);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            var errorMsg = $"[Błąd: {ex.Message}]";
            fullResponse.Append(errorMsg);
            await Clients.Caller.SendAsync("StreamEnd", errorMsg);
        }

        if (fullResponse.Length > 0)
        {
            var assistantMsg = await _chatService.AddMessageAsync(
                session.Id, MessageRole.Assistant, fullResponse.ToString());
            await Clients.Caller.SendAsync("MessageSaved", new
            { id = assistantMsg.Id, role = "Assistant", content = fullResponse.ToString() });
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
        return _openClawService.StreamAgentTaskAsync(
            apiKey, cfg?.AnthropicModel ?? "claude-sonnet-4-20250514", prompt, history);
    }

    private async Task<IAsyncEnumerable<string>> StreamUniScraperAsync(
        int userId, ChatSession session, string prompt, List<ChatMessage> history)
    {
        var apiKey = await _configService.GetDecryptedGeminiKeyAsync(userId)
            ?? throw new InvalidOperationException("Klucz Gemini API nie jest skonfigurowany. Przejdź do Ustawień.");
        var cfg = await _configService.GetRawConfigAsync(userId);
        if (cfg is null)
            throw new InvalidOperationException("Konfiguracja uczelni nie jest ustawiona. Przejdź do Ustawień.");

        // Pre-fetch scraper data to identify files to download
        ScraperResult? scraperData = null;
        try
        {
            scraperData = await _uniScraperService.FetchScrapedDataAsync(prompt, cfg, Context.ConnectionAborted);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Pre-fetch scraper data failed — falling back to text-only");
        }

        // Fire-and-forget: download up to 3 files in parallel, send each as FileAvailable
        if (scraperData?.BestMatchFiles.Count > 0)
        {
            _ = DownloadAndSendFilesAsync(scraperData.BestMatchFiles, Context.ConnectionAborted);
        }

        return _uniScraperService.StreamQueryAsync(apiKey, cfg.GeminiModel, prompt, cfg, history);
    }

    /// <summary>
    /// Downloads up to 3 files in parallel and sends each as a separate FileAvailable event.
    /// Runs in background — does not block text streaming.
    /// </summary>
    private async Task DownloadAndSendFilesAsync(List<ScrapedFile> files, CancellationToken ct)
    {
        try
        {
            // Small delay to let text streaming start first
            await Task.Delay(500, ct);

            var downloaded = await _uniScraperService.DownloadFilesAsync(files, ct);

            foreach (var file in downloaded)
            {
                if (string.IsNullOrEmpty(file.Base64Data)) continue;

                await Clients.Caller.SendAsync("FileAvailable", new
                {
                    fileName = file.FileName,
                    mimeType = file.MimeType,
                    base64Data = file.Base64Data,
                    sizeBytes = file.SizeBytes,
                    sourceUrl = file.SourceUrl,
                }, ct);

                Log.Information("Sent file {FileName} ({Size}KB) to client",
                    file.FileName, file.SizeBytes / 1024);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to download/send files to client");
        }
    }

    /// <summary>
    /// Fetches output files from completed OpenClaw agent task and sends each
    /// to the frontend as a FileAvailable event — same UX as UniScraper files.
    /// Replicates the Discord bot's file delivery pattern.
    /// </summary>
    private async Task DeliverAgentFilesAsync(string taskId, CancellationToken ct)
    {
        try
        {
            // Small delay — let the final text message render first
            await Task.Delay(800, ct);

            var files = await _openClawService.GetTaskFilesAsync(taskId, ct);
            if (files.Count == 0) return;

            Log.Information("Delivering {Count} agent file(s) from task {TaskId}", files.Count, taskId);

            foreach (var file in files)
            {
                if (string.IsNullOrEmpty(file.Base64Data)) continue;

                await Clients.Caller.SendAsync("FileAvailable", new
                {
                    fileName = file.FileName,
                    mimeType = file.MimeType,
                    base64Data = file.Base64Data,
                    sizeBytes = file.SizeBytes,
                    sourceUrl = file.SourceUrl,
                }, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to deliver agent files for task {TaskId}", taskId);
        }
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
