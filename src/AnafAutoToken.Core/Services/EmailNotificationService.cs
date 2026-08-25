using AnafAutoToken.Shared.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Reflection;
using System.Text;

namespace AnafAutoToken.Core.Services;

public class EmailNotificationService : IEmailNotificationService
{
    private const string DetailsSectionStart = "<!--SZCZEGOLY_START-->";
    private const string DetailsSectionEnd = "<!--SZCZEGOLY_END-->";

    private readonly EmailSettings? _emailSettings;
    private readonly ILogger<EmailNotificationService> _logger;
    private readonly string _templatesPath;

    public EmailNotificationService(
        IOptions<AnafSettings> settings,
        ILogger<EmailNotificationService> logger)
    {
        _emailSettings = settings.Value.Email;
        _logger = logger;
        _templatesPath = Path.Combine(AppContext.BaseDirectory, "EmailTemplates");
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

        var template = ApplyExceptionDetails(LoadTemplate("TokenRefreshErrorTemplate"), exception);
        var body = template
            .Replace("{0}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
            // Komunikaty ANAF bywają fragmentami JSON-a, a wyjątki niosą nazwy typów
            // generycznych - bez kodowania rozjechałyby układ wiadomości.
            .Replace("{1}", WebUtility.HtmlEncode(errorMessage));

        await SendEmailAsync(subject, body, cancellationToken);
    }

    public async Task SendTokenNoRefreshNeededNotificationAsync(
        DateTime expirationDate,
        int daysUntilRefresh,
        CancellationToken cancellationToken = default)
    {
        if (!IsEmailConfigured())
        {
            _logger.LogDebug("Email notifications are not configured. Skipping no-refresh-needed notification.");
            return;
        }

        var subject = "ANAF Token - Token nie wymaga odświeżenia";
        var template = LoadTemplate("TokenNoRefreshNeededTemplate");
        var body = template
            .Replace("{0}", expirationDate.ToString("yyyy-MM-dd HH:mm:ss"))
            .Replace("{1}", daysUntilRefresh.ToString())
            .Replace("{2}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

        await SendEmailAsync(subject, body, cancellationToken);
    }

    /// <summary>
    /// Wypełnia sekcję ze szczegółami technicznymi albo usuwa ją w całości, gdy nie ma
    /// wyjątku - pusta ramka w wiadomości tylko myli odbiorcę.
    /// </summary>
    internal static string ApplyExceptionDetails(string template, Exception? exception)
    {
        if (exception is null)
        {
            return RemoveSection(template, DetailsSectionStart, DetailsSectionEnd);
        }

        var details = new StringBuilder()
            .Append(exception.GetType().FullName)
            .Append(": ")
            .AppendLine(exception.Message);

        if (exception.InnerException is { } inner)
        {
            details
                .AppendLine()
                .Append("Wyjątek wewnętrzny: ")
                .Append(inner.GetType().FullName)
                .Append(": ")
                .AppendLine(inner.Message);
        }

        if (!string.IsNullOrWhiteSpace(exception.StackTrace))
        {
            details.AppendLine().AppendLine(exception.StackTrace.Trim());
        }

        return template.Replace("{2}", WebUtility.HtmlEncode(details.ToString().TrimEnd()));
    }

    internal static string RemoveSection(string template, string start, string end)
    {
        var startIndex = template.IndexOf(start, StringComparison.Ordinal);
        var endIndex = template.IndexOf(end, StringComparison.Ordinal);

        if (startIndex < 0 || endIndex < startIndex)
        {
            return template;
        }

        return template.Remove(startIndex, endIndex - startIndex + end.Length);
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
