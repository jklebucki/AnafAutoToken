using AnafAutoToken.Shared.Extensions;
using FluentAssertions;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;

namespace AnafAutoToken.Tests.Extensions;

public class JwtExtensionsTests
{
    private string CreateValidToken(DateTime expirationDate)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.ASCII.GetBytes("this-is-a-very-secure-key-for-testing-purposes-only-12345");
        var notBefore = expirationDate.AddHours(-2); // Ensure NotBefore is before Expires
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[] { new Claim("id", "1") }),
            NotBefore = notBefore,
            Expires = expirationDate,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature)
        };
        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    [Fact]
    public void GetExpirationDate_WithValidToken_ReturnsCorrectExpirationDate()
    {
        // Arrange
        var expectedExpiration = DateTime.UtcNow.AddHours(2);
        var token = CreateValidToken(expectedExpiration);

        // Act
        var result = token.GetExpirationDate();

        // Assert
        result.Should().NotBeNull();
        result.Should().BeCloseTo(expectedExpiration, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void GetExpirationDate_WithInvalidToken_ReturnsNull()
    {
        // Arrange
        var invalidToken = "invalid.jwt.token";

        // Act
        var result = invalidToken.GetExpirationDate();

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetExpirationDate_WithEmptyString_ReturnsNull()
    {
        // Arrange
        var emptyToken = string.Empty;

        // Act
        var result = emptyToken.GetExpirationDate();

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetExpirationDate_WithNull_ReturnsNull()
    {
        // Arrange
        string? nullToken = null;

        // Act
        var result = nullToken!.GetExpirationDate();

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void IsExpiringSoon_WithTokenExpiringIn5Days_AndThresholdOf7Days_ReturnsTrue()
    {
        // Arrange
        var expirationDate = DateTime.UtcNow.AddDays(5);
        var token = CreateValidToken(expirationDate);
        var daysThreshold = 7;

        // Act
        var result = token.IsExpiringSoon(daysThreshold);

        // Assert
        result.Should().BeTrue("token expires in 5 days which is within 7 days threshold");
    }

    [Fact]
    public void IsExpiringSoon_WithTokenExpiringIn10Days_AndThresholdOf7Days_ReturnsFalse()
    {
        // Arrange
        var expirationDate = DateTime.UtcNow.AddDays(10);
        var token = CreateValidToken(expirationDate);
        var daysThreshold = 7;

        // Act
        var result = token.IsExpiringSoon(daysThreshold);

        // Assert
        result.Should().BeFalse("token expires in 10 days which is beyond 7 days threshold");
    }

    [Fact]
    public void IsExpiringSoon_WithExpiredToken_ReturnsTrue()
    {
        // Arrange
        var expirationDate = DateTime.UtcNow.AddDays(-1);
        var token = CreateValidToken(expirationDate);
        var daysThreshold = 7;

        // Act
        var result = token.IsExpiringSoon(daysThreshold);

        // Assert
        result.Should().BeTrue("token is already expired");
    }

    [Fact]
    public void IsExpiringSoon_WithInvalidToken_ReturnsFalse()
    {
        // Arrange
        var invalidToken = "invalid.jwt.token";
        var daysThreshold = 7;

        // Act
        var result = invalidToken.IsExpiringSoon(daysThreshold);

        // Assert
        result.Should().BeFalse("invalid token cannot be validated");
    }

    [Fact]
    public void IsExpiringSoon_WithTokenExpiringExactlyAtThreshold_ReturnsTrue()
    {
        // Arrange
        var daysThreshold = 7;
        var expirationDate = DateTime.UtcNow.AddDays(daysThreshold);
        var token = CreateValidToken(expirationDate);

        // Act
        var result = token.IsExpiringSoon(daysThreshold);

        // Assert
        result.Should().BeTrue("token expires exactly at threshold");
    }

    [Fact]
    public void IsValid_WithValidFutureToken_ReturnsTrue()
    {
        // Arrange
        var expirationDate = DateTime.UtcNow.AddHours(1);
        var token = CreateValidToken(expirationDate);

        // Act
        var result = token.IsValid();

        // Assert
        result.Should().BeTrue("token has not expired yet");
    }

    [Fact]
    public void IsValid_WithExpiredToken_ReturnsFalse()
    {
        // Arrange
        var expirationDate = DateTime.UtcNow.AddHours(-1);
        var token = CreateValidToken(expirationDate);

        // Act
        var result = token.IsValid();

        // Assert
        result.Should().BeFalse("token has already expired");
    }

    [Fact]
    public void IsValid_WithInvalidToken_ReturnsFalse()
    {
        // Arrange
        var invalidToken = "invalid.jwt.token";

        // Act
        var result = invalidToken.IsValid();

        // Assert
        result.Should().BeFalse("invalid token cannot be validated");
    }

    [Fact]
    public void IsValid_WithMalformedJwt_ReturnsFalse()
    {
        // Arrange
        var malformedToken = "header.payload";

        // Act
        var result = malformedToken.IsValid();

        // Assert
        result.Should().BeFalse("malformed JWT should not be valid");
    }
}
