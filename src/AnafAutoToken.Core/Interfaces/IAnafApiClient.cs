using AnafAutoToken.Core.Models;

namespace AnafAutoToken.Core.Interfaces;

public interface IAnafApiClient
{
    Task<AnafTokenResponse> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default);
}
