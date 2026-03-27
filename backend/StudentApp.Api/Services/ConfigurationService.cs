using Microsoft.EntityFrameworkCore;
using StudentApp.Api.Data;
using StudentApp.Api.Data.Entities;
using StudentApp.Api.Models.DTOs;
using StudentApp.Api.Models.Requests;
using StudentApp.Api.Security;

namespace StudentApp.Api.Services;

/// <summary>
/// Manages user configuration — API keys are encrypted before storage.
/// </summary>
public interface IConfigurationService
{
    Task<UserConfigurationDto?> GetConfigAsync(int userId);
    Task<UserConfigurationDto> UpdateConfigAsync(int userId, UpdateConfigurationRequest request);
    Task<string?> GetDecryptedGeminiKeyAsync(int userId);
    Task<string?> GetDecryptedAnthropicKeyAsync(int userId);
    Task<UserConfiguration?> GetRawConfigAsync(int userId);
}

public sealed class ConfigurationService : IConfigurationService
{
    private readonly ApplicationDbContext _db;
    private readonly IEncryptionService _encryption;

    public ConfigurationService(ApplicationDbContext db, IEncryptionService encryption)
    {
        _db = db;
        _encryption = encryption;
    }

    public async Task<UserConfigurationDto?> GetConfigAsync(int userId)
    {
        var cfg = await _db.UserConfigurations.FirstOrDefaultAsync(c => c.UserId == userId);
        return cfg?.ToDto();
    }

    public async Task<UserConfigurationDto> UpdateConfigAsync(int userId, UpdateConfigurationRequest request)
    {
        var cfg = await _db.UserConfigurations.FirstOrDefaultAsync(c => c.UserId == userId);
        if (cfg is null)
        {
            cfg = new UserConfiguration { UserId = userId };
            _db.UserConfigurations.Add(cfg);
        }

        // Encrypt API keys if provided (empty string = clear)
        if (request.GeminiApiKey is not null)
            cfg.GeminiApiKeyEncrypted = string.IsNullOrEmpty(request.GeminiApiKey)
                ? null
                : _encryption.Encrypt(request.GeminiApiKey);

        if (request.AnthropicApiKey is not null)
            cfg.AnthropicApiKeyEncrypted = string.IsNullOrEmpty(request.AnthropicApiKey)
                ? null
                : _encryption.Encrypt(request.AnthropicApiKey);

        // Update non-sensitive fields
        if (request.UniversityName is not null) cfg.UniversityName = request.UniversityName;
        if (request.Faculty is not null) cfg.Faculty = request.Faculty;
        if (request.FieldOfStudy is not null) cfg.FieldOfStudy = request.FieldOfStudy;
        if (request.AcademicYear is not null) cfg.AcademicYear = request.AcademicYear;
        if (request.StudyYear.HasValue) cfg.StudyYear = request.StudyYear;
        if (request.DeanGroup is not null) cfg.DeanGroup = request.DeanGroup;
        if (request.GeminiModel is not null) cfg.GeminiModel = request.GeminiModel;
        if (request.AnthropicModel is not null) cfg.AnthropicModel = request.AnthropicModel;

        cfg.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return cfg.ToDto();
    }

    public async Task<string?> GetDecryptedGeminiKeyAsync(int userId)
    {
        var cfg = await _db.UserConfigurations.FirstOrDefaultAsync(c => c.UserId == userId);
        if (cfg?.GeminiApiKeyEncrypted is null) return null;
        return _encryption.Decrypt(cfg.GeminiApiKeyEncrypted);
    }

    public async Task<string?> GetDecryptedAnthropicKeyAsync(int userId)
    {
        var cfg = await _db.UserConfigurations.FirstOrDefaultAsync(c => c.UserId == userId);
        if (cfg?.AnthropicApiKeyEncrypted is null) return null;
        return _encryption.Decrypt(cfg.AnthropicApiKeyEncrypted);
    }

    public async Task<UserConfiguration?> GetRawConfigAsync(int userId)
    {
        return await _db.UserConfigurations.FirstOrDefaultAsync(c => c.UserId == userId);
    }
}
