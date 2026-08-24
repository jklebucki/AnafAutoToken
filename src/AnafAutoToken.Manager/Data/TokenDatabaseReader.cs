using AnafAutoToken.Shared.Extensions;
using Microsoft.Data.Sqlite;

namespace AnafAutoToken.Manager.Data;

/// <summary>
/// Reads the token history straight from SQLite. Raw ADO.NET is used instead of EF Core
/// so that a database created by an older build (without <c>RefreshTokenExpiresAt</c>)
/// can still be inspected.
/// </summary>
internal static class TokenDatabaseReader
{
    public static string ResolveDatabasePath(string? connectionString, string baseDirectory)
    {
        var builder = new SqliteConnectionStringBuilder(
            string.IsNullOrWhiteSpace(connectionString) ? "Data Source=tokens.db" : connectionString);

        var dataSource = string.IsNullOrWhiteSpace(builder.DataSource) ? "tokens.db" : builder.DataSource;

        return Path.IsPathRooted(dataSource)
            ? Path.GetFullPath(dataSource)
            : Path.GetFullPath(Path.Combine(baseDirectory, dataSource));
    }

    public static async Task<IReadOnlyList<TokenLogRow>> ReadAllAsync(
        string databasePath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(databasePath))
        {
            throw new FileNotFoundException($"Nie znaleziono pliku bazy danych: {databasePath}", databasePath);
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite
        }.ToString();

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var hasRefreshTokenExpiresAt = await HasColumnAsync(connection, "RefreshTokenExpiresAt", cancellationToken);

        var refreshTokenExpiresAtColumn = hasRefreshTokenExpiresAt
            ? "RefreshTokenExpiresAt"
            : "NULL AS RefreshTokenExpiresAt";

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT Id,
                   AccessToken,
                   RefreshToken,
                   {refreshTokenExpiresAtColumn},
                   ExpiresAt,
                   CreatedAt,
                   IsSuccess,
                   ErrorMessage,
                   ResponseStatusCode
            FROM TokenRefreshLogs
            ORDER BY CreatedAt DESC, Id DESC
            """;

        var rows = new List<TokenLogRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var accessToken = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            var refreshToken = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);

            rows.Add(new TokenLogRow(
                Id: reader.GetInt32(0),
                AccessToken: accessToken,
                RefreshToken: refreshToken,
                StoredRefreshTokenExpiresAt: reader.IsDBNull(3) ? null : reader.GetDateTime(3),
                ExpiresAt: reader.GetDateTime(4),
                CreatedAt: reader.GetDateTime(5),
                IsSuccess: reader.GetBoolean(6),
                ErrorMessage: reader.IsDBNull(7) ? null : reader.GetString(7),
                ResponseStatusCode: reader.IsDBNull(8) ? null : reader.GetInt32(8)));
        }

        return rows;
    }

    private static async Task<bool> HasColumnAsync(
        SqliteConnection connection,
        string columnName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info('TokenRefreshLogs');";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

internal sealed record TokenLogRow(
    int Id,
    string AccessToken,
    string RefreshToken,
    DateTime? StoredRefreshTokenExpiresAt,
    DateTime ExpiresAt,
    DateTime CreatedAt,
    bool IsSuccess,
    string? ErrorMessage,
    int? ResponseStatusCode)
{
    /// <summary>Expiration read from the access token itself, not from the stored column.</summary>
    public DateTime? AccessTokenExpiresAt =>
        string.IsNullOrWhiteSpace(AccessToken) ? null : AccessToken.GetExpirationDate();

    /// <summary>
    /// ANAF refresh tokens are not always JWTs, so the value decoded from the token wins
    /// and the column written by the worker is the fallback.
    /// </summary>
    public DateTime? RefreshTokenExpiresAt =>
        (string.IsNullOrWhiteSpace(RefreshToken) ? null : RefreshToken.GetExpirationDate())
        ?? StoredRefreshTokenExpiresAt;

    public string Status => IsSuccess ? "OK" : "BŁĄD";
}
