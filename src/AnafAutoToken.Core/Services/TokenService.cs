using AnafAutoToken.Core.Interfaces;
using AnafAutoToken.Core.Models;
using AnafAutoToken.Shared.Configuration;
using AnafAutoToken.Shared.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AnafAutoToken.Core.Services;

public class TokenService(
    IConfigFileService configFileService,
    ITokenValidationService tokenValidationService,
    IAnafApiClient anafApiClient,
    ITokenRepository tokenRepository,
    IEmailNotificationService emailNotificationService,
    IOptions<AnafSettings> settings,
    ILogger<TokenService> logger) : ITokenService
{
    private const int RefreshTokenExpiryWarningDays = 30;
    private static readonly TimeSpan DefaultRefreshTokenLifetime = TimeSpan.FromDays(365);

    private readonly AnafSettings _settings = settings.Value;

    public async Task<TokenRefreshResult> CheckAndRefreshTokenIfNeededAsync(
        TokenCheckTrigger trigger = TokenCheckTrigger.Scheduled,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation("Starting token check and refresh process");

            // Read current access token from config.ini
            var currentAccessToken = await configFileService.ReadAccessTokenAsync(cancellationToken);
            logger.LogInformation("Current access token read from config file");

            // ShouldRefreshToken cannot tell a token that is far from expiring apart from
            // one that cannot be parsed at all - both come back as false. Checking the
            // expiration date up front stops an unreadable token from being reported as
            // healthy forever.
            var currentExpiration = tokenValidationService.GetExpirationDate(currentAccessToken);

            if (!currentExpiration.HasValue)
            {
                return await ReportUnreadableAccessTokenAsync(trigger, cancellationToken);
            }

            // Check if token needs refresh
            if (!tokenValidationService.ShouldRefreshToken(currentAccessToken, _settings.DaysBeforeExpiration))
            {
                var expirationDate = currentExpiration.Value;
                logger.LogInformation(
                    "Token does not need refresh yet. Expires at: {ExpirationDate}",
                    expirationDate);

                // The access token is fine, but the refresh token has its own lifetime -
                // if it dies unnoticed the service can never refresh again.
                var storedRefreshTokenExpiry = await LogStoredRefreshTokenHealthAsync(cancellationToken);

                await RecordCheckAsync(
                    trigger,
                    TokenCheckOutcome.NoRefreshNeeded,
                    expirationDate,
                    storedRefreshTokenExpiry,
                    "Access token jest jeszcze wazny - odswiezenie nie bylo potrzebne.",
                    cancellationToken);

                // Calculate days until expiration and days until the system will attempt refresh
                var now = DateTime.UtcNow;
                var daysUntilExpirationDouble = (expirationDate - now).TotalDays;

                // Days until the system will attempt refresh = daysUntilExpiration - DaysBeforeExpirationFromConfig
                var daysUntilRefresh = (int)Math.Max(0, Math.Ceiling(daysUntilExpirationDouble - _settings.DaysBeforeExpiration));

                // Send no-refresh-needed notification with computed values
                try
                {
                    logger.LogInformation("Sending no refresh needed notification email");
                    await emailNotificationService.SendTokenNoRefreshNeededNotificationAsync(
                        expirationDate,
                        daysUntilRefresh,
                        cancellationToken);
                    logger.LogInformation("No refresh needed notification email sent successfully");
                }
                catch (Exception emailEx)
                {
                    logger.LogError(emailEx, "Failed to send no refresh needed notification email");
                }

                return TokenRefreshResult.NoRefreshNeeded(expirationDate);
            }

            logger.LogInformation("Token needs refresh. Proceeding with refresh process");

            // Get the latest refresh token from database
            var latestLog = await tokenRepository.GetLatestSuccessfulLogAsync(cancellationToken);
            var refreshToken = latestLog?.RefreshToken;

            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                // If no refresh token in database, use initial refresh token from configuration
                refreshToken = _settings.InitialRefreshToken;

                if (string.IsNullOrWhiteSpace(refreshToken))
                {
                    var errorMessage = "No refresh token available in database or configuration";
                    logger.LogError("No refresh token available. Cannot proceed with token refresh");

                    // Send error notification
                    try
                    {
                        logger.LogInformation("Sending error notification email: No refresh token available");
                        await emailNotificationService.SendTokenRefreshErrorNotificationAsync(
                            errorMessage,
                            null,
                            cancellationToken);
                        logger.LogInformation("Error notification email sent successfully");
                    }
                    catch (Exception emailEx)
                    {
                        logger.LogError(emailEx, "Failed to send error notification email for missing refresh token");
                    }

                    await RecordCheckAsync(
                        trigger,
                        TokenCheckOutcome.Failed,
                        currentExpiration,
                        null,
                        errorMessage,
                        cancellationToken);

                    return TokenRefreshResult.Failure(errorMessage);
                }

                logger.LogWarning("Using initial refresh token from configuration");
            }
            else
            {
                logger.LogInformation(
                    "Using refresh token stored by refresh log {LogId} saved at {SavedAt}",
                    latestLog!.Id,
                    latestLog.CreatedAt);

                if (latestLog.RefreshTokenExpiresAt is { } storedRefreshTokenExpiry
                    && storedRefreshTokenExpiry <= DateTime.UtcNow)
                {
                    logger.LogError(
                        "The stored refresh token expired at {RefreshTokenExpiresAt}. The refresh call will most likely be rejected by ANAF",
                        storedRefreshTokenExpiry);
                }
            }

            // Call ANAF API to refresh token
            AnafTokenResponse? tokenResponse = null;
            Exception? apiException = null;

            try
            {
                tokenResponse = await anafApiClient.RefreshTokenAsync(refreshToken, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to refresh token via ANAF API");
                apiException = ex;

                // Send error notification for API failure
                try
                {
                    logger.LogInformation("Sending error notification email: ANAF API failure");
                    await emailNotificationService.SendTokenRefreshErrorNotificationAsync(
                        "Failed to refresh token via ANAF API",
                        ex,
                        cancellationToken);
                    logger.LogInformation("Error notification email sent successfully");
                }
                catch (Exception emailEx)
                {
                    logger.LogError(emailEx, "Failed to send error notification email for ANAF API failure");
                }
            }

            if (tokenResponse != null)
            {
                var createdAt = DateTime.UtcNow;
                var expiresAt = createdAt.AddSeconds(tokenResponse.ExpiresIn);
                var newRefreshToken = ResolveNewRefreshToken(tokenResponse.RefreshToken, refreshToken);
                var refreshTokenExpiresAt = ResolveRefreshTokenExpiration(
                    newRefreshToken,
                    tokenResponse.RefreshTokenExpiresIn,
                    createdAt);

                var log = new TokenRefreshLog
                {
                    RefreshToken = newRefreshToken,
                    AccessToken = tokenResponse.AccessToken,
                    ExpiresAt = expiresAt,
                    RefreshTokenExpiresAt = refreshTokenExpiresAt,
                    CreatedAt = createdAt,
                    IsSuccess = true,
                    ResponseStatusCode = 200
                };

                // Persist before touching config.ini. ANAF invalidates the previous refresh
                // token once a rotated one is issued, and the database is the only place the
                // new one is kept - losing it here would leave the service unable to refresh
                // ever again, while a stale access token in config.ini is recoverable on the
                // next run.
                try
                {
                    await tokenRepository.AddTokenRefreshLogAsync(log, cancellationToken);
                }
                catch (Exception dbEx)
                {
                    logger.LogError(
                        dbEx,
                        "Failed to persist the refreshed tokens to the database. Attempting to write the new access token to the config file so the issued token is not wasted");

                    try
                    {
                        await configFileService.UpdateAccessTokenAsync(tokenResponse.AccessToken, cancellationToken);
                        logger.LogWarning("New access token written to the config file, but the rotated refresh token could not be stored");
                    }
                    catch (Exception configEx)
                    {
                        logger.LogError(configEx, "Failed to write the new access token to the config file after the database error");
                    }

                    throw;
                }

                // Create backup of current config.ini
                await configFileService.CreateBackupAsync(cancellationToken);
                logger.LogInformation("Config file backed up successfully");

                // Update config.ini with new access token
                await configFileService.UpdateAccessTokenAsync(
                    tokenResponse.AccessToken,
                    cancellationToken);
                logger.LogInformation("Config file updated with new access token");

                logger.LogInformation(
                    "Token refresh completed successfully. New token expires at: {ExpiresAt}, refresh token valid until: {RefreshTokenExpiresAt}",
                    expiresAt,
                    refreshTokenExpiresAt);

                // Send success notification
                try
                {
                    logger.LogInformation("Sending success notification email: Token refreshed successfully");
                    await emailNotificationService.SendTokenRefreshSuccessNotificationAsync(
                        expiresAt,
                        cancellationToken);
                    logger.LogInformation("Success notification email sent successfully");
                }
                catch (Exception emailEx)
                {
                    logger.LogError(emailEx, $"Failed to send success notification email. Token was refreshed successfully but email notification failed. {emailEx.InnerException?.Message}");
                }

                await RecordCheckAsync(
                    trigger,
                    TokenCheckOutcome.Refreshed,
                    expiresAt,
                    refreshTokenExpiresAt,
                    "Token odswiezony, nowy refresh token zapisany w bazie.",
                    cancellationToken);

                return TokenRefreshResult.Success(expiresAt);
            }
            else
            {
                // Log failed attempt
                var errorMessage = apiException?.Message ?? "Unknown error during token refresh";

                var failedLog = new TokenRefreshLog
                {
                    RefreshToken = refreshToken,
                    AccessToken = string.Empty,
                    ExpiresAt = DateTime.UtcNow,
                    RefreshTokenExpiresAt = latestLog?.RefreshTokenExpiresAt ?? refreshToken.GetExpirationDate(),
                    CreatedAt = DateTime.UtcNow,
                    IsSuccess = false,
                    ErrorMessage = errorMessage,
                    ResponseStatusCode = null
                };

                await tokenRepository.AddTokenRefreshLogAsync(failedLog, cancellationToken);

                logger.LogError("Token refresh failed and logged to database");

                await RecordCheckAsync(
                    trigger,
                    TokenCheckOutcome.Failed,
                    currentExpiration,
                    failedLog.RefreshTokenExpiresAt,
                    errorMessage,
                    cancellationToken);

                return TokenRefreshResult.Failure(errorMessage, apiException);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in token check and refresh process");

            // Send error notification for unexpected errors
            try
            {
                logger.LogInformation("Sending error notification email: Unexpected error in token refresh process");
                await emailNotificationService.SendTokenRefreshErrorNotificationAsync(
                    "Unexpected error in token check and refresh process",
                    ex,
                    cancellationToken);
                logger.LogInformation("Error notification email sent successfully");
            }
            catch (Exception emailEx)
            {
                logger.LogError(emailEx, "Failed to send error notification email for unexpected error");
            }

            await RecordCheckAsync(trigger, TokenCheckOutcome.Failed, null, null, ex.Message, cancellationToken);

            return TokenRefreshResult.Failure(ex.Message, ex);
        }
    }

    private string ResolveNewRefreshToken(string? returnedRefreshToken, string usedRefreshToken)
    {
        if (string.IsNullOrWhiteSpace(returnedRefreshToken))
        {
            logger.LogWarning(
                "ANAF did not return a refresh token. Keeping the refresh token that was used for this call");
            return usedRefreshToken;
        }

        var newRefreshToken = returnedRefreshToken.Trim();

        if (string.Equals(newRefreshToken, usedRefreshToken, StringComparison.Ordinal))
        {
            logger.LogWarning("ANAF returned the same refresh token that was sent - no rotation happened");
        }
        else
        {
            logger.LogInformation("ANAF returned a rotated refresh token. Storing it as the current refresh token");
        }

        return newRefreshToken;
    }

    private static DateTime ResolveRefreshTokenExpiration(
        string refreshToken,
        int? refreshTokenExpiresIn,
        DateTime createdAt)
    {
        var fromToken = refreshToken.GetExpirationDate();

        if (fromToken.HasValue)
        {
            return fromToken.Value;
        }

        return refreshTokenExpiresIn.HasValue
            ? createdAt.AddSeconds(refreshTokenExpiresIn.Value)
            : createdAt.Add(DefaultRefreshTokenLifetime);
    }

    private async Task<DateTime?> LogStoredRefreshTokenHealthAsync(CancellationToken cancellationToken)
    {
        try
        {
            var latestLog = await tokenRepository.GetLatestSuccessfulLogAsync(cancellationToken);

            if (latestLog is null)
            {
                logger.LogWarning(
                    "No successful refresh is stored in the database yet - the initial refresh token from configuration will be used for the next refresh");
                return null;
            }

            if (latestLog.RefreshTokenExpiresAt is not { } refreshTokenExpiresAt)
            {
                return null;
            }

            var daysLeft = (refreshTokenExpiresAt - DateTime.UtcNow).TotalDays;

            if (daysLeft <= 0)
            {
                logger.LogError(
                    "The stored refresh token expired at {RefreshTokenExpiresAt}. A new authorization is required",
                    refreshTokenExpiresAt);
            }
            else if (daysLeft <= RefreshTokenExpiryWarningDays)
            {
                logger.LogWarning(
                    "The stored refresh token expires at {RefreshTokenExpiresAt} (in {DaysLeft} days)",
                    refreshTokenExpiresAt,
                    (int)daysLeft);
            }
            else
            {
                logger.LogInformation(
                    "Stored refresh token is valid until {RefreshTokenExpiresAt}",
                    refreshTokenExpiresAt);
            }

            return refreshTokenExpiresAt;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to inspect the stored refresh token");
            return null;
        }
    }

    /// <summary>
    /// Slad po przebiegu. Nigdy nie przerywa odswiezania - historia sprawdzen jest
    /// wartosciowa, ale nie wazniejsza od samego tokenu.
    /// </summary>
    private async Task RecordCheckAsync(
        TokenCheckTrigger trigger,
        TokenCheckOutcome outcome,
        DateTime? accessTokenExpiresAt,
        DateTime? refreshTokenExpiresAt,
        string? message,
        CancellationToken cancellationToken)
    {
        try
        {
            await tokenRepository.AddTokenCheckLogAsync(
                new TokenCheckLog
                {
                    CheckedAt = DateTime.UtcNow,
                    Outcome = outcome,
                    Trigger = trigger,
                    AccessTokenExpiresAt = accessTokenExpiresAt,
                    RefreshTokenExpiresAt = refreshTokenExpiresAt,
                    Message = Truncate(message, 1000)
                },
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to record the token check in the database");
        }
    }

    private static string? Truncate(string? value, int maxLength) =>
        value is not null && value.Length > maxLength ? value[..maxLength] : value;

    private async Task<TokenRefreshResult> ReportUnreadableAccessTokenAsync(
        TokenCheckTrigger trigger,
        CancellationToken cancellationToken)
    {
        const string errorMessage = "The access token in the config file could not be parsed, so its expiration date is unknown";

        logger.LogError(
            "Access token from {ConfigFilePath} is not a readable JWT. Token expiration cannot be evaluated",
            _settings.ConfigFilePath);

        try
        {
            await emailNotificationService.SendTokenRefreshErrorNotificationAsync(
                errorMessage,
                null,
                cancellationToken);
        }
        catch (Exception emailEx)
        {
            logger.LogError(emailEx, "Failed to send error notification email for unreadable access token");
        }

        await RecordCheckAsync(trigger, TokenCheckOutcome.Failed, null, null, errorMessage, cancellationToken);

        return TokenRefreshResult.Failure(errorMessage);
    }
}
