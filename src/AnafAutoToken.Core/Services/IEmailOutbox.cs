namespace AnafAutoToken.Core.Services;

public interface IEmailOutbox
{
    void Enqueue(EmailMessage message);
}
