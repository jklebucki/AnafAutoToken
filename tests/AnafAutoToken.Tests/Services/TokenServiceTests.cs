using AnafAutoToken.Core.Interfaces;
using AnafAutoToken.Core.Models;
using AnafAutoToken.Core.Services;
using AnafAutoToken.Shared.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace AnafAutoToken.Tests.Services;

public class TokenServiceTests
{
    private const string StoredRefreshToken = "stored-refresh-token";
    private const string InitialRefreshToken = "initial-refresh-token";
    private const string RotatedRefreshToken = "rotated-refresh-token";
    private const string CurrentAccessToken = "current-access-token";
    private const string NewAccessToken = "new-access-token";
    private const string RawAnafResponse = """{"access_token":"new-access-token","refresh_token":"rotated-refresh-token"}""";

    private readonly Mock<IConfigFileService> _configFileServiceMock = new();
    private readonly Mock<ITokenValidationService> _tokenValidationServiceMock = new();
    private readonly Mock<IAnafApiClient> _anafApiClientMock = new();
    private readonly Mock<ITokenRepository> _tokenRepositoryMock = new();
    private readonly Mock<IEmailNotificationService> _emailNotificationServiceMock = new();
    private readonly Mock<IRefreshResponseArchive> _refreshResponseArchiveMock = new();
    private readonly List<string> _callOrder = [];
    private readonly List<TokenCheckLog> _checkLogs = [];

