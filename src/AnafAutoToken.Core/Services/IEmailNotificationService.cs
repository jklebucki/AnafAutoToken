namespace AnafAutoToken.Core.Services;

public interface IEmailNotificationService
{
    Task SendTokenRefreshSuccessNotificationAsync(DateTime newExpirationDate, CancellationToken cancellationToken = default);
    Task SendTokenRefreshErrorNotificationAsync(string errorMessage, Exception? exception = null, CancellationToken cancellationToken = default);
    Task SendTokenNoRefreshNeededNotificationAsync(DateTime expirationDate, int daysUntilRefresh, CancellationToken cancellationToken = default);
}
