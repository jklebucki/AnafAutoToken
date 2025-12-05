namespace AnafAutoToken.Shared.Configuration;

public class AnafSettings
{
    public required string TokenEndpoint { get; init; }
    public required BasicAuthSettings BasicAuth { get; init; }
    public required CheckScheduleSettings CheckSchedule { get; init; }
    public required int DaysBeforeExpiration { get; init; }
    public required string ConfigFilePath { get; init; }
    public required string BackupDirectory { get; init; }
    public string? InitialRefreshToken { get; init; }
    public EmailSettings? Email { get; init; }
}

public class BasicAuthSettings
{
    public required string Username { get; init; }
    public required string Password { get; init; }
}

public class CheckScheduleSettings
{
    public required int CheckHour { get; init; }
    public required int CheckMinute { get; init; }
}

public class EmailSettings
{
    public required string SmtpServer { get; init; }
    public required int SmtpPort { get; init; }
    public required string Username { get; init; }
    public required string Password { get; init; }
    public required string FromAddress { get; init; }
    public required string FromName { get; init; }
    public required string[] ToAddresses { get; init; }
    public bool EnableSsl { get; init; } = true;
}
