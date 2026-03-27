using Microsoft.EntityFrameworkCore;
using StudentApp.Api.Data;
using StudentApp.Api.Data.Entities;
using StudentApp.Api.Models.DTOs;
using StudentApp.Api.Models.Requests;

namespace StudentApp.Api.Services;

/// <summary>
/// Admin-only user management: create, list, soft-delete, restore.
/// </summary>
public interface IUserService
{
    Task<UserDto> CreateUserAsync(CreateUserRequest request);
    Task<List<UserDto>> GetAllUsersAsync(bool includeDeleted = false);
    Task<UserDto?> GetByPublicIdAsync(Guid publicId);
    Task<bool> SoftDeleteAsync(Guid publicId);
    Task<bool> RestoreAsync(Guid publicId);
}

public sealed class UserService : IUserService
{
    private readonly ApplicationDbContext _db;

    public UserService(ApplicationDbContext db) => _db = db;

    public async Task<UserDto> CreateUserAsync(CreateUserRequest request)
    {
        if (await _db.Users.AnyAsync(u => u.Username == request.Username))
            throw new InvalidOperationException($"Użytkownik '{request.Username}' już istnieje.");

        var user = new User
        {
            Username = request.Username,
            DisplayName = request.DisplayName,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Role = UserRole.User
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        // Create empty configuration
        _db.UserConfigurations.Add(new UserConfiguration { UserId = user.Id });
        await _db.SaveChangesAsync();

        return user.ToDto();
    }

    public async Task<List<UserDto>> GetAllUsersAsync(bool includeDeleted = false)
    {
        var query = includeDeleted
            ? _db.Users.IgnoreQueryFilters()
            : _db.Users.AsQueryable();

        return await query
            .OrderBy(u => u.Username)
            .Select(u => u.ToDto())
            .ToListAsync();
    }

    public async Task<UserDto?> GetByPublicIdAsync(Guid publicId)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.PublicId == publicId);
        return user?.ToDto();
    }

    public async Task<bool> SoftDeleteAsync(Guid publicId)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.PublicId == publicId);
        if (user is null || user.Role == UserRole.Admin) return false;

        user.IsDeleted = true;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> RestoreAsync(Guid publicId)
    {
        var user = await _db.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.PublicId == publicId);
        if (user is null) return false;

        user.IsDeleted = false;
        await _db.SaveChangesAsync();
        return true;
    }
}
