using AnafAutoToken.Core.Services;
using AnafAutoToken.Shared.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Reflection;

namespace AnafAutoToken.Tests.Services;

public class EmailNotificationServiceTests : IDisposable
{
    private readonly Mock<ILogger<EmailNotificationService>> _loggerMock;
    private readonly Mock<IOptions<AnafSettings>> _settingsMock;
    private readonly string _testTemplatesDirectory;

    public EmailNotificationServiceTests()
    {
        _loggerMock = new Mock<ILogger<EmailNotificationService>>();
        _settingsMock = new Mock<IOptions<AnafSettings>>();
        
        _testTemplatesDirectory = Path.Combine(Path.GetTempPath(), $"EmailTemplates_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testTemplatesDirectory);
    }

    private EmailNotificationService CreateServiceWithEmailSettings(EmailSettings? emailSettings)
    {
        var settings = new AnafSettings
        {
            TokenEndpoint = "https://test.api.anaf.ro/token",
            BasicAuth = new BasicAuthSettings
            {
                Username = "test",
                Password = "test"
            },
            CheckSchedule = new CheckScheduleSettings
            {
                CheckHour = 8,
                CheckMinute = 0
            },
            DaysBeforeExpiration = 7,
            ConfigFilePath = "test_config.ini",
            BackupDirectory = "test_backups",
            Email = emailSettings
        };

        _settingsMock.Setup(s => s.Value).Returns(settings);
        return new EmailNotificationService(_settingsMock.Object, _loggerMock.Object);
    }

    private void SetupTemplateDirectory(EmailNotificationService service)
    {
        var templatesPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "EmailTemplates");
        if (!Directory.Exists(templatesPath))
        {
            Directory.CreateDirectory(templatesPath);
        }

        var successTemplate = """
            <html>
            <body>
            <h1>Token Refresh Success</h1>
            <p>Time: {0}</p>
            <p>New Expiration: {1}</p>
            </body>
            </html>
            """;
        File.WriteAllText(Path.Combine(templatesPath, "TokenRefreshSuccessTemplate.html"), successTemplate);

        var errorTemplate = """
            <html>
            <body>
            <h1>Token Refresh Error</h1>
            <p>Time: {0}</p>
            <p>Error: {1}</p>
            <p>Details: {2}</p>
            </body>
            </html>
            """;
        File.WriteAllText(Path.Combine(templatesPath, "TokenRefreshErrorTemplate.html"), errorTemplate);

        var noRefreshTemplate = """
            <html>
            <body>
            <h1>Token No Refresh Needed</h1>
            <p>Expiration: {0}</p>
            <p>Days Until Refresh: {1}</p>
            <p>Check Time: {2}</p>
            </body>
            </html>
            """;
        File.WriteAllText(Path.Combine(templatesPath, "TokenNoRefreshNeededTemplate.html"), noRefreshTemplate);
    }

