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
    private static string BuildConnectionString(string databasePath) =>
        new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite
        }.ToString();

    public static async Task<IReadOnlyList<TokenLogRow>> ReadAllAsync(
        string databasePath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(databasePath))
        {
            throw new FileNotFoundException($"Nie znaleziono pliku bazy danych: {databasePath}", databasePath);
        }

        await using var connection = new SqliteConnection(BuildConnectionString(databasePath));
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

    public static async Task<IReadOnlyList<TokenCheckRow>> ReadChecksAsync(
        string databasePath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(databasePath))
        {
            throw new FileNotFoundException($"Nie znaleziono pliku bazy danych: {databasePath}", databasePath);
        }

        await using var connection = new SqliteConnection(BuildConnectionString(databasePath));
        await connection.OpenAsync(cancellationToken);

        if (!await HasTableAsync(connection, "TokenCheckLogs", cancellationToken))
        {
            // Baza sprzed wprowadzenia historii przebiegow - pusta lista zamiast bledu.
            return [];
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, CheckedAt, Outcome, Trigger, AccessTokenExpiresAt, RefreshTokenExpiresAt, Message
            FROM TokenCheckLogs
            ORDER BY CheckedAt DESC, Id DESC
            """;

        var rows = new List<TokenCheckRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new TokenCheckRow(
                Id: reader.GetInt32(0),
                CheckedAt: reader.GetDateTime(1),
                Outcome: reader.GetInt32(2),
                Trigger: reader.GetInt32(3),
                AccessTokenExpiresAt: reader.IsDBNull(4) ? null : reader.GetDateTime(4),
                RefreshTokenExpiresAt: reader.IsDBNull(5) ? null : reader.GetDateTime(5),
                Message: reader.IsDBNull(6) ? null : reader.GetString(6)));
        }

        return rows;
    }

    private static async Task<bool> HasTableAsync(
        SqliteConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
        command.Parameters.AddWithValue("$name", tableName);

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) > 0;
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
