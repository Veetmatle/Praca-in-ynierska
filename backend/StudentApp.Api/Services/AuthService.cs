using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using StudentApp.Api.Data;
using StudentApp.Api.Data.Entities;
using StudentApp.Api.Models.Requests;
using StudentApp.Api.Models.Responses;

namespace StudentApp.Api.Services;

public interface IAuthService
{
    Task<AuthResponse?> LoginAsync(LoginRequest request, string? ipAddress);
    Task<AuthResponse?> RefreshAsync(string refreshToken, string? ipAddress);
    Task RevokeRefreshTokenAsync(string refreshToken);
    Task<bool> ChangePasswordAsync(int userId, ChangePasswordRequest request);
}

public sealed class AuthService : IAuthService
{
    private readonly ApplicationDbContext _db;
    private readonly IConfiguration _config;

    public AuthService(ApplicationDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    public async Task<AuthResponse?> LoginAsync(LoginRequest request, string? ipAddress)
    {
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Username == request.Username);

        if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            return null;

        user.LastLoginAt = DateTime.UtcNow;
        
        var accessToken = GenerateAccessToken(user);
        var refreshToken = await GenerateRefreshTokenAsync(user.Id, ipAddress);

        await _db.SaveChangesAsync();

        return new AuthResponse(
            accessToken.Token,
            refreshToken.Token,
            accessToken.ExpiresAt,
            user.Username,
            user.DisplayName,
            user.Role.ToString()
        );
    }

    public async Task<AuthResponse?> RefreshAsync(string token, string? ipAddress)
    {
        var existing = await _db.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.Token == token);

        if (existing is null || !existing.IsActive || existing.User.IsDeleted)
            return null;

        // Revoke old token
        existing.IsRevoked = true;
        existing.RevokedAt = DateTime.UtcNow;

        var accessToken = GenerateAccessToken(existing.User);
        var newRefresh = await GenerateRefreshTokenAsync(existing.UserId, ipAddress);

        await _db.SaveChangesAsync();

        return new AuthResponse(
            accessToken.Token,
            newRefresh.Token,
            accessToken.ExpiresAt,
            existing.User.Username,
            existing.User.DisplayName,
            existing.User.Role.ToString()
        );
    }

    public async Task RevokeRefreshTokenAsync(string token)
    {
        var existing = await _db.RefreshTokens.FirstOrDefaultAsync(rt => rt.Token == token);
        if (existing is not null && existing.IsActive)
        {
            existing.IsRevoked = true;
            existing.RevokedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
    }

    public async Task<bool> ChangePasswordAsync(int userId, ChangePasswordRequest request)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user is null) return false;

        if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash))
            return false;

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        await _db.SaveChangesAsync();
        return true;
    }

    // ── Private helpers ──────────────────────────────────

    private (string Token, DateTime ExpiresAt) GenerateAccessToken(User user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        
        var expiryMinutes = int.TryParse(_config["Jwt:AccessTokenExpirationMinutes"], out var m) ? m : 15;
        var expiresAt = DateTime.UtcNow.AddMinutes(expiryMinutes);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim("publicId", user.PublicId.ToString()),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim("displayName", user.DisplayName)
        };

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: expiresAt,
            signingCredentials: creds
        );

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    private async Task<RefreshToken> GenerateRefreshTokenAsync(int userId, string? ipAddress)
    {
        var expiryDays = int.TryParse(_config["Jwt:RefreshTokenExpirationDays"], out var d) ? d : 7;
        
        var refreshToken = new RefreshToken
        {
            UserId = userId,
            Token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)),
            ExpiresAt = DateTime.UtcNow.AddDays(expiryDays),
            CreatedByIp = ipAddress
        };

        _db.RefreshTokens.Add(refreshToken);
        
        // Clean up old tokens for this user
        var oldTokens = await _db.RefreshTokens
            .Where(rt => rt.UserId == userId && (rt.IsRevoked || rt.ExpiresAt <= DateTime.UtcNow))
            .ToListAsync();
        _db.RefreshTokens.RemoveRange(oldTokens);

        return refreshToken;
    }
}