    public TokenServiceTests()
    {
        _configFileServiceMock
            .Setup(x => x.ReadAccessTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CurrentAccessToken);

        _configFileServiceMock
            .Setup(x => x.CreateBackupAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback(() => _callOrder.Add("config-backup"));

        _configFileServiceMock
            .Setup(x => x.UpdateAccessTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback(() => _callOrder.Add("config-update"));

        _tokenRepositoryMock
            .Setup(x => x.AddTokenRefreshLogAsync(It.IsAny<TokenRefreshLog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback(() => _callOrder.Add("database-save"));

        _tokenRepositoryMock
            .Setup(x => x.AddTokenCheckLogAsync(It.IsAny<TokenCheckLog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback<TokenCheckLog, CancellationToken>((log, _) => _checkLogs.Add(log));

        // The access token is readable and due for refresh unless a test says otherwise.
        _tokenValidationServiceMock
            .Setup(x => x.GetExpirationDate(It.IsAny<string>()))
            .Returns(DateTime.UtcNow.AddDays(1));

        _tokenValidationServiceMock
            .Setup(x => x.ShouldRefreshToken(It.IsAny<string>(), It.IsAny<int>()))
            .Returns(true);
    }

    [Fact]
    public async Task CheckAndRefreshTokenIfNeededAsync_SendsTheStoredRefreshTokenToAnaf()
    {
        StoreLatestSuccessfulLog(StoredRefreshToken);
        SetupApiResponse(RotatedRefreshToken);

        await CreateService().CheckAndRefreshTokenIfNeededAsync();

        _anafApiClientMock.Verify(
            x => x.RefreshTokenAsync(StoredRefreshToken, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CheckAndRefreshTokenIfNeededAsync_WithoutStoredLog_UsesInitialRefreshTokenFromConfiguration()
    {
        _tokenRepositoryMock
            .Setup(x => x.GetLatestSuccessfulLogAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((TokenRefreshLog?)null);

        SetupApiResponse(RotatedRefreshToken);

        await CreateService().CheckAndRefreshTokenIfNeededAsync();

        _anafApiClientMock.Verify(
            x => x.RefreshTokenAsync(InitialRefreshToken, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CheckAndRefreshTokenIfNeededAsync_StoresTheRotatedRefreshTokenReturnedByAnaf()
    {
        StoreLatestSuccessfulLog(StoredRefreshToken);
        SetupApiResponse(RotatedRefreshToken);

        var result = await CreateService().CheckAndRefreshTokenIfNeededAsync();

        result.IsSuccess.Should().BeTrue();
        var savedLog = CapturedSuccessfulLog();
        savedLog.RefreshToken.Should().Be(RotatedRefreshToken);
        savedLog.AccessToken.Should().Be(NewAccessToken);
    }

    [Fact]
    public async Task CheckAndRefreshTokenIfNeededAsync_WhenAnafReturnsNoRefreshToken_KeepsThePreviousOne()
    {
        StoreLatestSuccessfulLog(StoredRefreshToken);
        SetupApiResponse(refreshToken: null);

        var result = await CreateService().CheckAndRefreshTokenIfNeededAsync();

        result.IsSuccess.Should().BeTrue("a missing refresh token must not throw away a working access token");
        CapturedSuccessfulLog().RefreshToken.Should().Be(StoredRefreshToken);
    }

    [Fact]
    public async Task CheckAndRefreshTokenIfNeededAsync_WhenAnafReturnsBlankRefreshToken_KeepsThePreviousOne()
    {
        StoreLatestSuccessfulLog(StoredRefreshToken);
        SetupApiResponse("   ");

        await CreateService().CheckAndRefreshTokenIfNeededAsync();

        CapturedSuccessfulLog().RefreshToken.Should().Be(StoredRefreshToken);
    }

    [Fact]
    public async Task CheckAndRefreshTokenIfNeededAsync_PersistsTokensBeforeWritingTheConfigFile()
    {
        StoreLatestSuccessfulLog(StoredRefreshToken);
        SetupApiResponse(RotatedRefreshToken);

        await CreateService().CheckAndRefreshTokenIfNeededAsync();

        _callOrder.Should().Equal("database-save", "config-backup", "config-update");
    }

    [Fact]
    public async Task CheckAndRefreshTokenIfNeededAsync_WhenDatabaseSaveFails_ReportsFailure()
    {
        StoreLatestSuccessfulLog(StoredRefreshToken);
        SetupApiResponse(RotatedRefreshToken);

        _tokenRepositoryMock
            .Setup(x => x.AddTokenRefreshLogAsync(It.IsAny<TokenRefreshLog>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database is locked"));

        var result = await CreateService().CheckAndRefreshTokenIfNeededAsync();

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Be("database is locked");

        // The issued access token is still pushed to config.ini so it is not wasted.
        _configFileServiceMock.Verify(
            x => x.UpdateAccessTokenAsync(NewAccessToken, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CheckAndRefreshTokenIfNeededAsync_WhenRefreshFails_KeepsTheRefreshTokenThatWasUsed()
    {
        StoreLatestSuccessfulLog(StoredRefreshToken);

        _anafApiClientMock
            .Setup(x => x.RefreshTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("invalid_grant"));

        var result = await CreateService().CheckAndRefreshTokenIfNeededAsync();

        result.IsSuccess.Should().BeFalse();

        var failedLog = CapturedLogs().Single();
        failedLog.IsSuccess.Should().BeFalse();
        failedLog.RefreshToken.Should().Be(StoredRefreshToken);

        _configFileServiceMock.Verify(
            x => x.UpdateAccessTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CheckAndRefreshTokenIfNeededAsync_WithUnreadableAccessToken_ReportsFailureInsteadOfHealthyToken()
    {
        _tokenValidationServiceMock
            .Setup(x => x.GetExpirationDate(It.IsAny<string>()))
            .Returns((DateTime?)null);

        _tokenValidationServiceMock
            .Setup(x => x.ShouldRefreshToken(It.IsAny<string>(), It.IsAny<int>()))
            .Returns(false);

        var result = await CreateService().CheckAndRefreshTokenIfNeededAsync();

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("could not be parsed");

        _emailNotificationServiceMock.Verify(
            x => x.SendTokenRefreshErrorNotificationAsync(
                It.IsAny<string>(),
                It.IsAny<Exception?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _anafApiClientMock.Verify(
            x => x.RefreshTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CheckAndRefreshTokenIfNeededAsync_WhenRefreshIsNotNeeded_DoesNotTouchTheStoredTokens()
    {
        _tokenValidationServiceMock
            .Setup(x => x.ShouldRefreshToken(It.IsAny<string>(), It.IsAny<int>()))
            .Returns(false);

        StoreLatestSuccessfulLog(StoredRefreshToken);

        var result = await CreateService().CheckAndRefreshTokenIfNeededAsync();

        result.IsSuccess.Should().BeTrue();
        result.TokenWasRefreshed.Should().BeFalse();
        _callOrder.Should().BeEmpty();
    }

    [Fact]
    public async Task CheckAndRefreshTokenIfNeededAsync_DerivesRefreshTokenExpiryFromRefreshTokenExpiresIn()
    {
        StoreLatestSuccessfulLog(StoredRefreshToken);
        SetupApiResponse(RotatedRefreshToken, refreshTokenExpiresIn: 60 * 60 * 24 * 90);

        var before = DateTime.UtcNow;
        await CreateService().CheckAndRefreshTokenIfNeededAsync();

        CapturedSuccessfulLog().RefreshTokenExpiresAt
            .Should().BeCloseTo(before.AddDays(90), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task CheckAndRefreshTokenIfNeededAsync_WithoutRefreshTokenExpiryHint_FallsBackToOneYear()
    {
        StoreLatestSuccessfulLog(StoredRefreshToken);
        SetupApiResponse(RotatedRefreshToken);

        var before = DateTime.UtcNow;
        await CreateService().CheckAndRefreshTokenIfNeededAsync();

        CapturedSuccessfulLog().RefreshTokenExpiresAt
            .Should().BeCloseTo(before.AddDays(365), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task CheckAndRefreshTokenIfNeededAsync_WhenRefreshSucceeds_ArchivesTheRawAnafResponse()
    {
        StoreLatestSuccessfulLog(StoredRefreshToken);
        SetupApiResponse(RotatedRefreshToken);

        await CreateService().CheckAndRefreshTokenIfNeededAsync();

        _refreshResponseArchiveMock.Verify(
            x => x.SaveAsync(RawAnafResponse, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CheckAndRefreshTokenIfNeededAsync_ArchivesBeforeTouchingTheDatabase()
    {
        StoreLatestSuccessfulLog(StoredRefreshToken);
        SetupApiResponse(RotatedRefreshToken);

        _refreshResponseArchiveMock
            .Setup(x => x.SaveAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("archiwum.json")
            .Callback(() => _callOrder.Add("archive"));

        await CreateService().CheckAndRefreshTokenIfNeededAsync();

        _callOrder.Should().Equal("archive", "database-save", "config-backup", "config-update");
    }

    [Fact]
    public async Task CheckAndRefreshTokenIfNeededAsync_WhenArchiveFails_StillRefreshes()
    {
        StoreLatestSuccessfulLog(StoredRefreshToken);
        SetupApiResponse(RotatedRefreshToken);

        _refreshResponseArchiveMock
            .Setup(x => x.SaveAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException("brak dostepu do katalogu"));

        var result = await CreateService().CheckAndRefreshTokenIfNeededAsync();

        result.IsSuccess.Should().BeTrue("kopia na dysku nie moze byc wazniejsza od samego tokenu");
        CapturedSuccessfulLog().RefreshToken.Should().Be(RotatedRefreshToken);
    }

    [Fact]
    public async Task CheckAndRefreshTokenIfNeededAsync_WhenRefreshIsNotNeeded_ArchivesNothing()
    {
        _tokenValidationServiceMock
            .Setup(x => x.ShouldRefreshToken(It.IsAny<string>(), It.IsAny<int>()))
            .Returns(false);

        StoreLatestSuccessfulLog(StoredRefreshToken);

        await CreateService().CheckAndRefreshTokenIfNeededAsync();

        _refreshResponseArchiveMock.Verify(
            x => x.SaveAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CheckAndRefreshTokenIfNeededAsync_WhenRefreshIsNotNeeded_StillRecordsTheCheck()
    {
        _tokenValidationServiceMock
            .Setup(x => x.ShouldRefreshToken(It.IsAny<string>(), It.IsAny<int>()))
            .Returns(false);

        StoreLatestSuccessfulLog(StoredRefreshToken);

        await CreateService().CheckAndRefreshTokenIfNeededAsync();

        // To jest sedno naprawy: przebieg bez odswiezenia rowniez zostawia slad,
        // wiec pusta historia znaczy "serwis nie chodzil", a nie "nie bylo co robic".
        var check = _checkLogs.Single();
        check.Outcome.Should().Be(TokenCheckOutcome.NoRefreshNeeded);
        check.Trigger.Should().Be(TokenCheckTrigger.Scheduled);
        check.AccessTokenExpiresAt.Should().NotBeNull();
        _callOrder.Should().BeEmpty("token nie wymagal odswiezenia");
    }

    [Fact]
    public async Task CheckAndRefreshTokenIfNeededAsync_WhenRefreshSucceeds_RecordsRefreshedCheck()
    {
        StoreLatestSuccessfulLog(StoredRefreshToken);
        SetupApiResponse(RotatedRefreshToken);

        await CreateService().CheckAndRefreshTokenIfNeededAsync(TokenCheckTrigger.Manual);

        var check = _checkLogs.Single();
        check.Outcome.Should().Be(TokenCheckOutcome.Refreshed);
        check.Trigger.Should().Be(TokenCheckTrigger.Manual);
    }

    [Fact]
    public async Task CheckAndRefreshTokenIfNeededAsync_WhenRefreshFails_RecordsFailedCheck()
    {
        StoreLatestSuccessfulLog(StoredRefreshToken);

        _anafApiClientMock
            .Setup(x => x.RefreshTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("invalid_grant"));

        await CreateService().CheckAndRefreshTokenIfNeededAsync(TokenCheckTrigger.Startup);

        var check = _checkLogs.Single();
        check.Outcome.Should().Be(TokenCheckOutcome.Failed);
        check.Trigger.Should().Be(TokenCheckTrigger.Startup);
        check.Message.Should().Contain("invalid_grant");
    }

    [Fact]
    public async Task CheckAndRefreshTokenIfNeededAsync_WithUnreadableAccessToken_RecordsFailedCheck()
    {
        _tokenValidationServiceMock
            .Setup(x => x.GetExpirationDate(It.IsAny<string>()))
            .Returns((DateTime?)null);

        await CreateService().CheckAndRefreshTokenIfNeededAsync();

        _checkLogs.Single().Outcome.Should().Be(TokenCheckOutcome.Failed);
    }

    [Fact]
    public async Task CheckAndRefreshTokenIfNeededAsync_WhenCheckLogCannotBeWritten_StillRefreshes()
    {
        StoreLatestSuccessfulLog(StoredRefreshToken);
        SetupApiResponse(RotatedRefreshToken);

        _tokenRepositoryMock
            .Setup(x => x.AddTokenCheckLogAsync(It.IsAny<TokenCheckLog>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("historia niedostepna"));

        var result = await CreateService().CheckAndRefreshTokenIfNeededAsync();

        result.IsSuccess.Should().BeTrue("historia sprawdzen nie moze byc wazniejsza od samego tokenu");
        CapturedSuccessfulLog().RefreshToken.Should().Be(RotatedRefreshToken);
    }

    private TokenService CreateService() => new(
        _configFileServiceMock.Object,
        _tokenValidationServiceMock.Object,
        _anafApiClientMock.Object,
        _tokenRepositoryMock.Object,
        _emailNotificationServiceMock.Object,
        _refreshResponseArchiveMock.Object,
        Options.Create(CreateSettings()),
        Mock.Of<ILogger<TokenService>>());

    private void StoreLatestSuccessfulLog(string refreshToken) =>
        _tokenRepositoryMock
            .Setup(x => x.GetLatestSuccessfulLogAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TokenRefreshLog
            {
                Id = 7,
                RefreshToken = refreshToken,
                AccessToken = CurrentAccessToken,
                ExpiresAt = DateTime.UtcNow.AddDays(1),
                RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(200),
                CreatedAt = DateTime.UtcNow.AddDays(-89),
                IsSuccess = true
            });

    private void SetupApiResponse(string? refreshToken, int? refreshTokenExpiresIn = null) =>
        _anafApiClientMock
            .Setup(x => x.RefreshTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnafTokenResponse
            {
                AccessToken = NewAccessToken,
                ExpiresIn = 60 * 60 * 24 * 90,
                TokenType = "Bearer",
                Scope = "read",
                RefreshToken = refreshToken,
                RefreshTokenExpiresIn = refreshTokenExpiresIn,
                RawJson = RawAnafResponse
            });

    private List<TokenRefreshLog> CapturedLogs()
    {
        var logs = new List<TokenRefreshLog>();

        foreach (var invocation in _tokenRepositoryMock.Invocations)
        {
            if (invocation.Method.Name == nameof(ITokenRepository.AddTokenRefreshLogAsync))
            {
                logs.Add((TokenRefreshLog)invocation.Arguments[0]);
            }
        }

        return logs;
    }

    private TokenRefreshLog CapturedSuccessfulLog() => CapturedLogs().Single(log => log.IsSuccess);

    private static AnafSettings CreateSettings() => new()
    {
        TokenEndpoint = "https://logincert.anaf.ro/anaf-oauth2/v1/token",
        BasicAuth = new BasicAuthSettings { Username = "client", Password = "secret" },
        CheckSchedule = new CheckScheduleSettings { CheckHour = 12, CheckMinute = 0 },
        DaysBeforeExpiration = 3,
        ConfigFilePath = Path.Combine(Path.GetTempPath(), "config.ini"),
        BackupDirectory = Path.Combine(Path.GetTempPath(), "backups"),
        InitialRefreshToken = InitialRefreshToken
    };
}
