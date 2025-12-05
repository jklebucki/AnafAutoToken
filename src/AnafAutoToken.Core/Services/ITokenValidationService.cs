namespace AnafAutoToken.Core.Services;

public interface ITokenValidationService
{
    bool ShouldRefreshToken(string accessToken, int daysBeforeExpiration);
    DateTime? GetExpirationDate(string accessToken);
    bool IsTokenValid(string accessToken);
}
