using AnafAutoToken.Shared.Configuration;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Mail;

namespace AnafAutoToken.Core.Services;

public class SmtpEmailSender(IOptions<AnafSettings> settings) : IEmailSender
{
    private readonly EmailSettings? _emailSettings = settings.Value.Email;

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        if (_emailSettings == null)
        {
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
            Subject = message.Subject,
            Body = message.Body,
            IsBodyHtml = true
        };

        foreach (var toAddress in _emailSettings.ToAddresses)
        {
            mailMessage.To.Add(toAddress);
        }

        await smtpClient.SendMailAsync(mailMessage, cancellationToken);
    }
}
