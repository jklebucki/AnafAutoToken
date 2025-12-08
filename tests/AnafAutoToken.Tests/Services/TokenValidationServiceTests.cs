using AnafAutoToken.Core.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;

namespace AnafAutoToken.Tests.Services;

public class TokenValidationServiceTests
{
    private readonly Mock<ILogger<TokenValidationService>> _loggerMock;
    private readonly TokenValidationService _service;

    public TokenValidationServiceTests()
    {
        _loggerMock = new Mock<ILogger<TokenValidationService>>();
        _service = new TokenValidationService(_loggerMock.Object);
    }

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
    public void ShouldRefreshToken_WithTokenExpiringSoon_ReturnsTrue()
    {
        // Arrange
        var expirationDate = DateTime.UtcNow.AddDays(5);
        var token = CreateValidToken(expirationDate);
        var daysBeforeExpiration = 7;

        // Act
        var result = _service.ShouldRefreshToken(token, daysBeforeExpiration);

        // Assert
        result.Should().BeTrue("token expires within threshold");
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Token is expiring soon")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void ShouldRefreshToken_WithTokenNotExpiringSoon_ReturnsFalse()
    {
        // Arrange
        var expirationDate = DateTime.UtcNow.AddDays(10);
        var token = CreateValidToken(expirationDate);
        var daysBeforeExpiration = 7;

        // Act
        var result = _service.ShouldRefreshToken(token, daysBeforeExpiration);

        // Assert
        result.Should().BeFalse("token does not expire within threshold");
    }

    [Fact]
    public void ShouldRefreshToken_WithInvalidToken_ReturnsFalse()
    {
        // Arrange
        var invalidToken = "invalid.jwt.token";
        var daysBeforeExpiration = 7;

        // Act
        var result = _service.ShouldRefreshToken(invalidToken, daysBeforeExpiration);

        // Assert
        result.Should().BeFalse("invalid token should return false");
    }

    [Fact]
    public void GetExpirationDate_WithValidToken_ReturnsCorrectDate()
    {
        // Arrange
        var expectedExpiration = DateTime.UtcNow.AddHours(2);
        var token = CreateValidToken(expectedExpiration);

        // Act
        var result = _service.GetExpirationDate(token);

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
        var result = _service.GetExpirationDate(invalidToken);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void IsTokenValid_WithValidToken_ReturnsTrue()
    {
        // Arrange
        var expirationDate = DateTime.UtcNow.AddHours(1);
        var token = CreateValidToken(expirationDate);

        // Act
        var result = _service.IsTokenValid(token);

        // Assert
        result.Should().BeTrue("token is still valid");
    }

    [Fact]
    public void IsTokenValid_WithExpiredToken_ReturnsFalse()
    {
        // Arrange
        var expirationDate = DateTime.UtcNow.AddHours(-1);
        var token = CreateValidToken(expirationDate);

        // Act
        var result = _service.IsTokenValid(token);

        // Assert
        result.Should().BeFalse("token has expired");
    }

    [Fact]
    public void IsTokenValid_WithInvalidToken_ReturnsFalse()
    {
        // Arrange
        var invalidToken = "invalid.jwt.token";

        // Act
        var result = _service.IsTokenValid(invalidToken);

        // Assert
        result.Should().BeFalse("invalid token should not be valid");
    }

    [Fact]
    public void ShouldRefreshToken_WithExpiredToken_ReturnsTrue()
    {
        // Arrange
        var expirationDate = DateTime.UtcNow.AddDays(-1);
        var token = CreateValidToken(expirationDate);
        var daysBeforeExpiration = 7;

        // Act
        var result = _service.ShouldRefreshToken(token, daysBeforeExpiration);

        // Assert
        result.Should().BeTrue("expired token should be refreshed");
    }

    [Fact]
    public void ShouldRefreshToken_WithTokenExpiringExactlyAtThreshold_ReturnsTrue()
    {
        // Arrange
        var daysBeforeExpiration = 7;
        var expirationDate = DateTime.UtcNow.AddDays(daysBeforeExpiration);
        var token = CreateValidToken(expirationDate);

        // Act
        var result = _service.ShouldRefreshToken(token, daysBeforeExpiration);

        // Assert
        result.Should().BeTrue("token expiring exactly at threshold should be refreshed");
    }
}
