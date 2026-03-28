using System.Runtime.CompilerServices;
using System.Text.Json;
using StudentApp.Api.Data.Entities;
using Serilog;

namespace StudentApp.Api.Services;

/// <summary>
/// Client for the UniScraper microservice. Sends parameterized queries 
/// (faculty, field of study, year, group) and streams back responses via Gemini.
/// </summary>
public interface IUniScraperService
{
    IAsyncEnumerable<string> StreamQueryAsync(
        string geminiApiKey,
        string geminiModel,
        string query,
        UserConfiguration userConfig,
        List<ChatMessage> history,
        CancellationToken cancellationToken = default);
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
        // Step 1: Fetch context from UniScraper
        string? scrapedContext = null;
        string? fetchError = null;
        try
        {
            scrapedContext = await FetchScrapedContextAsync(query, userConfig, cancellationToken);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "UniScraper fetch failed");
            fetchError = "[Nie udało się pobrać danych z uczelni. Spróbuj ponownie później.]";
        }

        if (fetchError is not null)
        {
            yield return fetchError;
            yield break;
        }

        if (string.IsNullOrWhiteSpace(scrapedContext))
        {
            yield return "Nie znaleziono pasujących informacji na stronie uczelni.";
            yield break;
        }

        // Step 2: Build enriched prompt with scraped context and pass to Gemini
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

    private async Task<string> FetchScrapedContextAsync(
        string query,
        UserConfiguration config,
        CancellationToken ct)
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

        var result = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return result.TryGetProperty("context", out var ctx) ? ctx.GetString() ?? "" : "";
    }
}
