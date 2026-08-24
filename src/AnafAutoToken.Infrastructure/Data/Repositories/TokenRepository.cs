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
            var latestLog = await LatestSuccessfulQuery()
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
                "Token refresh log added. Id: {Id}, Success: {IsSuccess}, ExpiresAt: {ExpiresAt}, RefreshTokenExpiresAt: {RefreshTokenExpiresAt}",
                log.Id,
                log.IsSuccess,
                log.ExpiresAt,
                log.RefreshTokenExpiresAt);
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
            return await LatestSuccessfulQuery()
                .FirstOrDefaultAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving latest successful log from database");
            throw;
        }
    }

    public async Task AddTokenCheckLogAsync(TokenCheckLog log, CancellationToken cancellationToken = default)
    {
        try
        {
            await context.TokenCheckLogs.AddAsync(log, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Token check log added. Id: {Id}, Outcome: {Outcome}, Trigger: {Trigger}",
                log.Id,
                log.Outcome,
                log.Trigger);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error adding token check log to database");
            throw;
        }
    }

    public async Task<TokenCheckLog?> GetLatestCheckLogAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await context.TokenCheckLogs
                .OrderByDescending(log => log.CheckedAt)
                .ThenByDescending(log => log.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving latest token check log from database");
            throw;
        }
    }

    // Rows written in the same tick would otherwise tie on CreatedAt, so the identity
    // column decides which one is really the newest. Blank refresh tokens are skipped
    // because they cannot be used to refresh anything.
    private IQueryable<TokenRefreshLog> LatestSuccessfulQuery() =>
        context.TokenRefreshLogs
            .Where(log => log.IsSuccess && log.RefreshToken != null && log.RefreshToken.Trim() != string.Empty)
            .OrderByDescending(log => log.CreatedAt)
            .ThenByDescending(log => log.Id);
}
