using Microsoft.EntityFrameworkCore;
using StudentApp.Api.Data.Entities;
using Serilog;

namespace StudentApp.Api.Data;

/// <summary>
/// Applies pending migrations and seeds the default Super Admin account on first run.
/// </summary>
public static class DbInitializer
{
    public static async Task InitializeAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        Log.Information("Applying database migrations...");
        await db.Database.MigrateAsync();
        Log.Information("Migrations applied successfully");

        // Seed admin if no users exist (ignoring soft-delete filter)
        var hasUsers = await db.Users.IgnoreQueryFilters().AnyAsync();
        if (!hasUsers)
        {
            var defaultPassword = config["Admin:DefaultPassword"] ?? "Admin123!";
            var admin = new User
            {
                Username = "admin",
                DisplayName = "Super Admin",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(defaultPassword),
                Role = UserRole.Admin,
                CreatedAt = DateTime.UtcNow
            };

            db.Users.Add(admin);
            await db.SaveChangesAsync();

            // Create empty configuration for admin
            db.UserConfigurations.Add(new UserConfiguration { UserId = admin.Id });
            await db.SaveChangesAsync();

            Log.Warning(
                "Default admin account created. Username: admin. " +
                "Password was set from Admin:DefaultPassword config. CHANGE IT IMMEDIATELY AFTER FIRST LOGIN!");
        }
    }
}
