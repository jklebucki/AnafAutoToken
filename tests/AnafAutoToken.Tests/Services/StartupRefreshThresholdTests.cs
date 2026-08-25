using AnafAutoToken.Core.Interfaces;
using AnafAutoToken.Core.Models;
using AnafAutoToken.Core.Services;
using AnafAutoToken.Shared.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace AnafAutoToken.Tests.Services;

/// <summary>
/// Próg z <c>DaysBeforeExpiration</c> sprawdzany na prawdziwym <see cref="TokenValidationService"/>
/// i prawdziwych tokenach JWT - atrapa walidatora sprawdzałaby tylko własne ustawienie.
/// </summary>
public class StartupRefreshThresholdTests
{
    private const int DaysBeforeExpiration = 3;

    private readonly Mock<IConfigFileService> _configFileServiceMock = new();
    private readonly Mock<IAnafApiClient> _anafApiClientMock = new();
    private readonly Mock<ITokenRepository> _tokenRepositoryMock = new();
    private readonly List<TokenCheckLog> _checkLogs = [];

    public StartupRefreshThresholdTests()
    {
        _configFileServiceMock
            .Setup(x => x.CreateBackupAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _configFileServiceMock
            .Setup(x => x.UpdateAccessTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _tokenRepositoryMock
            .Setup(x => x.AddTokenRefreshLogAsync(It.IsAny<TokenRefreshLog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _tokenRepositoryMock
            .Setup(x => x.AddTokenCheckLogAsync(It.IsAny<TokenCheckLog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback<TokenCheckLog, CancellationToken>((log, _) => _checkLogs.Add(log));

        _tokenRepositoryMock
            .Setup(x => x.GetLatestSuccessfulLogAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TokenRefreshLog
            {
                Id = 1,
                RefreshToken = "stored-refresh-token",
                AccessToken = "stored-access-token",
                ExpiresAt = DateTime.UtcNow.AddDays(1),
                CreatedAt = DateTime.UtcNow.AddDays(-80),
                IsSuccess = true
            });
    }

    [Theory]
    [InlineData(90)]
    [InlineData(30)]
    [InlineData(4)]
    [InlineData(3.5)]
    public async Task Startup_DoesNotRefreshWhileTheAccessTokenOutlivesTheThreshold(double daysUntilExpiry)
    {
        SetConfigFileToken(TestJwt.ExpiringInDays(daysUntilExpiry));

        var result = await CreateService().CheckAndRefreshTokenIfNeededAsync(TokenCheckTrigger.Startup);

        result.IsSuccess.Should().BeTrue();
        result.TokenWasRefreshed.Should().BeFalse();

        _anafApiClientMock.Verify(
            x => x.RefreshTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);

        _configFileServiceMock.Verify(
            x => x.UpdateAccessTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);

        _tokenRepositoryMock.Verify(
            x => x.AddTokenRefreshLogAsync(It.IsAny<TokenRefreshLog>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // Sam przebieg musi zostawić ślad - to jest dowód, że serwis wstał.
        _checkLogs.Single().Outcome.Should().Be(TokenCheckOutcome.NoRefreshNeeded);
        _checkLogs.Single().Trigger.Should().Be(TokenCheckTrigger.Startup);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(0.5)]
    [InlineData(-1)]
    public async Task Startup_RefreshesOnceTheAccessTokenReachesTheThreshold(double daysUntilExpiry)
    {
        SetConfigFileToken(TestJwt.ExpiringInDays(daysUntilExpiry));
        SetupSuccessfulApiResponse();

        var result = await CreateService().CheckAndRefreshTokenIfNeededAsync(TokenCheckTrigger.Startup);

        result.TokenWasRefreshed.Should().BeTrue();

        _anafApiClientMock.Verify(
            x => x.RefreshTokenAsync("stored-refresh-token", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task AutomaticRefresh_IsRecordedWithTheAutoMode()
    {
        SetConfigFileToken(TestJwt.ExpiringInDays(1));
        SetupSuccessfulApiResponse();

        TokenRefreshLog? saved = null;

        _tokenRepositoryMock
            .Setup(x => x.AddTokenRefreshLogAsync(It.IsAny<TokenRefreshLog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback<TokenRefreshLog, CancellationToken>((log, _) => saved = log);

        await CreateService().CheckAndRefreshTokenIfNeededAsync(TokenCheckTrigger.Scheduled);

        saved.Should().NotBeNull();
        saved!.RefreshMode.Should().Be(TokenRefreshMode.Auto);
    }

    private void SetConfigFileToken(string accessToken) =>
        _configFileServiceMock
            .Setup(x => x.ReadAccessTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(accessToken);

    private void SetupSuccessfulApiResponse() =>
        _anafApiClientMock
            .Setup(x => x.RefreshTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnafTokenResponse
            {
                AccessToken = TestJwt.ExpiringInDays(90),
                ExpiresIn = 60 * 60 * 24 * 90,
                RefreshToken = "rotated-refresh-token",
                RawJson = """{"access_token":"..."}"""
            });

    private TokenService CreateService() => new(
        _configFileServiceMock.Object,
        new TokenValidationService(Mock.Of<ILogger<TokenValidationService>>()),
        _anafApiClientMock.Object,
        _tokenRepositoryMock.Object,
        Mock.Of<IEmailNotificationService>(),
        Mock.Of<IRefreshResponseArchive>(),
        Options.Create(new AnafSettings
        {
            TokenEndpoint = "https://logincert.anaf.ro/anaf-oauth2/v1/token",
            BasicAuth = new BasicAuthSettings { Username = "u", Password = "p" },
            CheckSchedule = new CheckScheduleSettings { CheckHour = 12, CheckMinute = 0 },
            DaysBeforeExpiration = DaysBeforeExpiration,
            ConfigFilePath = Path.Combine(Path.GetTempPath(), "config.ini"),
            BackupDirectory = Path.Combine(Path.GetTempPath(), "backups")
        }),
        Mock.Of<ILogger<TokenService>>());
}
