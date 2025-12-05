using AnafAutoToken.Core.Interfaces;
using AnafAutoToken.Core.Models;
using AnafAutoToken.Shared.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AnafAutoToken.Core.Services;

public class TokenService(
    IConfigFileService configFileService,
    ITokenValidationService tokenValidationService,
    IAnafApiClient anafApiClient,
    ITokenRepository tokenRepository,
    IOptions<AnafSettings> settings,
    ILogger<TokenService> logger) : ITokenService
{
    private readonly AnafSettings _settings = settings.Value;

    public async Task<TokenRefreshResult> CheckAndRefreshTokenIfNeededAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation("Starting token check and refresh process");

            // Read current access token from config.ini
            var currentAccessToken = await configFileService.ReadAccessTokenAsync(cancellationToken);
            logger.LogInformation("Current access token read from config file");

            // Check if token needs refresh
            if (!tokenValidationService.ShouldRefreshToken(currentAccessToken, _settings.DaysBeforeExpiration))
            {
                var expirationDate = tokenValidationService.GetExpirationDate(currentAccessToken);
                logger.LogInformation(
                    "Token does not need refresh yet. Expires at: {ExpirationDate}",
                    expirationDate);
                return TokenRefreshResult.NoRefreshNeeded(expirationDate ?? DateTime.MaxValue);
            }

            logger.LogInformation("Token needs refresh. Proceeding with refresh process");

            // Get the latest refresh token from database
            var refreshToken = await tokenRepository.GetLatestRefreshTokenAsync(cancellationToken);

            if (string.IsNullOrEmpty(refreshToken))
            {
                // If no refresh token in database, use initial refresh token from configuration
                refreshToken = _settings.InitialRefreshToken;

                if (string.IsNullOrEmpty(refreshToken))
                {
                    var errorMessage = "No refresh token available in database or configuration";
                    logger.LogError("No refresh token available. Cannot proceed with token refresh");
                    return TokenRefreshResult.Failure(errorMessage);
                }

                logger.LogWarning("Using initial refresh token from configuration");
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
            }

            if (tokenResponse != null)
            {
                // Create backup of current config.ini
                await configFileService.CreateBackupAsync(cancellationToken);
                logger.LogInformation("Config file backed up successfully");

                // Update config.ini with new access token
                await configFileService.UpdateAccessTokenAsync(
                    tokenResponse.AccessToken,
                    cancellationToken);
                logger.LogInformation("Config file updated with new access token");

                // Calculate expiration date
                var expiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn);

                // Save to database
                var log = new TokenRefreshLog
                {
                    RefreshToken = tokenResponse.RefreshToken,
                    AccessToken = tokenResponse.AccessToken,
                    ExpiresAt = expiresAt,
                    CreatedAt = DateTime.UtcNow,
                    IsSuccess = true,
                    ResponseStatusCode = 200
                };

                await tokenRepository.AddTokenRefreshLogAsync(log, cancellationToken);

                logger.LogInformation(
                    "Token refresh completed successfully. New token expires at: {ExpiresAt}",
                    expiresAt);

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
                    CreatedAt = DateTime.UtcNow,
                    IsSuccess = false,
                    ErrorMessage = errorMessage,
                    ResponseStatusCode = null
                };

                await tokenRepository.AddTokenRefreshLogAsync(failedLog, cancellationToken);

                logger.LogError("Token refresh failed and logged to database");

                return TokenRefreshResult.Failure(errorMessage, apiException);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in token check and refresh process");
            return TokenRefreshResult.Failure(ex.Message, ex);
        }
    }
}
