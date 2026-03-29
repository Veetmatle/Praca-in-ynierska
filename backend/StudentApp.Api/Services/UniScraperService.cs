using System.Runtime.CompilerServices;
using System.Text.Json;
using StudentApp.Api.Data.Entities;
using Serilog;

namespace StudentApp.Api.Services;

public interface IUniScraperService
{
    IAsyncEnumerable<string> StreamQueryAsync(
        string geminiApiKey, string geminiModel, string query,
        UserConfiguration userConfig, List<ChatMessage> history,
        CancellationToken cancellationToken = default);

    Task<ScraperResult?> FetchScrapedDataAsync(
        string query, UserConfiguration config, CancellationToken ct);

    Task<List<DownloadedFile>> DownloadFilesAsync(
        List<ScrapedFile> files, CancellationToken ct);
}

public sealed class UniScraperService : IUniScraperService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IGeminiService _geminiService;
    private readonly string _scraperBaseUrl;

    public UniScraperService(
        IHttpClientFactory httpClientFactory,
        IGeminiService geminiService,
        IConfiguration config)
    {
        _httpClientFactory = httpClientFactory;
        _geminiService = geminiService;
        _scraperBaseUrl = config["UniScraper:BaseUrl"]?.TrimEnd('/') ?? "http://uniscraper-api:8001";
    }

    public async IAsyncEnumerable<string> StreamQueryAsync(
        string geminiApiKey,
        string geminiModel,
        string query,
        UserConfiguration userConfig,
        List<ChatMessage> history,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string? scrapedContext = null;
        string? fetchError = null;
        try
        {
            var data = await FetchScrapedDataAsync(query, userConfig, cancellationToken);
            scrapedContext = data?.Context;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "UniScraper fetch failed");
            fetchError = "[Nie udało się pobrać danych z uczelni. Spróbuj ponownie później.]";
        }

        if (fetchError is not null) { yield return fetchError; yield break; }

        if (string.IsNullOrWhiteSpace(scrapedContext))
        {
            yield return "Nie znaleziono pasujących informacji na stronie uczelni.";
            yield break;
        }

        var enrichedPrompt = $"""
            Kontekst ze strony uczelni ({userConfig.UniversityName ?? "Uczelnia"}):
            ---
            {scrapedContext}
            ---

            Pytanie studenta: {query}

            Odpowiedz na podstawie powyższego kontekstu. Jeśli kontekst nie zawiera odpowiedzi,
            powiedz o tym. Podaj linki do źródeł jeśli są dostępne.
            """;

        await foreach (var chunk in _geminiService.StreamResponseAsync(
            geminiApiKey, geminiModel, enrichedPrompt, history, cancellationToken))
        {
            yield return chunk;
        }
    }

    /// <summary>
    /// Fetches scraped data including up to 3 best matching file URLs.
    /// </summary>
    public async Task<ScraperResult?> FetchScrapedDataAsync(
        string query, UserConfiguration config, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("UniScraperApi");

        var payload = new
        {
            query,
            university = config.UniversityName ?? "Politechnika Krakowska",
            faculty = config.Faculty,
            field_of_study = config.FieldOfStudy,
            academic_year = config.AcademicYear,
            study_year = config.StudyYear,
            dean_group = config.DeanGroup
        };

        var response = await client.PostAsJsonAsync($"{_scraperBaseUrl}/api/scrape", payload, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);

        var result = new ScraperResult
        {
            Context = json.TryGetProperty("context", out var ctx) ? ctx.GetString() ?? "" : ""
        };

        // Extract up to 3 best matching files
        if (json.TryGetProperty("best_match_files", out var bmf) && bmf.ValueKind == JsonValueKind.Array)
        {
            foreach (var fileEl in bmf.EnumerateArray())
            {
                result.BestMatchFiles.Add(new ScrapedFile
                {
                    Text = fileEl.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "",
                    Url = fileEl.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "",
                });
            }
        }

        return result;
    }

    /// <summary>
    /// Downloads up to 3 files in PARALLEL via Task.WhenAll.
    /// Each file is fetched through UniScraper's /api/fetch-file endpoint.
    /// </summary>
    public async Task<List<DownloadedFile>> DownloadFilesAsync(
        List<ScrapedFile> files, CancellationToken ct)
    {
        if (files.Count == 0) return new List<DownloadedFile>();

        var downloadTasks = files.Select(f => DownloadSingleFileAsync(f.Url, ct)).ToArray();

        DownloadedFile?[] results;
        try
        {
            results = await Task.WhenAll(downloadTasks);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Some file downloads failed during parallel fetch");
            // Collect what we can — individual tasks handle their own errors
            results = downloadTasks.Select(t => t.IsCompletedSuccessfully ? t.Result : null).ToArray();
        }

        return results.Where(r => r is not null && !string.IsNullOrEmpty(r.Base64Data)).ToList()!;
    }

    private async Task<DownloadedFile?> DownloadSingleFileAsync(string fileUrl, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("UniScraperApi");
        var payload = new { url = fileUrl };

        try
        {
            var response = await client.PostAsJsonAsync($"{_scraperBaseUrl}/api/fetch-file", payload, ct);
            if (!response.IsSuccessStatusCode)
            {
                Log.Warning("Failed to download file {Url}: HTTP {Status}", fileUrl, response.StatusCode);
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            return new DownloadedFile
            {
                FileName = result.TryGetProperty("fileName", out var fn) ? fn.GetString() ?? "document" : "document",
                MimeType = result.TryGetProperty("mimeType", out var mt) ? mt.GetString() ?? "application/octet-stream" : "application/octet-stream",
                Base64Data = result.TryGetProperty("base64Data", out var bd) ? bd.GetString() ?? "" : "",
                SizeBytes = result.TryGetProperty("sizeBytes", out var sb) ? sb.GetInt64() : 0,
                SourceUrl = fileUrl,
            };
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to download file {Url}", fileUrl);
            return null;
        }
    }
}

// ── DTOs ──────────────────────────────────────────────────

public class ScraperResult
{
    public string Context { get; set; } = "";
    public List<ScrapedFile> BestMatchFiles { get; set; } = new();
}

public class ScrapedFile
{
    public string Text { get; set; } = "";
    public string Url { get; set; } = "";
}

public class DownloadedFile
{
    public string FileName { get; set; } = "";
    public string MimeType { get; set; } = "";
    public string Base64Data { get; set; } = "";
    public long SizeBytes { get; set; }
    public string SourceUrl { get; set; } = "";
}
