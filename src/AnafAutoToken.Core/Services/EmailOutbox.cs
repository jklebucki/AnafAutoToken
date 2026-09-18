using Microsoft.Extensions.Logging;
using System.Threading.Channels;

namespace AnafAutoToken.Core.Services;

/// <summary>
/// Kolejka powiadomień wysyłanych w tle. Serwer SMTP potrafi chwilowo nie odpowiadać,
/// a ponowienia co kilka minut nie mogą trzymać odświeżania tokenu - blokowałyby
/// koordynator i ręczne odświeżenie z menedżera, które ma kilkuminutowy timeout.
/// </summary>
public sealed class EmailOutbox : IEmailOutbox
{
    public const int MaxRetries = 3;
    public static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromMinutes(5);

    private readonly Channel<EmailMessage> _queue = Channel.CreateUnbounded<EmailMessage>(
        new UnboundedChannelOptions { SingleReader = true });

    private readonly IEmailSender _sender;
    private readonly ILogger<EmailOutbox> _logger;
    private readonly TimeSpan _retryDelay;

    public EmailOutbox(IEmailSender sender, ILogger<EmailOutbox> logger)
        : this(sender, logger, DefaultRetryDelay)
    {
    }

    internal EmailOutbox(IEmailSender sender, ILogger<EmailOutbox> logger, TimeSpan retryDelay)
    {
        _sender = sender;
        _logger = logger;
        _retryDelay = retryDelay;
    }

    public void Enqueue(EmailMessage message)
    {
        if (!_queue.Writer.TryWrite(message))
        {
            throw new InvalidOperationException("Email outbox is closed");
        }
    }

    /// <summary>Przetwarza kolejkę aż do zatrzymania usługi.</summary>
    public async Task ProcessAsync(CancellationToken cancellationToken)
    {
        await foreach (var message in _queue.Reader.ReadAllAsync(cancellationToken))
        {
            await DeliverAsync(message, cancellationToken);
        }
    }

    /// <summary>
    /// Pierwsza próba plus <see cref="MaxRetries"/> ponowień. Błąd z pełnym wyjątkiem
    /// trafia do logu dopiero po ostatniej nieudanej próbie.
    /// </summary>
    internal async Task DeliverAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        for (var retry = 0; ; retry++)
        {
            try
            {
                await _sender.SendAsync(message, cancellationToken);
                _logger.LogInformation("Email notification sent successfully. Subject: {Subject}", message.Subject);
                return;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                if (retry >= MaxRetries)
                {
                    _logger.LogError(
                        ex,
                        "Failed to send email notification after {Attempts} attempts. Subject: {Subject}",
                        retry + 1,
                        message.Subject);
                    return;
                }

                _logger.LogWarning(
                    "Sending email notification failed ({Reason}). Retry {Retry} of {MaxRetries} in {Delay}. Subject: {Subject}",
                    ex.InnerException?.Message ?? ex.Message,
                    retry + 1,
                    MaxRetries,
                    _retryDelay,
                    message.Subject);
            }

            await Task.Delay(_retryDelay, cancellationToken);
        }
    }
}
