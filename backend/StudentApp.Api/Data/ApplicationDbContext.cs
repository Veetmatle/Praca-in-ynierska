using Microsoft.EntityFrameworkCore;
using StudentApp.Api.Data.Entities;

namespace StudentApp.Api.Data;

/// <summary>
/// Application database context. Configures entity relationships, indexes,
/// query filters for soft-delete, and seeds the default admin account.
/// </summary>
public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<UserConfiguration> UserConfigurations => Set<UserConfiguration>();
    public DbSet<ChatSession> ChatSessions => Set<ChatSession>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── User ─────────────────────────────────────────
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasIndex(u => u.PublicId).IsUnique();
            entity.HasIndex(u => u.Username).IsUnique();
            entity.Property(u => u.Username).HasMaxLength(100);
            entity.Property(u => u.DisplayName).HasMaxLength(200);
            entity.Property(u => u.PasswordHash).HasMaxLength(500);
            
            // Global query filter — soft-deleted users are excluded by default
            entity.HasQueryFilter(u => !u.IsDeleted);
        });

        // ── UserConfiguration ────────────────────────────
        modelBuilder.Entity<UserConfiguration>(entity =>
        {
            entity.HasIndex(uc => uc.PublicId).IsUnique();
            entity.HasIndex(uc => uc.UserId).IsUnique();
            
            entity.HasOne(uc => uc.User)
                  .WithOne(u => u.Configuration)
                  .HasForeignKey<UserConfiguration>(uc => uc.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
                  
            entity.Property(uc => uc.GeminiApiKeyEncrypted).HasMaxLength(1000);
            entity.Property(uc => uc.AnthropicApiKeyEncrypted).HasMaxLength(1000);
            entity.Property(uc => uc.UniversityName).HasMaxLength(300);
            entity.Property(uc => uc.Faculty).HasMaxLength(200);
            entity.Property(uc => uc.FieldOfStudy).HasMaxLength(200);
            entity.Property(uc => uc.AcademicYear).HasMaxLength(20);
            entity.Property(uc => uc.DeanGroup).HasMaxLength(50);
        });

        // ── ChatSession ──────────────────────────────────
        modelBuilder.Entity<ChatSession>(entity =>
        {
            entity.HasIndex(cs => cs.PublicId).IsUnique();
            entity.HasIndex(cs => new { cs.UserId, cs.UpdatedAt });
            entity.Property(cs => cs.Title).HasMaxLength(300);
            
            entity.HasQueryFilter(cs => !cs.IsDeleted);
            
            entity.HasOne(cs => cs.User)
                  .WithMany(u => u.ChatSessions)
                  .HasForeignKey(cs => cs.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // ── ChatMessage ──────────────────────────────────
        modelBuilder.Entity<ChatMessage>(entity =>
        {
            entity.HasIndex(cm => new { cm.ChatSessionId, cm.CreatedAt });
            
            entity.HasOne(cm => cm.ChatSession)
                  .WithMany(cs => cs.Messages)
                  .HasForeignKey(cm => cm.ChatSessionId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // ── RefreshToken ─────────────────────────────────
        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasIndex(rt => rt.Token).IsUnique();
            entity.Property(rt => rt.Token).HasMaxLength(500);
            entity.Property(rt => rt.CreatedByIp).HasMaxLength(50);
            
            entity.HasOne(rt => rt.User)
                  .WithMany(u => u.RefreshTokens)
                  .HasForeignKey(rt => rt.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Seed: Super Admin ────────────────────────────
        // Password will be set at runtime via DbInitializer, not here.
    }
}
