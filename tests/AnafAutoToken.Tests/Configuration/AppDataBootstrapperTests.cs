using System.Text.Json.Nodes;
using AnafAutoToken.Infrastructure.Data;
using AnafAutoToken.Shared.Configuration;
using FluentAssertions;

namespace AnafAutoToken.Tests.Configuration;

[Collection(DataDirectoryCollection.Name)]
public class AppDataBootstrapperTests : IDisposable
{
    public AppDataBootstrapperTests() => Cleanup();

    [Fact]
    public void DataDirectory_HonoursTheEnvironmentOverride()
    {
        AppPaths.DataDirectory.Should().Be(TestDataDirectory.Path);
        AppPaths.SettingsFile.Should().Be(Path.Combine(TestDataDirectory.Path, "appsettings.json"));
        AppPaths.DatabaseFile.Should().Be(Path.Combine(TestDataDirectory.Path, "tokens.db"));
    }

    [Fact]
    public void Ensure_CreatesDirectoriesAndSettingsFile()
    {
        var result = AppDataBootstrapper.Ensure(seedSettingsFile: null);

        result.CreatedDataDirectory.Should().BeTrue();
        result.CreatedSettingsFile.Should().BeTrue();

        Directory.Exists(AppPaths.DataDirectory).Should().BeTrue();
        Directory.Exists(AppPaths.BackupDirectory).Should().BeTrue();
        Directory.Exists(AppPaths.LogDirectory).Should().BeTrue();
        File.Exists(AppPaths.SettingsFile).Should().BeTrue();
    }

    [Fact]
    public void Ensure_IsIdempotentAndNeverOverwritesAnExistingSettingsFile()
    {
        AppDataBootstrapper.Ensure(seedSettingsFile: null);

        var marker = JsonNode.Parse(File.ReadAllText(AppPaths.SettingsFile))!.AsObject();
        marker["Anaf"]!["InitialRefreshToken"] = "token-ustawiony-przez-operatora";
        File.WriteAllText(AppPaths.SettingsFile, marker.ToJsonString());

        var second = AppDataBootstrapper.Ensure(seedSettingsFile: null);

        second.CreatedSettingsFile.Should().BeFalse();

        var reloaded = JsonNode.Parse(File.ReadAllText(AppPaths.SettingsFile))!.AsObject();
        reloaded["Anaf"]!["InitialRefreshToken"]!.GetValue<string>()
            .Should().Be("token-ustawiony-przez-operatora");
    }

    [Fact]
    public void Ensure_RewritesSeededPathsSoTheyPointAtTheDataDirectory()
    {
        // Wzorzec z katalogu wdrożeniowego zwykle niesie względną bazę - to właśnie ona
        // powodowała, że usługa zakładała tokens.db w C:\Windows\System32.
        var seedPath = Path.Combine(Path.GetTempPath(), $"seed_{Guid.NewGuid():N}.json");

        File.WriteAllText(seedPath, """
            {
              "Anaf": { "BackupDirectory": "backups", "DaysBeforeExpiration": 9 },
              "ConnectionStrings": { "TokenDatabase": "Data Source=tokens.db" }
            }
            """);

        try
        {
            AppDataBootstrapper.Ensure(seedPath);

            var settings = JsonNode.Parse(File.ReadAllText(AppPaths.SettingsFile))!.AsObject();

            settings["ConnectionStrings"]!["TokenDatabase"]!.GetValue<string>()
                .Should().Be(AppPaths.DefaultConnectionString);
            settings["Anaf"]!["BackupDirectory"]!.GetValue<string>()
                .Should().Be(AppPaths.BackupDirectory);

            // Pozostałe wartości ze wzorca muszą przetrwać.
            settings["Anaf"]!["DaysBeforeExpiration"]!.GetValue<int>().Should().Be(9);
        }
        finally
        {
            File.Delete(seedPath);
        }
    }

    [Theory]
    [InlineData("Data Source=tokens.db")]
    [InlineData("Data Source=podkatalog/tokens.db")]
    [InlineData("")]
    [InlineData(null)]
    public void ResolveConnectionString_AlwaysProducesAnAbsolutePathInsideTheDataDirectory(string? connectionString)
    {
        var databasePath = TokenDatabase.ResolveDatabasePath(connectionString);

        Path.IsPathRooted(databasePath).Should().BeTrue();
        databasePath.Should().StartWith(AppPaths.DataDirectory);
    }

    [Fact]
    public void ResolveConnectionString_KeepsAnAbsolutePathUntouched()
    {
        var absolute = Path.Combine(Path.GetTempPath(), "gdzie-indziej", "tokens.db");

        TokenDatabase.ResolveDatabasePath($"Data Source={absolute}")
            .Should().Be(Path.GetFullPath(absolute));
    }

    public void Dispose() => Cleanup();

    private static void Cleanup()
    {
        if (Directory.Exists(TestDataDirectory.Path))
        {
            Directory.Delete(TestDataDirectory.Path, recursive: true);
        }
    }
}
