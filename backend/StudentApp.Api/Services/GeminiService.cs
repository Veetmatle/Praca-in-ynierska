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

        // Build sliding window context from recent messages
        var contextMessages = history
            .OrderByDescending(m => m.CreatedAt)
            .Take(SlidingWindowSize)
            .Reverse()
            .Select(m => new
            {
                role = m.Role == MessageRole.User ? "user" : "model",
                parts = new[] { new { text = m.Content } }
            })
            .ToList();

        // Add new prompt
        contextMessages.Add(new
        {
            role = "user",
            parts = new[] { new { text = prompt + DefaultPromptSuffix } }
        });

        var requestBody = new
        {
            contents = contextMessages,
            generationConfig = new
            {
                maxOutputTokens = 2048,
                temperature = 0.7
            }
        };

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:streamGenerateContent?alt=sse&key={apiKey}";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = JsonContent.Create(requestBody);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException ex)
        {
            Log.Error(ex, "Gemini API request failed");
            yield return $"[Błąd API Gemini: {ex.StatusCode}]";
            yield break;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
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
            }
            catch
            {
                // Skip malformed chunks
            }

            if (!string.IsNullOrEmpty(text))
                yield return text;
        }
    }
}
