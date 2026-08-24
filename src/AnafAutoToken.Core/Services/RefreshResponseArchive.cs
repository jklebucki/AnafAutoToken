using System.Text;
using AnafAutoToken.Shared.Configuration;
using Microsoft.Extensions.Logging;

namespace AnafAutoToken.Core.Services;

/// <summary>
/// Odkłada surową odpowiedź ANAF do katalogu danych jako
/// <c>refresh_response_yyyy-MM-dd_HH-mm-ss.json</c>. To zabezpieczenie na wypadek, gdyby
/// zapis do bazy albo do config.ini poszedł nie tak - rotowanego refresh tokena nie da się
/// odtworzyć z żadnego innego miejsca.
/// </summary>
public class RefreshResponseArchive(ILogger<RefreshResponseArchive> logger) : IRefreshResponseArchive
{
    private const string FileNamePrefix = "refresh_response_";

    public async Task<string?> SaveAsync(
        string? rawResponse,
        DateTime refreshedAt,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawResponse))
        {
            logger.LogWarning("ANAF response body was empty - nothing to archive");
            return null;
        }

        Directory.CreateDirectory(AppPaths.DataDirectory);

        var path = BuildAvailablePath(refreshedAt);

        // Zapisujemy dokładnie to, co przyszło z ANAF - bez reformatowania, żeby archiwum
        // było wiernym śladem odpowiedzi.
        await File.WriteAllTextAsync(path, rawResponse, new UTF8Encoding(false), cancellationToken);

        logger.LogInformation("ANAF refresh response archived to {ArchivePath}", path);

        return path;
    }

    /// <summary>
    /// Znacznik czasu jest lokalny - tak samo jak w nazwach kopii config.ini, żeby operator
    /// czytał obie listy w tej samej strefie.
    /// </summary>
    private static string BuildAvailablePath(DateTime refreshedAt)
    {
        var timestamp = (refreshedAt.Kind == DateTimeKind.Utc ? refreshedAt.ToLocalTime() : refreshedAt)
            .ToString("yyyy-MM-dd_HH-mm-ss");

        var path = Path.Combine(AppPaths.DataDirectory, $"{FileNamePrefix}{timestamp}.json");

        if (!File.Exists(path))
        {
            return path;
        }

        // Dwa odświeżenia w tej samej sekundzie są mało prawdopodobne, ale nadpisanie
        // wcześniejszego archiwum przekreślałoby cały sens tego zapisu.
        for (var attempt = 2; attempt < 100; attempt++)
        {
            var candidate = Path.Combine(AppPaths.DataDirectory, $"{FileNamePrefix}{timestamp}_{attempt}.json");

            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(AppPaths.DataDirectory, $"{FileNamePrefix}{timestamp}_{Guid.NewGuid():N}.json");
    }
}
