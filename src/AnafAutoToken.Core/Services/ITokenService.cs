using AnafAutoToken.Core.Interfaces;
using AnafAutoToken.Core.Models;

namespace AnafAutoToken.Core.Services;

public interface ITokenService
{
    Task<TokenRefreshResult> CheckAndRefreshTokenIfNeededAsync(
        TokenCheckTrigger trigger = TokenCheckTrigger.Scheduled,
        CancellationToken cancellationToken = default);
}
