using AnafAutoToken.Shared.Extensions;
using Microsoft.Extensions.Logging;

namespace AnafAutoToken.Core.Services;

public class TokenValidationService(ILogger<TokenValidationService> logger) : ITokenValidationService
{
    public bool ShouldRefreshToken(string accessToken, int daysBeforeExpiration)
    {
        try
        {
            var isExpiringSoon = accessToken.IsExpiringSoon(daysBeforeExpiration);

            if (isExpiringSoon)
            {
                var expirationDate = accessToken.GetExpirationDate();
                logger.LogInformation(
                    "Token is expiring soon. Expiration date: {ExpirationDate}, Days threshold: {DaysThreshold}",
                    expirationDate,
                    daysBeforeExpiration);
            }

            return isExpiringSoon;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error validating token expiration");
            return false;
        }
    }

    public DateTime? GetExpirationDate(string accessToken)
    {
        try
        {
            return accessToken.GetExpirationDate();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting token expiration date");
            return null;
        }
    }

    public bool IsTokenValid(string accessToken)
    {
        try
        {
            return accessToken.IsValid();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking token validity");
            return false;
        }
    }
}
