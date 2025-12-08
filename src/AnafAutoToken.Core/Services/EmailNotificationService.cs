using AnafAutoToken.Shared.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Reflection;

namespace AnafAutoToken.Core.Services;

public class EmailNotificationService : IEmailNotificationService
{
    private readonly EmailSettings? _emailSettings;
    private readonly ILogger<EmailNotificationService> _logger;
    private readonly string _templatesPath;

    public EmailNotificationService(
        IOptions<AnafSettings> settings,
        ILogger<EmailNotificationService> logger)
    {
        _emailSettings = settings.Value.Email;
        _logger = logger;
        _templatesPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "EmailTemplates");
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

        var subject = "ANAF Token - Pomyślna aktualizacja tokena";
        var template = LoadTemplate("TokenRefreshSuccessTemplate");
        var body = template
            .Replace("{0}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
            .Replace("{1}", newExpirationDate.ToString("yyyy-MM-dd HH:mm:ss"));

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

        var subject = "ANAF Token - Błąd aktualizacji tokena";
        var exceptionDetails = exception != null
            ? $"{exception.GetType().FullName}: {exception.Message}{exception.StackTrace}{exception.InnerException?.Message}"
            : string.Empty;

        var template = LoadTemplate("TokenRefreshErrorTemplate");
        var body = template
            .Replace("{0}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
            .Replace("{1}", errorMessage)
            .Replace("{2}", exceptionDetails);

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
        {
            _logger.LogWarning("Email settings are null. Cannot send email with subject: {Subject}", subject);
            throw new InvalidOperationException("Email settings are not configured");
        }

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

    private string LoadTemplate(string templateName)
    {
        var path = Path.Combine(_templatesPath, $"{templateName}.html");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Template {templateName} not found at {path}.");
        }
        return File.ReadAllText(path);
    }
}
