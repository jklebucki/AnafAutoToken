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
    IEmailNotificationService emailNotificationService,
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
            
            return TokenRefreshResult.Failure(ex.Message, ex);
        }
    }
}
