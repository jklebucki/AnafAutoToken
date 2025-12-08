using AnafAutoToken.Core.Services;
using AnafAutoToken.Shared.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace AnafAutoToken.Tests.Services;

public class ConfigFileServiceTests : IDisposable
{
    private readonly Mock<ILogger<ConfigFileService>> _loggerMock;
    private readonly Mock<IOptions<AnafSettings>> _settingsMock;
    private readonly string _testDirectory;
    private readonly string _testConfigFile;
    private readonly string _testBackupDirectory;
    private readonly ConfigFileService _service;

    public ConfigFileServiceTests()
    {
        _loggerMock = new Mock<ILogger<ConfigFileService>>();
        _settingsMock = new Mock<IOptions<AnafSettings>>();
        
        _testDirectory = Path.Combine(Path.GetTempPath(), $"ConfigFileServiceTests_{Guid.NewGuid()}");
        _testBackupDirectory = Path.Combine(_testDirectory, "backups");
        _testConfigFile = Path.Combine(_testDirectory, "test_config.ini");

        Directory.CreateDirectory(_testDirectory);

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
            ConfigFilePath = _testConfigFile,
            BackupDirectory = _testBackupDirectory
        };

        _settingsMock.Setup(s => s.Value).Returns(settings);
        _service = new ConfigFileService(_settingsMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task ReadAccessTokenAsync_WithValidConfigFile_ReturnsToken()
    {
        // Arrange
        var expectedToken = "test.jwt.token.value";
        var configContent = $"""
            [SomeOtherSection]
            SomeKey=SomeValue

            [AcessToken]
            {expectedToken}

            [AnotherSection]
            AnotherKey=AnotherValue
            """;
        await File.WriteAllTextAsync(_testConfigFile, configContent);

        // Act
        var result = await _service.ReadAccessTokenAsync();

        // Assert
        result.Should().Be(expectedToken);
    }

    [Fact]
    public async Task ReadAccessTokenAsync_WithConfigFileNotFound_ThrowsFileNotFoundException()
    {
        // Arrange
        // Config file does not exist

        // Act
        Func<Task> act = async () => await _service.ReadAccessTokenAsync();

        // Assert
        await act.Should().ThrowAsync<FileNotFoundException>()
            .WithMessage($"Config file not found: {_testConfigFile}");
    }

    [Fact]
    public async Task ReadAccessTokenAsync_WithMissingAccessToken_ThrowsInvalidOperationException()
    {
        // Arrange
        var configContent = """
            [SomeOtherSection]
            SomeKey=SomeValue
            """;
        await File.WriteAllTextAsync(_testConfigFile, configContent);

        // Act
        Func<Task> act = async () => await _service.ReadAccessTokenAsync();

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Access token not found in config file");
    }

    [Fact]
    public async Task ReadAccessTokenAsync_WithTokenHavingWhitespace_ReturnsTrimedToken()
    {
        // Arrange
        var expectedToken = "test.jwt.token.value";
        var configContent = $"""
            [AcessToken]
              {expectedToken}  

            """;
        await File.WriteAllTextAsync(_testConfigFile, configContent);

        // Act
        var result = await _service.ReadAccessTokenAsync();

        // Assert
        result.Should().Be(expectedToken);
    }

    [Fact]
    public async Task UpdateAccessTokenAsync_WithValidConfigFile_UpdatesToken()
    {
        // Arrange
        var oldToken = "old.jwt.token.value";
        var newToken = "new.jwt.token.value";
        var configContent = $"""
            [SomeSection]
            SomeKey=SomeValue

            [AcessToken]
            {oldToken}

            [AnotherSection]
            AnotherKey=AnotherValue
            """;
        await File.WriteAllTextAsync(_testConfigFile, configContent);

        // Act
        await _service.UpdateAccessTokenAsync(newToken);

        // Assert
        var updatedContent = await File.ReadAllTextAsync(_testConfigFile);
        updatedContent.Should().Contain(newToken);
        updatedContent.Should().NotContain(oldToken);
        updatedContent.Should().Contain("[AcessToken]");
        updatedContent.Should().Contain("[SomeSection]");
        updatedContent.Should().Contain("[AnotherSection]");
    }

    [Fact]
    public async Task UpdateAccessTokenAsync_WithConfigFileNotFound_ThrowsFileNotFoundException()
    {
        // Arrange
        var newToken = "new.jwt.token.value";

        // Act
        Func<Task> act = async () => await _service.UpdateAccessTokenAsync(newToken);

        // Assert
        await act.Should().ThrowAsync<FileNotFoundException>()
            .WithMessage($"Config file not found: {_testConfigFile}");
    }

    [Fact]
    public async Task UpdateAccessTokenAsync_PreservesFileStructure()
    {
        // Arrange
        var oldToken = "old.jwt.token.value";
        var newToken = "new.jwt.token.value";
        var configContent = $"""
            [Section1]
            Key1=Value1
            Key2=Value2

            [AcessToken]
            {oldToken}

            [Section2]
            Key3=Value3
            """;
        await File.WriteAllTextAsync(_testConfigFile, configContent);

        // Act
        await _service.UpdateAccessTokenAsync(newToken);

        // Assert
        var updatedContent = await File.ReadAllTextAsync(_testConfigFile);
        updatedContent.Should().Contain("Key1=Value1");
        updatedContent.Should().Contain("Key2=Value2");
        updatedContent.Should().Contain("Key3=Value3");
        updatedContent.Should().Contain("[Section1]");
        updatedContent.Should().Contain("[Section2]");
    }

    [Fact]
    public async Task CreateBackupAsync_WithValidConfigFile_CreatesBackup()
    {
        // Arrange
        var configContent = """
            [AcessToken]
            test.jwt.token
            """;
        await File.WriteAllTextAsync(_testConfigFile, configContent);

        // Act
        await _service.CreateBackupAsync();

        // Assert
        Directory.Exists(_testBackupDirectory).Should().BeTrue();
        var backupFiles = Directory.GetFiles(_testBackupDirectory, "bak_config_ini_*.txt");
        backupFiles.Should().NotBeEmpty();
        
        var backupContent = await File.ReadAllTextAsync(backupFiles[0]);
        backupContent.Should().Be(configContent);
    }

    [Fact]
    public async Task CreateBackupAsync_CreatesBackupWithTimestamp()
    {
        // Arrange
        var configContent = "[AcessToken]\ntest.token";
        await File.WriteAllTextAsync(_testConfigFile, configContent);

        // Act
        await _service.CreateBackupAsync();

        // Assert
        var backupFiles = Directory.GetFiles(_testBackupDirectory, "bak_config_ini_*.txt");
        backupFiles.Should().HaveCount(1);
        
        var fileName = Path.GetFileName(backupFiles[0]);
        fileName.Should().MatchRegex(@"bak_config_ini_\d{8}_\d{6}\.txt");
    }

    [Fact]
    public async Task CreateBackupAsync_WithNonExistentConfigFile_DoesNotThrow()
    {
        // Arrange
        // Config file does not exist

        // Act
        Func<Task> act = async () => await _service.CreateBackupAsync();

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CreateBackupAsync_CreatesBackupDirectoryIfNotExists()
    {
        // Arrange
        var configContent = "[AcessToken]\ntest.token";
        await File.WriteAllTextAsync(_testConfigFile, configContent);
        
        if (Directory.Exists(_testBackupDirectory))
        {
            Directory.Delete(_testBackupDirectory, true);
        }

        // Act
        await _service.CreateBackupAsync();

        // Assert
        Directory.Exists(_testBackupDirectory).Should().BeTrue();
    }

    [Fact]
    public async Task CreateBackupAsync_MultipleBackups_CreatesMultipleFiles()
    {
        // Arrange
        var configContent = "[AcessToken]\ntest.token";
        await File.WriteAllTextAsync(_testConfigFile, configContent);

        // Act
        await _service.CreateBackupAsync();
        await Task.Delay(1100); // Ensure different timestamp
        await _service.CreateBackupAsync();

        // Assert
        var backupFiles = Directory.GetFiles(_testBackupDirectory, "bak_config_ini_*.txt");
        backupFiles.Length.Should().BeGreaterThanOrEqualTo(2);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            try
            {
                Directory.Delete(_testDirectory, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}
