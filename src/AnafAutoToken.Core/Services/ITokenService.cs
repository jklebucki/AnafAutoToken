using AnafAutoToken.Core.Models;

namespace AnafAutoToken.Core.Services;

public interface ITokenService
{
    Task<TokenRefreshResult> CheckAndRefreshTokenIfNeededAsync(CancellationToken cancellationToken = default);
}
