namespace AnafAutoToken.Shared.Models;

public sealed record CurrentTokenExportFile(
    DateTime ExportedAtUtc,
    string? SourceDatabase,
    CurrentTokenPayload CurrentToken);

public sealed record CurrentTokenPayload(
    string AccessToken,
    string RefreshToken,
    DateTime? RefreshTokenExpiresAt,
    DateTime? AccessTokenExpiresAt,
    DateTime? StoredExpiresAt,
    DateTime? SavedAt,
    string Source);

/// <summary>
/// Odpowiedź endpointu ręcznego odświeżenia tokenu (POST /api/tokens/refresh).
/// Współdzielona z menedżerem, żeby obie strony nie rozjechały się na kształcie JSON-a.
/// </summary>
public sealed record ManualTokenRefreshResponse(
    bool IsSuccess,
    bool TokenWasRefreshed,
    DateTime? NewExpirationDate,
    string? ErrorMessage,
    DateTime StartedAtUtc,
    DateTime CompletedAtUtc);
