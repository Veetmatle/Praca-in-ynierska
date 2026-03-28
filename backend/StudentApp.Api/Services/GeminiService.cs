using System.Runtime.CompilerServices;
using System.Text.Json;
using StudentApp.Api.Data.Entities;
using StudentApp.Api.Security;
using Serilog;

namespace StudentApp.Api.Services;

/// <summary>
/// Gemini AI service. Accepts user API key (decrypted in-memory), manages conversation context
/// with a sliding window, and returns responses as IAsyncEnumerable for SignalR streaming.
/// Adapted from the original Discord bot GeminiService.
/// </summary>
public interface IGeminiService
{
    IAsyncEnumerable<string> StreamResponseAsync(
        string apiKey,
        string model,
        string prompt,
        List<ChatMessage> history,
        CancellationToken cancellationToken = default);
}

public sealed class GeminiService : IGeminiService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private const int SlidingWindowSize = 20;
    private const string DefaultPromptSuffix = "\nOdpowiadaj zwięźle i precyzyjnie, chyba że instrukcja mówi inaczej.";

    public GeminiService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async IAsyncEnumerable<string> StreamResponseAsync(
        string apiKey,
        string model,
        string prompt,
        List<ChatMessage> history,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("GeminiApi");

        var slidingWindow = history
            .OrderByDescending(m => m.CreatedAt)
            .Take(SlidingWindowSize)
            .Reverse()
            .ToList();

        // Extract system messages → systemInstruction (Gemini API top-level field)
        var systemParts = slidingWindow
            .Where(m => m.Role == MessageRole.System)
            .Select(m => new { text = m.Content })
            .ToList();

        // Build contents — skip System messages, map User/Assistant explicitly
        var mappedMessages = new List<(string Role, string Content)>();
        foreach (var m in slidingWindow.Where(m => m.Role != MessageRole.System))
        {
            if (m.Role == MessageRole.User)
                mappedMessages.Add(("user", m.Content));
            else if (m.Role == MessageRole.Assistant)
                mappedMessages.Add(("model", m.Content));
            else
            {
                Log.Warning("GeminiService: unknown MessageRole {Role} on message {Id} — skipping", m.Role, m.Id);
            }
        }

        // Gemini requires contents to start with a "user" turn
        while (mappedMessages.Count > 0 && mappedMessages[0].Role == "model")
        {
            Log.Warning("GeminiService: dropping leading 'model' message to satisfy Gemini alternation requirement");
            mappedMessages.RemoveAt(0);
        }

        var contextMessages = mappedMessages
            .Select(m => new { role = m.Role, parts = new[] { new { text = m.Content } } })
            .Cast<object>()
            .ToList();

        // Add new prompt
        contextMessages.Add(new
        {
            role = "user",
            parts = new[] { new { text = prompt + DefaultPromptSuffix } }
        });

        object requestBody;
        if (systemParts.Count > 0)
        {
            requestBody = new
            {
                systemInstruction = new { parts = systemParts },
                contents = contextMessages,
                generationConfig = new { maxOutputTokens = 2048, temperature = 0.7 }
            };
        }
        else
        {
            requestBody = new
            {
                contents = contextMessages,
                generationConfig = new { maxOutputTokens = 2048, temperature = 0.7 }
            };
        }

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:streamGenerateContent?alt=sse&key={apiKey}";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = JsonContent.Create(requestBody);

        // ── Send request (yield not allowed in try/catch, use variable) ──
        HttpResponseMessage response;
        string? sendError = null;
        try
        {
            response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException ex)
        {
            Log.Error(ex, "Gemini API request failed");
            sendError = $"[Błąd API Gemini: {ex.StatusCode}]";
            response = null!;
        }

        if (sendError is not null)
        {
            yield return sendError;
            yield break;
        }

        // ── Stream response (read chunks, yield outside try/catch) ──
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        const int maxConsecutiveErrors = 5;
        int consecutiveErrors = 0;
        string? streamError = null;

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = await reader.ReadLineAsync(cancellationToken);
            }
            catch (IOException ex)
            {
                Log.Error(ex, "Gemini stream: connection lost while reading response stream");
                streamError = "[Strumień odpowiedzi przerwany — utracono połączenie z API Gemini]";
                break;
            }
            catch (HttpRequestException ex)
            {
                Log.Error(ex, "Gemini stream: HTTP error while reading response stream");
                streamError = "[Strumień odpowiedzi przerwany — problem z API Gemini]";
                break;
            }

            if (string.IsNullOrEmpty(line) || !line.StartsWith("data: "))
                continue;

            var json = line[6..];
            if (json == "[DONE]") break;

            string? text = null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                text = doc.RootElement
                    .GetProperty("candidates")[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text")
                    .GetString();

                consecutiveErrors = 0;
            }
            catch (Exception ex)
            {
                consecutiveErrors++;
                Log.Debug("Gemini stream: malformed chunk skipped. Error: {Error}, Raw: {Line}", ex.Message, json);

                if (consecutiveErrors >= maxConsecutiveErrors)
                {
                    Log.Warning("Gemini stream: {Count} consecutive unparseable chunks. Aborting.", consecutiveErrors);
                    streamError = "[Strumień odpowiedzi przerwany — problem z API Gemini]";
                    break;
                }

                continue;
            }

            if (!string.IsNullOrEmpty(text))
                yield return text;
        }

        if (streamError is not null)
            yield return streamError;
    }
}
