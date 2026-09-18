using AnafAutoToken.Core.Services;

namespace AnafAutoToken.Worker;

/// <summary>Wysyła w tle powiadomienia zakolejkowane w <see cref="EmailOutbox"/>.</summary>
public sealed class EmailDeliveryService(EmailOutbox outbox, ILogger<EmailDeliveryService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await outbox.ProcessAsync(stoppingToken);
        }
        catch (Exception) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Email delivery is stopping");
        }
        catch (Exception ex)
        {
            // Awaria wysyłki nie może zatrzymać całego hosta razem z odświeżaniem tokenu.
            logger.LogError(ex, "Email delivery stopped unexpectedly");
        }
    }
}
