using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace AnafAutoToken.Tests;

internal static class TestJwt
{
    private static readonly byte[] Key =
        Encoding.ASCII.GetBytes("this-is-a-very-secure-key-for-testing-purposes-only-12345");

    /// <summary>Token JWT wygasający za podaną liczbę dni (ujemna = już wygasł).</summary>
    public static string ExpiringInDays(double days)
    {
        var expires = DateTime.UtcNow.AddDays(days);
        var handler = new JwtSecurityTokenHandler();

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([new Claim("id", "1")]),
            // Zawsze przed Expires, także dla tokenu, który już wygasł - JWT wymaga
            // ostrej nierówności. 90 dni to długość życia access tokenu w ANAF.
            NotBefore = expires.AddDays(-90),
            Expires = expires,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Key),
                SecurityAlgorithms.HmacSha256Signature)
        };

        return handler.WriteToken(handler.CreateToken(descriptor));
    }
}
