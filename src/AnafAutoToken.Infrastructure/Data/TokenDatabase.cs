using AnafAutoToken.Core.Interfaces;
using AnafAutoToken.Shared.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AnafAutoToken.Infrastructure.Data;

/// <summary>
/// Ustala i przygotowuje plik bazy. Wszystkie programy przechodzą przez ten typ, żeby
/// względne <c>Data Source</c> nie rozwijało się względem katalogu roboczego procesu -
/// usługa Windows startuje z C:\Windows\System32 i właśnie tam zakładała bazę.
/// </summary>
public static class TokenDatabase
{
    /// <summary>
    /// Zwraca connection string z bezwzględną ścieżką. Puste wejście oznacza domyślną
    /// bazę w katalogu danych; ścieżka względna jest rozwijana względem katalogu danych,
    /// a nie względem katalogu roboczego.
    /// </summary>
    public static string ResolveConnectionString(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return AppPaths.DefaultConnectionString;
        }

        var builder = new SqliteConnectionStringBuilder(connectionString);

        if (string.IsNullOrWhiteSpace(builder.DataSource))
        {
            builder.DataSource = AppPaths.DatabaseFile;
        }
        else if (!Path.IsPathRooted(builder.DataSource))
        {
            builder.DataSource = Path.Combine(AppPaths.DataDirectory, builder.DataSource);
        }

        builder.DataSource = Path.GetFullPath(builder.DataSource);

        return builder.ToString();
    }

    public static string ResolveDatabasePath(string? connectionString) =>
        new SqliteConnectionStringBuilder(ResolveConnectionString(connectionString)).DataSource;

    /// <summary>
    /// Dopisuje parę tokenów wprowadzoną ręcznie w menedżerze. Wiersz wygląda jak udane
    /// odświeżenie, bo dokładnie tym jest z punktu widzenia serwisu - różni go wyłącznie
    /// kolumna <c>Odswiezenie</c> = "Ręczne".
    /// </summary>
    public static async Task<int> AddManualTokenPairAsync(
        string? connectionString,
        string accessToken,
        string refreshToken,
        DateTime accessTokenExpiresAt,
        DateTime? refreshTokenExpiresAt,
        CancellationToken cancellationToken = default)
    {
        var options = new DbContextOptionsBuilder<AnafDbContext>()
            .UseSqlite(ResolveConnectionString(connectionString))
            .Options;

        await using var context = new AnafDbContext(options);
        await context.Database.MigrateAsync(cancellationToken);

        var log = new TokenRefreshLog
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresAt = accessTokenExpiresAt,
            RefreshTokenExpiresAt = refreshTokenExpiresAt,
            CreatedAt = DateTime.UtcNow,
            IsSuccess = true,
            ResponseStatusCode = null,
            RefreshMode = TokenRefreshMode.Manual
        };

        context.TokenRefreshLogs.Add(log);
        await context.SaveChangesAsync(cancellationToken);

        return log.Id;
    }

    /// <summary>Tworzy katalog bazy i nakłada migracje. Idempotentne.</summary>
    public static async Task<string> EnsureCreatedAsync(
        string? connectionString,
        CancellationToken cancellationToken = default)
    {
        var resolved = ResolveConnectionString(connectionString);
        var databasePath = new SqliteConnectionStringBuilder(resolved).DataSource;
        var directory = Path.GetDirectoryName(databasePath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var options = new DbContextOptionsBuilder<AnafDbContext>()
            .UseSqlite(resolved)
            .Options;

        await using var context = new AnafDbContext(options);
        await context.Database.MigrateAsync(cancellationToken);

        return databasePath;
    }
}
