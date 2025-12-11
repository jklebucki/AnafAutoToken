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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("ANAF Auto Token Worker starting at: {Time}", DateTimeOffset.Now);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var delayUntilNextCheck = CalculateDelayUntilNextCheck();
                await WaitForNextCheckAsync(delayUntilNextCheck, stoppingToken);
                
                if (!stoppingToken.IsCancellationRequested)
                {
                    await PerformTokenCheckAsync(stoppingToken);
                }
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

    private TimeSpan CalculateDelayUntilNextCheck()
    {
        var now = DateTime.Now;
        var scheduledTime = new DateTime(
            now.Year,
            now.Month,
            now.Day,
            _settings.CheckSchedule.CheckHour,
            _settings.CheckSchedule.CheckMinute,
            0);

        // If scheduled time has already passed today, schedule for tomorrow
        if (scheduledTime <= now)
        {
            scheduledTime = scheduledTime.AddDays(1);
        }

        var delay = scheduledTime - now;
        
        logger.LogInformation(
            "Next token check scheduled for: {ScheduledTime} (in {Hours}h {Minutes}m)",
            scheduledTime,
            (int)delay.TotalHours,
            delay.Minutes);

        return delay;
    }

    private async Task PerformTokenCheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation(
                "Scheduled token check time reached: {Hour}:{Minute:D2}",
                _settings.CheckSchedule.CheckHour,
                _settings.CheckSchedule.CheckMinute);

            using var scope = serviceScopeFactory.CreateScope();
            var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
            await tokenService.CheckAndRefreshTokenIfNeededAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during token check operation");
            // Don't rethrow - we want the service to continue running
        }
    }

    private async Task WaitForNextCheckAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
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
