using AnafAutoToken.Core.Services;
using AnafAutoToken.Shared.Configuration;
using Microsoft.Extensions.Options;

namespace AnafAutoToken.Worker;

public class Worker(
    ILogger<Worker> logger,
    IServiceScopeFactory serviceScopeFactory,
    IOptions<AnafSettings> settings) : BackgroundService
{
    private readonly AnafSettings _settings = settings.Value;
    private readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(3600);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("ANAF Auto Token Worker starting at: {Time}", DateTimeOffset.Now);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await PerformTokenCheckAsync(stoppingToken);
                await WaitForNextCheckAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Worker service is stopping");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Fatal error in worker service");
            throw;
        }
    }

    private async Task PerformTokenCheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            var now = DateTime.Now;

            // Check if current time matches the scheduled check time
            if (now.Hour == _settings.CheckSchedule.CheckHour &&
                now.Minute == _settings.CheckSchedule.CheckMinute)
            {
                logger.LogInformation(
                    "Scheduled token check time reached: {Hour}:{Minute:D2}",
                    _settings.CheckSchedule.CheckHour,
                    _settings.CheckSchedule.CheckMinute);

                using var scope = serviceScopeFactory.CreateScope();
                var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
                await tokenService.CheckAndRefreshTokenIfNeededAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during token check operation");
            // Don't rethrow - we want the service to continue running
        }
    }

    private async Task WaitForNextCheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            logger.LogDebug("Waiting for {Interval} before next check", _checkInterval);
            await Task.Delay(_checkInterval, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Expected when service is stopping
            throw;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("ANAF Auto Token Worker is stopping at: {Time}", DateTimeOffset.Now);
        await base.StopAsync(cancellationToken);
    }
}
