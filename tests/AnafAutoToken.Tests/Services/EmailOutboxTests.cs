using AnafAutoToken.Core.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using System.Net.Mail;

namespace AnafAutoToken.Tests.Services;

public class EmailOutboxTests
{
    private static readonly EmailMessage Message = new("Temat", "<p>Treść</p>");

    private readonly Mock<IEmailSender> _senderMock = new();
    private readonly Mock<ILogger<EmailOutbox>> _loggerMock = new();

    private EmailOutbox CreateOutbox() => new(_senderMock.Object, _loggerMock.Object, TimeSpan.Zero);

    [Fact]
    public async Task DeliverAsync_WhenFirstAttemptSucceeds_SendsOnceWithoutWarnings()
    {
        await CreateOutbox().DeliverAsync(Message, CancellationToken.None);

        _senderMock.Verify(x => x.SendAsync(Message, It.IsAny<CancellationToken>()), Times.Once);
        VerifyLogged(LogLevel.Warning, Times.Never());
        VerifyLogged(LogLevel.Error, Times.Never());
    }

    [Fact]
    public async Task DeliverAsync_WhenSendingRecoversOnRetry_DoesNotLogError()
    {
        _senderMock
            .SetupSequence(x => x.SendAsync(Message, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SmtpException("Failure sending mail."))
            .ThrowsAsync(new SmtpException("Failure sending mail."))
            .Returns(Task.CompletedTask);

        await CreateOutbox().DeliverAsync(Message, CancellationToken.None);

        _senderMock.Verify(x => x.SendAsync(Message, It.IsAny<CancellationToken>()), Times.Exactly(3));
        VerifyLogged(LogLevel.Warning, Times.Exactly(2));
        VerifyLogged(LogLevel.Error, Times.Never());
    }

    [Fact]
    public async Task DeliverAsync_WhenAllRetriesFail_LogsErrorOnceAfterLastAttempt()
    {
        _senderMock
            .Setup(x => x.SendAsync(Message, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SmtpException("Failure sending mail."));

        await CreateOutbox().DeliverAsync(Message, CancellationToken.None);

        _senderMock.Verify(
            x => x.SendAsync(Message, It.IsAny<CancellationToken>()),
            Times.Exactly(EmailOutbox.MaxRetries + 1));
        VerifyLogged(LogLevel.Warning, Times.Exactly(EmailOutbox.MaxRetries));
        VerifyLogged(LogLevel.Error, Times.Once());
    }

    [Fact]
    public async Task DeliverAsync_WhenStoppedWhileWaitingForRetry_StopsWithoutLoggingError()
    {
        using var cts = new CancellationTokenSource();
        var outbox = new EmailOutbox(_senderMock.Object, _loggerMock.Object, TimeSpan.FromMinutes(5));

        _senderMock
            .Setup(x => x.SendAsync(Message, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SmtpException("Failure sending mail."));

        cts.CancelAfter(TimeSpan.FromMilliseconds(200));
        var act = () => outbox.DeliverAsync(Message, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _senderMock.Verify(x => x.SendAsync(Message, It.IsAny<CancellationToken>()), Times.Once);
        VerifyLogged(LogLevel.Warning, Times.Once());
        VerifyLogged(LogLevel.Error, Times.Never());
    }

    [Fact]
    public async Task ProcessAsync_DeliversQueuedMessages()
    {
        using var cts = new CancellationTokenSource();
        var outbox = CreateOutbox();
        var second = new EmailMessage("Drugi", "<p>2</p>");

        _senderMock
            .Setup(x => x.SendAsync(second, It.IsAny<CancellationToken>()))
            .Callback(() => cts.Cancel())
            .Returns(Task.CompletedTask);

        outbox.Enqueue(Message);
        outbox.Enqueue(second);

        var act = () => outbox.ProcessAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _senderMock.Verify(x => x.SendAsync(Message, It.IsAny<CancellationToken>()), Times.Once);
        _senderMock.Verify(x => x.SendAsync(second, It.IsAny<CancellationToken>()), Times.Once);
    }

    private void VerifyLogged(LogLevel level, Times times) =>
        _loggerMock.Verify(
            x => x.Log(
                level,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            times);
}
