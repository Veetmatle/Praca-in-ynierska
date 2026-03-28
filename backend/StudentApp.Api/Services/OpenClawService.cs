using System.IO;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;

namespace StudentApp.Api.Services;

/// <summary>
/// HTTP client for the OpenClaw agent microservice.
/// Adapted from the Discord bot's OpenClawAgentClient — now streams SSE responses back.
/// API key is passed per-request (from user's encrypted config), not from server .env.
/// </summary>
public interface IOpenClawService
{
    IAsyncEnumerable<string> StreamChatAsync(
        string anthropicApiKey,
        string model,
        string prompt,
        List<Data.Entities.ChatMessage> history,
        CancellationToken cancellationToken = default);
}

public sealed class OpenClawService : IOpenClawService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _baseUrl;
    private readonly SemaphoreSlim _rateLimiter = new(1, 1);
    private DateTime _lastRequestTime = DateTime.MinValue;

    public OpenClawService(IHttpClientFactory httpClientFactory, IConfiguration config)
    {
        _httpClientFactory = httpClientFactory;
        _baseUrl = config["OpenClaw:BaseUrl"]?.TrimEnd('/') ?? "http://openclaw-api:8000";
    }

    public async IAsyncEnumerable<string> StreamChatAsync(
        string anthropicApiKey,
        string model,
        string prompt,
        List<Data.Entities.ChatMessage> history,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await ThrottleAsync(cancellationToken);
        var client = _httpClientFactory.CreateClient("OpenClawApi");

        var historyPayload = history
            .OrderByDescending(m => m.CreatedAt)
            .Take(10)
            .Reverse()
            .Select(m => new { role = m.Role.ToString().ToLower(), content = m.Content })
            .ToList();

        var payload = new
        {
            prompt,
            history = historyPayload,
            anthropic_api_key = anthropicApiKey,
            model,
            stream = true
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/chat");
        request.Content = JsonContent.Create(payload);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException ex)
        {
            Log.Error(ex, "OpenClaw API request failed");
            yield return $"[Błąd OpenClaw: {ex.StatusCode}]";
            yield break;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        int consecutiveErrors = 0;
        const int maxConsecutiveErrors = 5;

        try
        {
        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrEmpty(line)) continue;

            if (line.StartsWith("data: "))
            {
                var data = line[6..];
                if (data == "[DONE]") break;

                string? text = null;
                try
                {
                    using var doc = JsonDocument.Parse(data);
                    if (doc.RootElement.TryGetProperty("text", out var textEl))
                        text = textEl.GetString();
                    else if (doc.RootElement.TryGetProperty("content", out var contentEl))
                        text = contentEl.GetString();
                }
                catch (Exception ex)
                {
                    consecutiveErrors++;
                    Log.Debug("OpenClaw stream: malformed chunk skipped. Error: {Error}, Raw: {Data}",
                        ex.Message, data);

                    if (consecutiveErrors >= maxConsecutiveErrors)
                    {
                        Log.Warning("OpenClaw stream: {Count} consecutive parse errors, aborting",
                            consecutiveErrors);
                        yield return "\n\n[Strumień przerwany — problem z odpowiedzią OpenClaw]";
                        yield break;
                    }
                    continue;
                }

                if (!string.IsNullOrEmpty(text))
                {
                    consecutiveErrors = 0;
                    yield return text;
                }
            }
        }
        }
        catch (IOException ex)
        {
            Log.Warning("OpenClaw stream: connection lost: {Error}", ex.Message);
            yield return "\n\n[Połączenie z OpenClaw zostało przerwane]";
        }
        catch (HttpRequestException ex)
        {
            Log.Warning("OpenClaw stream: HTTP error during streaming: {Error}", ex.Message);
            yield return "\n\n[Błąd HTTP w strumieniu OpenClaw]";
        }
    }

    private async Task ThrottleAsync(CancellationToken ct)
    {
        await _rateLimiter.WaitAsync(ct);
        try
        {
            var elapsed = (DateTime.UtcNow - _lastRequestTime).TotalMilliseconds;
            if (elapsed < 500)
                await Task.Delay((int)(500 - elapsed), ct);
            _lastRequestTime = DateTime.UtcNow;
        }
        finally
        {
            _rateLimiter.Release();
        }
    }
}
