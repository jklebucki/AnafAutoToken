using System.IdentityModel.Tokens.Jwt;

namespace AnafAutoToken.Shared.Extensions;

public static class JwtExtensions
{
    public static DateTime? GetExpirationDate(this string jwtToken)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();

            if (!handler.CanReadToken(jwtToken))
                return null;

            var token = handler.ReadJwtToken(jwtToken);
            return token.ValidTo;
        }
        catch
        {
            return null;
        }
    }

    public static bool IsExpiringSoon(this string jwtToken, int daysThreshold)
    {
        var expirationDate = jwtToken.GetExpirationDate();

        if (!expirationDate.HasValue)
            return false;

        var thresholdDate = DateTime.UtcNow.AddDays(daysThreshold);
        return expirationDate.Value <= thresholdDate;
    }

    public static bool IsValid(this string jwtToken)
    {
        var expirationDate = jwtToken.GetExpirationDate();
        return expirationDate.HasValue && expirationDate.Value > DateTime.UtcNow;
    }
}
