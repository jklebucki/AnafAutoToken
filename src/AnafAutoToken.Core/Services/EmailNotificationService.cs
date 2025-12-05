using AnafAutoToken.Shared.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Mail;

namespace AnafAutoToken.Core.Services;

public class EmailNotificationService : IEmailNotificationService
{
    private readonly EmailSettings? _emailSettings;
    private readonly ILogger<EmailNotificationService> _logger;

    public EmailNotificationService(
        IOptions<AnafSettings> settings,
        ILogger<EmailNotificationService> logger)
    {
        _emailSettings = settings.Value.Email;
        _logger = logger;
    }

    public async Task SendTokenRefreshSuccessNotificationAsync(
        DateTime newExpirationDate,
        CancellationToken cancellationToken = default)
    {
        if (!IsEmailConfigured())
        {
            _logger.LogDebug("Email notifications are not configured. Skipping success notification.");
            return;
        }

        var subject = "ANAF Token - Pomyœlna aktualizacja tokena";
        var body = $@"
<html>
<body>
<h2>Token ANAF zosta³ pomyœlnie zaktualizowany</h2>
<p>Data i czas aktualizacji: <strong>{DateTime.Now:yyyy-MM-dd HH:mm:ss}</strong></p>
<p>Nowa data wygaœniêcia tokena: <strong>{newExpirationDate:yyyy-MM-dd HH:mm:ss}</strong></p>
<hr/>
<p><em>Wiadomoœæ wygenerowana automatycznie przez ANAF Auto Token Service</em></p>
</body>
</html>";

        await SendEmailAsync(subject, body, cancellationToken);
    }

    public async Task SendTokenRefreshErrorNotificationAsync(
        string errorMessage,
        Exception? exception = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsEmailConfigured())
        {
            _logger.LogDebug("Email notifications are not configured. Skipping error notification.");
            return;
        }

        var subject = "ANAF Token - B£¥D aktualizacji tokena";
        var exceptionDetails = exception != null
            ? $@"
<h3>Szczegó³y wyj¹tku:</h3>
<pre>{exception.GetType().FullName}: {exception.Message}

{exception.StackTrace}</pre>"
            : string.Empty;

        var body = $@"
<html>
<body>
<h2 style='color: red;'>Wyst¹pi³ b³¹d podczas aktualizacji tokena ANAF</h2>
<p>Data i czas b³êdu: <strong>{DateTime.Now:yyyy-MM-dd HH:mm:ss}</strong></p>
<h3>Komunikat b³êdu:</h3>
<p style='color: red;'>{errorMessage}</p>
{exceptionDetails}
<hr/>
<p><strong>Wymagana jest natychmiastowa interwencja!</strong></p>
<p><em>Wiadomoœæ wygenerowana automatycznie przez ANAF Auto Token Service</em></p>
</body>
</html>";

        await SendEmailAsync(subject, body, cancellationToken);
    }

    private bool IsEmailConfigured()
    {
        return _emailSettings != null
            && !string.IsNullOrEmpty(_emailSettings.SmtpServer)
            && !string.IsNullOrEmpty(_emailSettings.FromAddress)
            && _emailSettings.ToAddresses?.Length > 0;
    }

    private async Task SendEmailAsync(string subject, string body, CancellationToken cancellationToken)
    {
        if (_emailSettings == null)
            return;

        try
        {
            using var smtpClient = new SmtpClient(_emailSettings.SmtpServer, _emailSettings.SmtpPort)
            {
                Credentials = new NetworkCredential(_emailSettings.Username, _emailSettings.Password),
                EnableSsl = _emailSettings.EnableSsl
            };

            using var mailMessage = new MailMessage
            {
                From = new MailAddress(_emailSettings.FromAddress, _emailSettings.FromName),
                Subject = subject,
                Body = body,
                IsBodyHtml = true
            };

            foreach (var toAddress in _emailSettings.ToAddresses)
            {
                mailMessage.To.Add(toAddress);
            }

            await smtpClient.SendMailAsync(mailMessage, cancellationToken);
            _logger.LogInformation("Email notification sent successfully. Subject: {Subject}", subject);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email notification. Subject: {Subject}", subject);
            // Don't rethrow - email failure shouldn't stop the service
        }
    }
}
