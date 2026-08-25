using AnafAutoToken.Core.Interfaces;
using AnafAutoToken.Infrastructure.Data;
using AnafAutoToken.Shared.Configuration;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AnafAutoToken.Tests.Data;

/// <summary>
/// Kolumna <c>Odswiezenie</c> sprawdzana na prawdziwym pliku SQLite - chodzi o to, co
/// operator zobaczy zaglądając do bazy, więc asercje idą po surowym SQL, a nie po encji.
/// </summary>
[Collection(DataDirectoryCollection.Name)]
public class RefreshModeColumnTests : IDisposable
{
    public RefreshModeColumnTests() => Cleanup();

    [Fact]
    public async Task ManualEntry_IsStoredAsReczne()
    {
        await TokenDatabase.EnsureCreatedAsync(AppPaths.DefaultConnectionString);

        var id = await TokenDatabase.AddManualTokenPairAsync(
            AppPaths.DefaultConnectionString,
            accessToken: "wklejony-access-token",
            refreshToken: "wklejony-refresh-token",
            accessTokenExpiresAt: DateTime.UtcNow.AddDays(88),
            refreshTokenExpiresAt: DateTime.UtcNow.AddDays(360));

        id.Should().BeGreaterThan(0);
        (await ReadRefreshModeAsync(id)).Should().Be("Ręczne");

        // Wpis ma wyglądać jak udane odświeżenie - inaczej serwis by go nie użył.
        (await ReadColumnAsync(id, "IsSuccess")).Should().Be(1L);
        (await ReadColumnAsync(id, "RefreshToken")).Should().Be("wklejony-refresh-token");
    }

    [Fact]
    public async Task AutomaticEntry_IsStoredAsAuto()
    {
        await TokenDatabase.EnsureCreatedAsync(AppPaths.DefaultConnectionString);

        var options = new DbContextOptionsBuilder<AnafDbContext>()
            .UseSqlite(TokenDatabase.ResolveConnectionString(AppPaths.DefaultConnectionString))
            .Options;

        await using var context = new AnafDbContext(options);

        var log = new TokenRefreshLog
        {
            AccessToken = "access",
            RefreshToken = "refresh",
            ExpiresAt = DateTime.UtcNow.AddDays(90),
            CreatedAt = DateTime.UtcNow,
            IsSuccess = true
        };

        context.TokenRefreshLogs.Add(log);
        await context.SaveChangesAsync();

        // Domyślna wartość encji - serwis nie musi jej ustawiać, żeby wpis był "Auto".
        (await ReadRefreshModeAsync(log.Id)).Should().Be("Auto");
    }

    [Fact]
    public async Task ManualEntry_BecomesTheRefreshTokenTheServiceWillUse()
    {
        await TokenDatabase.EnsureCreatedAsync(AppPaths.DefaultConnectionString);

        var options = new DbContextOptionsBuilder<AnafDbContext>()
            .UseSqlite(TokenDatabase.ResolveConnectionString(AppPaths.DefaultConnectionString))
            .Options;

        await using (var context = new AnafDbContext(options))
        {
            context.TokenRefreshLogs.Add(new TokenRefreshLog
            {
                AccessToken = "stary-access",
                RefreshToken = "stary-refresh",
                ExpiresAt = DateTime.UtcNow.AddDays(1),
                CreatedAt = DateTime.UtcNow.AddMinutes(-5),
                IsSuccess = true
            });

            await context.SaveChangesAsync();
        }

        await TokenDatabase.AddManualTokenPairAsync(
            AppPaths.DefaultConnectionString,
            "nowy-access",
            "nowy-refresh",
            DateTime.UtcNow.AddDays(88),
            DateTime.UtcNow.AddDays(360));

        await using var verification = new AnafDbContext(options);

        var newest = await verification.TokenRefreshLogs
            .Where(log => log.IsSuccess)
            .OrderByDescending(log => log.CreatedAt)
            .ThenByDescending(log => log.Id)
            .FirstAsync();

        newest.RefreshToken.Should().Be("nowy-refresh");
        newest.RefreshMode.Should().Be(TokenRefreshMode.Manual);
    }

    private static async Task<string?> ReadRefreshModeAsync(int id) =>
        (string?)await ReadColumnAsync(id, "Odswiezenie");

    private static async Task<object?> ReadColumnAsync(int id, string column)
    {
        await using var connection = new SqliteConnection(
            TokenDatabase.ResolveConnectionString(AppPaths.DefaultConnectionString));

        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {column} FROM TokenRefreshLogs WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);

        return await command.ExecuteScalarAsync();
    }

    public void Dispose() => Cleanup();

    private static void Cleanup()
    {
        SqliteConnection.ClearAllPools();

        if (Directory.Exists(TestDataDirectory.Path))
        {
            Directory.Delete(TestDataDirectory.Path, recursive: true);
        }
    }
}