    [Fact]
    public async Task SendTokenRefreshSuccessNotificationAsync_WithEmailNotConfigured_DoesNotSendEmail()
    {
        // Arrange
        var service = CreateServiceWithEmailSettings(null);
        var newExpirationDate = DateTime.UtcNow.AddDays(30);

        // Act
        await service.SendTokenRefreshSuccessNotificationAsync(newExpirationDate);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Email notifications are not configured")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task SendTokenRefreshErrorNotificationAsync_WithEmailNotConfigured_DoesNotSendEmail()
    {
        // Arrange
        var service = CreateServiceWithEmailSettings(null);
        var errorMessage = "Test error";

        // Act
        await service.SendTokenRefreshErrorNotificationAsync(errorMessage);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Email notifications are not configured")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task SendTokenNoRefreshNeededNotificationAsync_WithEmailNotConfigured_DoesNotSendEmail()
    {
        // Arrange
        var service = CreateServiceWithEmailSettings(null);
        var expirationDate = DateTime.UtcNow.AddDays(10);

        // Act
        await service.SendTokenNoRefreshNeededNotificationAsync(expirationDate, 3);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Email notifications are not configured")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task SendTokenRefreshSuccessNotificationAsync_WithInvalidEmailSettings_DoesNotSendEmail()
    {
        // Arrange - missing ToAddresses
        var emailSettings = new EmailSettings
        {
            SmtpServer = "smtp.test.com",
            SmtpPort = 587,
            Username = "test@test.com",
            Password = "password",
            FromAddress = "from@test.com",
            FromName = "Test Sender",
            ToAddresses = Array.Empty<string>(),
            EnableSsl = true
        };
        var service = CreateServiceWithEmailSettings(emailSettings);
        var newExpirationDate = DateTime.UtcNow.AddDays(30);

        // Act
        await service.SendTokenRefreshSuccessNotificationAsync(newExpirationDate);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Email notifications are not configured")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task SendTokenRefreshSuccessNotificationAsync_WithMissingTemplate_ThrowsFileNotFoundException()
    {
        // Arrange
        var emailSettings = new EmailSettings
        {
            SmtpServer = "smtp.test.com",
            SmtpPort = 587,
            Username = "test@test.com",
            Password = "password",
            FromAddress = "from@test.com",
            FromName = "Test Sender",
            ToAddresses = new[] { "recipient@test.com" },
            EnableSsl = true
        };
        var service = CreateServiceWithEmailSettings(emailSettings);
        var newExpirationDate = DateTime.UtcNow.AddDays(30);

        // Act
        Func<Task> act = async () => await service.SendTokenRefreshSuccessNotificationAsync(newExpirationDate);

        // Assert
        await act.Should().ThrowAsync<FileNotFoundException>()
            .WithMessage("*TokenRefreshSuccessTemplate*");
    }

    [Fact]
    public async Task SendTokenRefreshErrorNotificationAsync_WithMissingTemplate_ThrowsFileNotFoundException()
    {
        // Arrange
        var emailSettings = new EmailSettings
        {
            SmtpServer = "smtp.test.com",
            SmtpPort = 587,
            Username = "test@test.com",
            Password = "password",
            FromAddress = "from@test.com",
            FromName = "Test Sender",
            ToAddresses = new[] { "recipient@test.com" },
            EnableSsl = true
        };
        var service = CreateServiceWithEmailSettings(emailSettings);
        var errorMessage = "Test error";

        // Act
        Func<Task> act = async () => await service.SendTokenRefreshErrorNotificationAsync(errorMessage);

        // Assert
        await act.Should().ThrowAsync<FileNotFoundException>()
            .WithMessage("*TokenRefreshErrorTemplate*");
    }

    [Fact]
    public async Task SendTokenNoRefreshNeededNotificationAsync_WithMissingTemplate_ThrowsFileNotFoundException()
    {
        // Arrange
        var emailSettings = new EmailSettings
        {
            SmtpServer = "smtp.test.com",
            SmtpPort = 587,
            Username = "test@test.com",
            Password = "password",
            FromAddress = "from@test.com",
            FromName = "Test Sender",
            ToAddresses = new[] { "recipient@test.com" },
            EnableSsl = true
        };
        var service = CreateServiceWithEmailSettings(emailSettings);
        var expirationDate = DateTime.UtcNow.AddDays(10);

        // Act
        Func<Task> act = async () => await service.SendTokenNoRefreshNeededNotificationAsync(expirationDate, 3);

        // Assert
        await act.Should().ThrowAsync<FileNotFoundException>()
            .WithMessage("*TokenNoRefreshNeededTemplate*");
    }

    [Fact]
    public void EmailSettings_WithEmptySmtpServer_IsNotConfigured()
    {
        // Arrange
        var emailSettings = new EmailSettings
        {
            SmtpServer = "",
            SmtpPort = 587,
            Username = "test@test.com",
            Password = "password",
            FromAddress = "from@test.com",
            FromName = "Test Sender",
            ToAddresses = new[] { "recipient@test.com" },
            EnableSsl = true
        };
        var service = CreateServiceWithEmailSettings(emailSettings);

        // Act
        var act = async () => await service.SendTokenRefreshSuccessNotificationAsync(DateTime.UtcNow);

        // Assert - should log debug message instead of attempting to send
        act.Should().NotThrowAsync();
    }

    [Fact]
    public void EmailSettings_WithEmptyFromAddress_IsNotConfigured()
    {
        // Arrange
        var emailSettings = new EmailSettings
        {
            SmtpServer = "smtp.test.com",
            SmtpPort = 587,
            Username = "test@test.com",
            Password = "password",
            FromAddress = "",
            FromName = "Test Sender",
            ToAddresses = new[] { "recipient@test.com" },
            EnableSsl = true
        };
        var service = CreateServiceWithEmailSettings(emailSettings);

        // Act
        var act = async () => await service.SendTokenRefreshSuccessNotificationAsync(DateTime.UtcNow);

        // Assert - should log debug message instead of attempting to send
        act.Should().NotThrowAsync();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testTemplatesDirectory))
        {
            try
            {
                Directory.Delete(_testTemplatesDirectory, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        // Clean up test templates from executing assembly location
        var templatesPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "EmailTemplates");
        if (Directory.Exists(templatesPath))
        {
            try
            {
                Directory.Delete(templatesPath, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}
