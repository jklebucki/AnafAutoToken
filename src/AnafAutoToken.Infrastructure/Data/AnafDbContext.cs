using AnafAutoToken.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AnafAutoToken.Infrastructure.Data;

public class AnafDbContext(DbContextOptions<AnafDbContext> options) : DbContext(options)
{
    public DbSet<TokenRefreshLog> TokenRefreshLogs => Set<TokenRefreshLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<TokenRefreshLog>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.RefreshToken)
                .IsRequired()
                .HasMaxLength(2000);

            entity.Property(e => e.AccessToken)
                .IsRequired()
                .HasMaxLength(5000);

            entity.Property(e => e.ExpiresAt)
                .IsRequired();

            entity.Property(e => e.CreatedAt)
                .IsRequired();

            entity.Property(e => e.IsSuccess)
                .IsRequired();

            entity.Property(e => e.ErrorMessage)
                .HasMaxLength(1000);

            entity.HasIndex(e => e.CreatedAt)
                .HasDatabaseName("IX_TokenRefreshLogs_CreatedAt");
        });
    }
}
