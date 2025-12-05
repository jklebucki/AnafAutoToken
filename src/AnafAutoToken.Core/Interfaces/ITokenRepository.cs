namespace AnafAutoToken.Core.Interfaces;

public interface ITokenRepository
{
    Task<string?> GetLatestRefreshTokenAsync(CancellationToken cancellationToken = default);
    Task AddTokenRefreshLogAsync(TokenRefreshLog log, CancellationToken cancellationToken = default);
    Task<TokenRefreshLog?> GetLatestSuccessfulLogAsync(CancellationToken cancellationToken = default);
}

public class TokenRefreshLog
{
    public int Id { get; set; }
    public required string RefreshToken { get; set; }
    public required string AccessToken { get; set; }
    public required DateTime ExpiresAt { get; set; }
    public required DateTime CreatedAt { get; set; }
    public required bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public int? ResponseStatusCode { get; set; }
}
