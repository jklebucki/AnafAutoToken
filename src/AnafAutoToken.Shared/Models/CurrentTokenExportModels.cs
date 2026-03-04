namespace AnafAutoToken.Shared.Models;

public sealed record CurrentTokenExportFile(
    DateTime ExportedAtUtc,
    string? SourceDatabase,
    CurrentTokenPayload CurrentToken);

public sealed record CurrentTokenPayload(
    string AccessToken,
    string RefreshToken,
    DateTime? AccessTokenExpiresAt,
    DateTime? StoredExpiresAt,
    DateTime? SavedAt,
    string Source);
