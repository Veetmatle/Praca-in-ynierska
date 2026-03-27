using System.Net;
using Microsoft.EntityFrameworkCore;
using Polly;
using Polly.Extensions.Http;
using StudentApp.Api.Data;
using StudentApp.Api.Security;
using StudentApp.Api.Services;

namespace StudentApp.Api.Infrastructure;

/// <summary>
/// Extension methods for configuring application services with dependency injection.
/// Adapted from the original Discord bot's ServiceCollectionExtensions — 
/// Riot/Discord removed, PostgreSQL + JWT + SignalR added.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration config)
    {
        // ── Database ─────────────────────────────────────
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(config.GetConnectionString("DefaultConnection")));

        // ── Security ─────────────────────────────────────
        services.AddSingleton<IEncryptionService, AesGcmEncryptionService>();

        // ── Business Services ────────────────────────────
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IChatService, ChatService>();
        services.AddScoped<IConfigurationService, ConfigurationService>();
        services.AddScoped<IGeminiService, GeminiService>();
        services.AddScoped<IOpenClawService, OpenClawService>();
        services.AddScoped<IUniScraperService, UniScraperService>();

        // ── HTTP Clients with Polly ──────────────────────
        services.AddGeminiHttpClient();
        services.AddOpenClawHttpClient(config);
        services.AddUniScraperHttpClient(config);

        return services;
    }

    private static void AddGeminiHttpClient(this IServiceCollection services)
    {
        services.AddHttpClient("GeminiApi", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(120);
        }).AddPolicyHandler(GetRetryPolicy("Gemini"));
    }

    private static void AddOpenClawHttpClient(this IServiceCollection services, IConfiguration config)
    {
        var baseUrl = config["OpenClaw:BaseUrl"] ?? "http://openclaw-api:8000";
        services.AddHttpClient("OpenClawApi", client =>
        {
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromMinutes(12);
        }).AddPolicyHandler(GetRetryPolicy("OpenClaw"));
    }

    private static void AddUniScraperHttpClient(this IServiceCollection services, IConfiguration config)
    {
        var baseUrl = config["UniScraper:BaseUrl"] ?? "http://uniscraper-api:8001";
        services.AddHttpClient("UniScraperApi", client =>
        {
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(60);
        }).AddPolicyHandler(GetRetryPolicy("UniScraper"));
    }

    /// <summary>
    /// Shared Polly retry policy — adapted from the original bot's GetRetryPolicy.
    /// Handles transient HTTP errors and 429 with Retry-After header support.
    /// </summary>
    private static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy(string serviceName)
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(msg => msg.StatusCode == HttpStatusCode.TooManyRequests)
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: (retryAttempt, response, _) =>
                {
                    if (response.Result?.StatusCode == HttpStatusCode.TooManyRequests &&
                        response.Result.Headers.RetryAfter?.Delta is { } retryAfter)
                        return retryAfter;
                    return TimeSpan.FromSeconds(Math.Pow(2, retryAttempt));
                },
                onRetryAsync: (outcome, timespan, retryAttempt, _) =>
                {
                    Serilog.Log.Warning("{Service} API retry {Attempt} after {Delay}s. Reason: {Reason}",
                        serviceName, retryAttempt, timespan.TotalSeconds,
                        outcome.Result?.StatusCode.ToString() ?? outcome.Exception?.Message);
                    return Task.CompletedTask;
                });
    }
}
