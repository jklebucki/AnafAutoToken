namespace AnafAutoToken.Core.Models;

public class TokenRefreshResult
{
    public bool IsSuccess { get; init; }
    public bool TokenWasRefreshed { get; init; }
    public DateTime? NewExpirationDate { get; init; }
    public string? ErrorMessage { get; init; }
    public Exception? Exception { get; init; }

    public static TokenRefreshResult NoRefreshNeeded(DateTime expirationDate) => new()
    {
        IsSuccess = true,
        TokenWasRefreshed = false,
        NewExpirationDate = expirationDate
    };

    public static TokenRefreshResult Success(DateTime newExpirationDate) => new()
    {
        IsSuccess = true,
        TokenWasRefreshed = true,
        NewExpirationDate = newExpirationDate
    };

    public static TokenRefreshResult Failure(string errorMessage, Exception? exception = null) => new()
    {
        IsSuccess = false,
        TokenWasRefreshed = false,
        ErrorMessage = errorMessage,
        Exception = exception
    };
}
