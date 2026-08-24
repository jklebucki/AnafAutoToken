using AnafAutoToken.Core.Interfaces;
using AnafAutoToken.Core.Services;
using AnafAutoToken.Shared.Configuration;
using Microsoft.Extensions.Options;

namespace AnafAutoToken.Worker;

public class Worker(
    ILogger<Worker> logger,
    IServiceScopeFactory serviceScopeFactory,
    TokenRefreshCoordinator refreshCoordinator,
    IOptions<AnafSettings> settings) : BackgroundService
{
    /// <summary>
    /// Krótki krok pętli zamiast jednego długiego Task.Delay: przetrwa uśpienie maszyny,
    /// zmianę czasu i zmianę godziny sprawdzania bez restartu usługi.
    /// </summary>
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);

    private readonly AnafSettings _settings = settings.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("ANAF Auto Token Worker starting at: {Time}", DateTimeOffset.Now);

        try
        {
            var lastCheckDate = await LoadLastCheckDateAsync(stoppingToken);

            // Nadrobienie pominiętego terminu. Poprzednia wersja przy starcie po godzinie
            // sprawdzania planowała następny przebieg na jutro, więc każde wdrożenie czy
            // restart maszyny po tej godzinie oznaczały dzień bez sprawdzenia tokenu.
            if (ShouldRunNow(DateTime.Now, lastCheckDate))
            {
                logger.LogInformation(
                    "Scheduled time for today has already passed and no check is recorded for today - running it now");

                await PerformTokenCheckAsync(TokenCheckTrigger.Startup, stoppingToken);
                lastCheckDate = DateOnly.FromDateTime(DateTime.Now);
            }

            LogNextCheck(DateTime.Now, lastCheckDate);

            using var timer = new PeriodicTimer(TickInterval);

            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                var now = DateTime.Now;

                if (!ShouldRunNow(now, lastCheckDate))
                {
                    continue;
                }

                await PerformTokenCheckAsync(TokenCheckTrigger.Scheduled, stoppingToken);
                lastCheckDate = DateOnly.FromDateTime(now);
                LogNextCheck(DateTime.Now, lastCheckDate);
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

    private TimeSpan ScheduledTimeOfDay =>
        new(_settings.CheckSchedule.CheckHour, _settings.CheckSchedule.CheckMinute, 0);

    private bool ShouldRunNow(DateTime now, DateOnly lastCheckDate) =>
        DateOnly.FromDateTime(now) > lastCheckDate && now.TimeOfDay >= ScheduledTimeOfDay;

    /// <summary>
    /// Data ostatniego przebiegu pochodzi z bazy, więc restart usługi nie powoduje
    /// ponownego sprawdzania tego samego dnia. Ręczne odświeżenie też się liczy - zrobiło
    /// dokładnie tę samą pracę co przebieg zaplanowany.
    /// </summary>
    private async Task<DateOnly> LoadLastCheckDateAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = serviceScopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<ITokenRepository>();
            var latestCheck = await repository.GetLatestCheckLogAsync(cancellationToken);

            if (latestCheck is null)
            {
                logger.LogInformation("No previous token check is recorded in the database");
                return DateOnly.MinValue;
            }

            var checkedAtLocal = DateTime.SpecifyKind(latestCheck.CheckedAt, DateTimeKind.Utc).ToLocalTime();

            logger.LogInformation(
                "Last recorded token check: {CheckedAt} ({Outcome}, {Trigger})",
                checkedAtLocal,
                latestCheck.Outcome,
                latestCheck.Trigger);

            return DateOnly.FromDateTime(checkedAtLocal);
        }
        catch (Exception ex)
        {
            // Brak historii nie może zablokować harmonogramu - w najgorszym razie
            // wykonamy jedno sprawdzenie więcej.
            logger.LogError(ex, "Could not read the last token check from the database");
            return DateOnly.MinValue;
        }
    }

    private void LogNextCheck(DateTime now, DateOnly lastCheckDate)
    {
        var today = DateOnly.FromDateTime(now);

        var next = today > lastCheckDate && now.TimeOfDay < ScheduledTimeOfDay
            ? now.Date.Add(ScheduledTimeOfDay)
            : now.Date.AddDays(1).Add(ScheduledTimeOfDay);

        var delay = next - now;

        logger.LogInformation(
            "Next token check scheduled for: {ScheduledTime} (in {Hours}h {Minutes}m)",
            next,
            (int)delay.TotalHours,
            delay.Minutes);
    }

    private async Task PerformTokenCheckAsync(TokenCheckTrigger trigger, CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation(
                "Running token check ({Trigger}). Scheduled time: {Hour}:{Minute:D2}",
                trigger,
                _settings.CheckSchedule.CheckHour,
                _settings.CheckSchedule.CheckMinute);

            if (refreshCoordinator.IsBusy)
            {
                logger.LogInformation("A manual token refresh is in progress - waiting for it to finish");
            }

            // Przez koordynator, żeby nie wejść w paradę ręcznemu odświeżeniu z menedżera.
            await refreshCoordinator.RunAsync(
                async token =>
                {
                    using var scope = serviceScopeFactory.CreateScope();
                    var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
                    await tokenService.CheckAndRefreshTokenIfNeededAsync(trigger, token);
                },
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during token check operation");
            // Don't rethrow - we want the service to continue running
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("ANAF Auto Token Worker is stopping at: {Time}", DateTimeOffset.Now);
        await base.StopAsync(cancellationToken);
    }
}
