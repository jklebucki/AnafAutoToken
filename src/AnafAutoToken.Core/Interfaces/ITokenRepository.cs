namespace AnafAutoToken.Core.Interfaces;

public interface ITokenRepository
{
    Task<string?> GetLatestRefreshTokenAsync(CancellationToken cancellationToken = default);
    Task AddTokenRefreshLogAsync(TokenRefreshLog log, CancellationToken cancellationToken = default);
    Task<TokenRefreshLog?> GetLatestSuccessfulLogAsync(CancellationToken cancellationToken = default);

    /// <summary>Zapisuje ślad po każdym przebiegu sprawdzenia, także gdy nic nie trzeba było robić.</summary>
    Task AddTokenCheckLogAsync(TokenCheckLog log, CancellationToken cancellationToken = default);

    Task<TokenCheckLog?> GetLatestCheckLogAsync(CancellationToken cancellationToken = default);
}

public class TokenRefreshLog
{
    public int Id { get; set; }
    public required string RefreshToken { get; set; }
    public required string AccessToken { get; set; }
    public required DateTime ExpiresAt { get; set; }
    public DateTime? RefreshTokenExpiresAt { get; set; }
    public required DateTime CreatedAt { get; set; }
    public required bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public int? ResponseStatusCode { get; set; }

    /// <summary>
    /// Skąd wziął się ten wpis: z automatycznego odświeżenia przez serwis, czy z pary
    /// wklejonej ręcznie w menedżerze. W bazie to kolumna <c>Odswiezenie</c>
    /// z wartościami "Auto" / "Ręczne".
    /// </summary>
    public TokenRefreshMode RefreshMode { get; set; } = TokenRefreshMode.Auto;
}

public enum TokenRefreshMode
{
    Auto = 0,
    Manual = 1
}

/// <summary>
/// Wpis powstaje przy KAŻDYM przebiegu sprawdzenia tokenu, niezależnie od tego, czy
/// odświeżenie było potrzebne. <see cref="TokenRefreshLog"/> notuje wyłącznie próby
/// odświeżenia, więc sam w sobie nie jest dowodem, że serwis w ogóle się uruchamiał.
/// </summary>
public class TokenCheckLog
{
    public int Id { get; set; }
    public required DateTime CheckedAt { get; set; }
    public required TokenCheckOutcome Outcome { get; set; }
    public required TokenCheckTrigger Trigger { get; set; }
    public DateTime? AccessTokenExpiresAt { get; set; }
    public DateTime? RefreshTokenExpiresAt { get; set; }
    public string? Message { get; set; }
}

public enum TokenCheckOutcome
{
    NoRefreshNeeded = 0,
    Refreshed = 1,
    Failed = 2
}

public enum TokenCheckTrigger
{
    Scheduled = 0,
    Manual = 1,
    Startup = 2
}
