using System.IO;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Serilog;

namespace StudentApp.Api.Services;

/// <summary>
/// HTTP client for the OpenClaw agent microservice.
/// OpenClaw = ALWAYS agent mode. Uses /tasks endpoint with full tool_use capability.
/// API key is passed per-request (from user's encrypted config), not from server .env.
/// </summary>
public interface IOpenClawService
{
    /// <summary>
    /// Submits a task to OpenClaw agent, polls for completion,
    /// and streams status updates + final response.
    /// Always uses /tasks endpoint with full tool_use capability.
    /// </summary>
    IAsyncEnumerable<string> StreamAgentTaskAsync(
        string anthropicApiKey,
        string model,
        string prompt,
        List<Data.Entities.ChatMessage> history,
        CancellationToken cancellationToken = default);

    /// <summary>Last submitted agent task ID — read by ChatHub to fetch output files.</summary>
    string? LastTaskId { get; }

    Task<List<DownloadedFile>> GetTaskFilesAsync(string taskId, CancellationToken ct);
}

public sealed class OpenClawService : IOpenClawService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _baseUrl;
    private readonly SemaphoreSlim _rateLimiter = new(1, 1);
    private DateTime _lastRequestTime = DateTime.MinValue;

    /// <summary>
    /// Last submitted agent task ID — read by ChatHub to fetch output files.
    /// Thread-safe: each SignalR connection runs on its own scope.
    /// </summary>
    public string? LastTaskId { get; private set; }

    public OpenClawService(IHttpClientFactory httpClientFactory, IConfiguration config)
    {
        _httpClientFactory = httpClientFactory;
        _baseUrl = config["OpenClaw:BaseUrl"]?.TrimEnd('/') ?? "http://openclaw-api:8000";
    }

    public async IAsyncEnumerable<string> StreamAgentTaskAsync(
        string anthropicApiKey,
        string model,
        string prompt,
        List<Data.Entities.ChatMessage> history,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await ThrottleAsync(cancellationToken);
        var client = _httpClientFactory.CreateClient("OpenClawApi");
        var taskId = Guid.NewGuid().ToString("N")[..8];
        LastTaskId = taskId;

        // ── Build prompt with recent history context ──
        var historyContext = "";
        if (history.Count > 0)
        {
            var recent = history
                .OrderByDescending(m => m.CreatedAt)
                .Take(5)
                .Reverse()
                .Select(m => $"{(m.Role == Data.Entities.MessageRole.User ? "Student" : "Asystent")}: {m.Content}");
            historyContext = "\n\nKontekst ostatnich wiadomości:\n" + string.Join("\n", recent) + "\n\n";
        }

        var fullPrompt = historyContext + "Aktualne zadanie: " + prompt;

        // ── Submit task ──
        var submitPayload = new
        {
            prompt = fullPrompt,
            task_id = taskId,
            anthropic_api_key = anthropicApiKey,
            model,
            max_iterations = 15,
            timeout_seconds = 360
        };

        string? submitError = null;
        try
        {
            var submitResponse = await client.PostAsJsonAsync(
                $"{_baseUrl}/tasks", submitPayload, cancellationToken);
            submitResponse.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to submit agent task {TaskId}", taskId);
            submitError = $"[Błąd wysyłania zadania do agenta: {ex.Message}]";
        }

        if (submitError is not null)
        {
            yield return submitError;
            yield break;
        }

        yield return "🔧 **Agent pracuje nad zadaniem...**\n\n";
        yield return "_Claude analizuje polecenie, pisze kod i uruchamia narzędzia. ";
        yield return "To może potrwać od kilku sekund do kilku minut._\n\n";

        // ── Poll for completion (max 6 minutes) ──
        var deadline = DateTime.UtcNow.AddMinutes(6);
        var lastStatus = "";
        JsonElement? finalResult = null;

        while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(2500, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Student disconnected — try to cancel the task
                try { await client.DeleteAsync($"{_baseUrl}/tasks/{taskId}"); } catch { }
                yield break;
            }

            JsonElement status;
            try
            {
                var statusResponse = await client.GetAsync(
                    $"{_baseUrl}/tasks/{taskId}", cancellationToken);
                if (!statusResponse.IsSuccessStatusCode) continue;
                status = await statusResponse.Content
                    .ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
            }
            catch (OperationCanceledException) { yield break; }
            catch { continue; }

            var statusStr = status.GetProperty("Status").GetString() ?? "";

            // Stream status updates to student
            if (statusStr != lastStatus)
            {
                lastStatus = statusStr;
                if (statusStr == "running")
                    yield return "⚙️ Agent uruchomił narzędzia...\n";
            }

            if (statusStr is "running" or "queued") continue;

            if (statusStr == "cancelled")
            {
                yield return "\n⚠️ Zadanie zostało anulowane.\n";
                yield break;
            }

            if (statusStr == "failed")
            {
                var errorMsg = status.TryGetProperty("Error", out var err)
                    ? err.GetString() : "Nieznany błąd";
                yield return $"\n❌ **Agent nie ukończył zadania:** {errorMsg}\n";
                yield break;
            }

            // Completed!
            finalResult = status;
            break;
        }

        if (finalResult is null)
        {
            yield return "\n⏱️ **Zadanie przekroczyło limit czasu (6 min).** Spróbuj uprościć polecenie.\n";
            try { await client.DeleteAsync($"{_baseUrl}/tasks/{taskId}"); } catch { }
            yield break;
        }

        // ── Stream final response ──
        yield return "\n---\n\n";

        if (finalResult.Value.TryGetProperty("DirectResponse", out var dr) && dr.ValueKind == JsonValueKind.String)
        {
            var directResponse = dr.GetString();
            if (!string.IsNullOrWhiteSpace(directResponse))
            {
                // Stream in chunks for progressive rendering
                const int chunkSize = 80;
                for (int i = 0; i < directResponse.Length; i += chunkSize)
                {
                    var chunk = directResponse.Substring(i, Math.Min(chunkSize, directResponse.Length - i));
                    yield return chunk;
                    try { await Task.Delay(15, cancellationToken); } catch { yield break; }
                }
            }
        }

        // ── List output files ──
        string? fileError = null;
        List<JsonElement>? files = null;
        try
        {
            var filesResponse = await client.GetAsync(
                $"{_baseUrl}/tasks/{taskId}/files", cancellationToken);
            if (filesResponse.IsSuccessStatusCode)
            {
                var filesJson = await filesResponse.Content
                    .ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
                if (filesJson.TryGetProperty("Files", out var filesArr))
                {
                    files = filesArr.EnumerateArray().ToList();
                }
            }
        }
        catch (Exception ex)
        {
            fileError = $"\n⚠️ Nie udało się pobrać plików: {ex.Message}\n";
        }

        if (fileError is not null)
            yield return fileError;

        if (files is not null && files.Count > 0)
        {
            yield return "\n\n---\n📁 **Wygenerowane pliki:**\n\n";
            foreach (var f in files)
            {
                var fileName = f.GetProperty("FileName").GetString() ?? "plik";
                var sizeBytes = f.TryGetProperty("SizeBytes", out var sb) ? sb.GetInt64() : 0;
                var tooLarge = f.TryGetProperty("TooLarge", out var tl) && tl.GetBoolean();
                var sizeDisplay = sizeBytes > 1024 * 1024
                    ? $"{sizeBytes / 1024.0 / 1024.0:F1} MB"
                    : $"{sizeBytes / 1024.0:F0} KB";

                if (tooLarge)
                    yield return $"- ⚠️ `{fileName}` — za duży ({sizeDisplay})\n";
                else
                    yield return $"- 📄 `{fileName}` ({sizeDisplay})\n";
            }
        }

        yield return "\n✅ **Zadanie zakończone.**\n";
    }

    public async Task<List<DownloadedFile>> GetTaskFilesAsync(string taskId, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("OpenClawApi");
        var files = new List<DownloadedFile>();

        try
        {
            var response = await client.GetAsync($"{_baseUrl}/tasks/{taskId}/files", ct);
            if (!response.IsSuccessStatusCode) return files;

            var result = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            if (!result.TryGetProperty("Files", out var filesArr)) return files;

            foreach (var f in filesArr.EnumerateArray())
            {
                var tooLarge = f.TryGetProperty("TooLarge", out var tl) && tl.GetBoolean();
                if (tooLarge) continue;

                var base64 = f.TryGetProperty("ContentBase64", out var cb) ? cb.GetString() ?? "" : "";
                if (string.IsNullOrEmpty(base64)) continue;

                var fileName = f.GetProperty("FileName").GetString() ?? "file";
                files.Add(new DownloadedFile
                {
                    FileName = fileName,
                    MimeType = GuessMimeType(fileName),
                    Base64Data = base64,
                    SizeBytes = f.TryGetProperty("SizeBytes", out var sb) ? sb.GetInt64() : 0,
                    SourceUrl = $"agent://{taskId}/{fileName}",
                });
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to fetch agent files for task {TaskId}", taskId);
        }

        return files;
    }

    private static string GuessMimeType(string fileName)
    {
        var ext = System.IO.Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".svg" => "image/svg+xml",
            ".csv" => "text/csv",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".py" => "text/x-python",
            ".js" => "text/javascript",
            ".html" => "text/html",
            ".json" => "application/json",
            ".zip" => "application/zip",
            ".txt" => "text/plain",
            _ => "application/octet-stream",
        };
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
