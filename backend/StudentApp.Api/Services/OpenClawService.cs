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

    Task<AgentTaskResult> SubmitAgentTaskAsync(
        string anthropicApiKey, string model, string prompt,
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

        // ── Send request ──
        HttpResponseMessage response;
        string? sendError = null;
        try
        {
            response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException ex)
        {
            Log.Error(ex, "OpenClaw API request failed");
            sendError = $"[Błąd OpenClaw: {ex.StatusCode}]";
            response = null!;
        }

        if (sendError is not null)
        {
            yield return sendError;
            yield break;
        }

        // ── Stream response ──
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        int consecutiveErrors = 0;
        const int maxConsecutiveErrors = 5;
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
                Log.Warning("OpenClaw stream: connection lost: {Error}", ex.Message);
                streamError = "[Połączenie z OpenClaw zostało przerwane]";
                break;
            }
            catch (HttpRequestException ex)
            {
                Log.Warning("OpenClaw stream: HTTP error: {Error}", ex.Message);
                streamError = "[Błąd HTTP w strumieniu OpenClaw]";
                break;
            }

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

                    consecutiveErrors = 0;
                }
                catch (Exception ex)
                {
                    consecutiveErrors++;
                    Log.Debug("OpenClaw stream: malformed chunk. Error: {Error}, Raw: {Data}", ex.Message, data);

                    if (consecutiveErrors >= maxConsecutiveErrors)
                    {
                        Log.Warning("OpenClaw stream: {Count} consecutive parse errors, aborting", consecutiveErrors);
                        streamError = "[Strumień przerwany — problem z odpowiedzią OpenClaw]";
                        break;
                    }
                    continue;
                }

                if (!string.IsNullOrEmpty(text))
                    yield return text;
            }
        }

        if (streamError is not null)
            yield return streamError;
    }

    public async Task<AgentTaskResult> SubmitAgentTaskAsync(
        string anthropicApiKey, string model, string prompt,
        CancellationToken cancellationToken = default)
    {
        await ThrottleAsync(cancellationToken);
        var client = _httpClientFactory.CreateClient("OpenClawApi");
        var taskId = Guid.NewGuid().ToString("N")[..8];

        // Submit task
        var submitPayload = new
        {
            prompt,
            task_id = taskId,
            anthropic_api_key = anthropicApiKey,
            model,
            max_iterations = 10,
            timeout_seconds = 300
        };

        var submitResponse = await client.PostAsJsonAsync(
            $"{_baseUrl}/tasks", submitPayload, cancellationToken);
        submitResponse.EnsureSuccessStatusCode();

        // Poll for completion (max 5 minutes)
        var deadline = DateTime.UtcNow.AddMinutes(5);
        while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(2000, cancellationToken);

            var statusResponse = await client.GetAsync(
                $"{_baseUrl}/tasks/{taskId}", cancellationToken);
            if (!statusResponse.IsSuccessStatusCode) continue;

            var status = await statusResponse.Content
                .ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);

            var statusStr = status.GetProperty("Status").GetString();
            if (statusStr is "running" or "queued") continue;

            if (statusStr == "failed")
            {
                return new AgentTaskResult
                {
                    Success = false,
                    Error = status.TryGetProperty("Error", out var err) ? err.GetString() : "Task failed"
                };
            }

            // Completed — get response and files
            var result = new AgentTaskResult
            {
                Success = true,
                DirectResponse = status.TryGetProperty("DirectResponse", out var dr) ? dr.GetString() : null
            };

            // Fetch files
            var filesResponse = await client.GetAsync(
                $"{_baseUrl}/tasks/{taskId}/files", cancellationToken);
            if (filesResponse.IsSuccessStatusCode)
            {
                var filesJson = await filesResponse.Content
                    .ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);

                if (filesJson.TryGetProperty("Files", out var filesArr))
                {
                    foreach (var f in filesArr.EnumerateArray())
                    {
                        result.OutputFiles.Add(new AgentOutputFile
                        {
                            FileName = f.GetProperty("FileName").GetString() ?? "",
                            ContentBase64 = f.TryGetProperty("ContentBase64", out var cb) ? cb.GetString() ?? "" : "",
                            SizeBytes = f.TryGetProperty("SizeBytes", out var sb) ? sb.GetInt64() : 0,
                            TooLarge = f.TryGetProperty("TooLarge", out var tl) && tl.GetBoolean()
                        });
                    }
                }
            }

            return result;
        }

        return new AgentTaskResult { Success = false, Error = "Task timed out (5 min)" };
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

public class AgentTaskResult
{
    public bool Success { get; set; }
    public string? DirectResponse { get; set; }
    public string? Error { get; set; }
    public List<AgentOutputFile> OutputFiles { get; set; } = new();
}

public class AgentOutputFile
{
    public string FileName { get; set; } = "";
    public string ContentBase64 { get; set; } = "";
    public long SizeBytes { get; set; }
    public bool TooLarge { get; set; }
}
