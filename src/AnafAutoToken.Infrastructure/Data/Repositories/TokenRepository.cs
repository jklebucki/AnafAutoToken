using AnafAutoToken.Core.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AnafAutoToken.Infrastructure.Data.Repositories;

public class TokenRepository(AnafDbContext context, ILogger<TokenRepository> logger, IConfiguration configuration) : ITokenRepository
{
    public async Task<string?> GetLatestRefreshTokenAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var latestLog = await context.TokenRefreshLogs
                .Where(log => log.IsSuccess)
                .OrderByDescending(log => log.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (latestLog != null)
            {
                return latestLog.RefreshToken;
            }

            // If no token in database, get from appsettings
            var defaultToken = configuration["Anaf:InitialRefreshToken"];
            return string.IsNullOrEmpty(defaultToken) ? null : defaultToken;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving latest refresh token from database");
            throw;
        }
    }

    public async Task AddTokenRefreshLogAsync(TokenRefreshLog log, CancellationToken cancellationToken = default)
    {
        try
        {
            await context.TokenRefreshLogs.AddAsync(log, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Token refresh log added. Success: {IsSuccess}, ExpiresAt: {ExpiresAt}",
                log.IsSuccess,
                log.ExpiresAt);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error adding token refresh log to database");
            throw;
        }
    }

    public async Task<TokenRefreshLog?> GetLatestSuccessfulLogAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await context.TokenRefreshLogs
                .Where(log => log.IsSuccess)
                .OrderByDescending(log => log.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving latest successful log from database");
            throw;
        }
    }
}
