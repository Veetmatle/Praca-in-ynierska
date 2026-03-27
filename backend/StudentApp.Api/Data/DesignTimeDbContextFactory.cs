using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace StudentApp.Api.Data;

/// <summary>
/// Design-time factory for EF Core CLI tools (dotnet ef migrations add ...).
/// Used only during development — in production, DI resolves the context.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<ApplicationDbContext>();
        
        // Default connection for local dev — override via env or appsettings
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Host=localhost;Database=studentapp;Username=studentapp;Password=devpassword";
        
        builder.UseNpgsql(connectionString);
        
        return new ApplicationDbContext(builder.Options);
    }
}
