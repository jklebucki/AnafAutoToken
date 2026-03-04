using System.Text.Json;
using System.Text.RegularExpressions;
using AnafAutoToken.Infrastructure.Data;
using AnafAutoToken.Shared.Extensions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

return await new ExportApplication(AppContext.BaseDirectory, Console.Out, Console.Error).RunAsync(args);

internal sealed partial class ExportApplication(string baseDirectory, TextWriter output, TextWriter error)
{
    private const string ExportCurrentTokenOption = "-ect";
    private const string ExportAllTokensOption = "-eat";
    private const string HelpOption = "-h";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0)
        {
            WriteHelp();
            return 0;
        }

        if (args.Length > 1)
        {
            error.WriteLine("Only one option can be used at a time.");
            WriteHelp();
            return 1;
        }

        var option = args[0].Trim().ToLowerInvariant();

        return option switch
        {
            ExportCurrentTokenOption => await ExportCurrentTokenAsync(),
            ExportAllTokensOption => await ExportAllTokensAsync(),
            HelpOption or "--help" or "/?" or "-?" => ShowHelp(),
            _ => ShowInvalidOption(option)
        };
    }

    private int ShowHelp()
    {
        WriteHelp();
        return 0;
    }

    private int ShowInvalidOption(string option)
    {
        error.WriteLine($"Unknown option '{option}'.");
        WriteHelp();
        return 1;
    }

    private async Task<int> ExportCurrentTokenAsync()
    {
        try
        {
            var configuration = LoadConfiguration();
            var databaseSettings = await ResolveBestDatabaseSettingsAsync(configuration);
            var latestToken = default(CurrentTokenEntry?);

            if (File.Exists(databaseSettings.DatabasePath))
            {
                latestToken = await GetLatestSuccessfulTokenAsync(databaseSettings.ConnectionString);
            }

            CurrentTokenExportFile payload;

            if (latestToken is not null)
            {
                payload = new CurrentTokenExportFile(
                    ExportedAtUtc: DateTime.UtcNow,
                    SourceDatabase: databaseSettings.DatabasePath,
                    CurrentToken: new CurrentTokenPayload(
                        latestToken.AccessToken,
                        latestToken.RefreshToken,
                        latestToken.AccessToken.GetExpirationDate(),
                        latestToken.StoredExpiresAt,
                        latestToken.SavedAt,
                        "database"));
            }
            else
            {
                var refreshToken = configuration["Anaf:InitialRefreshToken"];
                var accessToken = TryReadAccessTokenFromConfiguredFile(configuration);

                if (string.IsNullOrWhiteSpace(refreshToken) || string.IsNullOrWhiteSpace(accessToken))
                {
                    error.WriteLine("No current token could be resolved from the database or configuration files.");
                    return 1;
                }

                payload = new CurrentTokenExportFile(
                    ExportedAtUtc: DateTime.UtcNow,
                    SourceDatabase: File.Exists(databaseSettings.DatabasePath) ? databaseSettings.DatabasePath : null,
                    CurrentToken: new CurrentTokenPayload(
                        accessToken,
                        refreshToken,
                        accessToken.GetExpirationDate(),
                        StoredExpiresAt: null,
                        SavedAt: null,
                        Source: "config-and-settings"));
            }

            var exportPath = BuildExportPath("anaf-current-token");
            await WriteJsonAsync(exportPath, payload);

            output.WriteLine($"Current token exported to '{exportPath}'.");
            return 0;
        }
        catch (Exception ex)
        {
            error.WriteLine($"Current token export failed: {ex.Message}");
            return 1;
        }
    }

    private async Task<int> ExportAllTokensAsync()
    {
        try
        {
            var configuration = LoadConfiguration();
            var databaseSettings = await ResolveBestDatabaseSettingsAsync(configuration);

            if (!File.Exists(databaseSettings.DatabasePath))
            {
                error.WriteLine($"SQLite database file was not found: '{databaseSettings.DatabasePath}'.");
                return 1;
            }

            var tokens = await GetAllTokenLogsAsync(databaseSettings.ConnectionString);
            var payload = new AllTokensExportFile(
                ExportedAtUtc: DateTime.UtcNow,
                SourceDatabase: databaseSettings.DatabasePath,
                Count: tokens.Count,
                TokenEntries: tokens);

            var exportPath = BuildExportPath("anaf-all-tokens");
            await WriteJsonAsync(exportPath, payload);

            output.WriteLine($"All saved tokens exported to '{exportPath}'.");
            return 0;
        }
        catch (Exception ex)
        {
            error.WriteLine($"All tokens export failed: {ex.Message}");
            return 1;
        }
    }

    private IConfigurationRoot LoadConfiguration()
    {
        return new ConfigurationBuilder()
            .SetBasePath(baseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();
    }

    private async Task<DatabaseSettings> ResolveBestDatabaseSettingsAsync(IConfiguration configuration)
    {
        var candidates = BuildDatabaseCandidates(configuration);
        var primaryCandidate = candidates[0];
        DatabaseSettings? bestCandidate = null;
        var bestCount = -1;

        foreach (var candidate in candidates)
        {
            if (!File.Exists(candidate.DatabasePath))
            {
                continue;
            }

            var tokenLogCount = await TryGetTokenLogCountAsync(candidate.ConnectionString);

            if (tokenLogCount > bestCount)
            {
                bestCount = tokenLogCount;
                bestCandidate = candidate;
            }
        }

        return bestCandidate ?? primaryCandidate;
    }

    private List<DatabaseSettings> BuildDatabaseCandidates(IConfiguration configuration)
    {
        var rawConnectionString = configuration.GetConnectionString("TokenDatabase") ?? "Data Source=tokens.db";
        var baseBuilder = new SqliteConnectionStringBuilder(rawConnectionString);

        if (string.IsNullOrWhiteSpace(baseBuilder.DataSource))
        {
            baseBuilder.DataSource = "tokens.db";
        }

        List<string> candidatePaths;

        if (Path.IsPathRooted(baseBuilder.DataSource))
        {
            candidatePaths =
            [
                Path.GetFullPath(baseBuilder.DataSource)
            ];
        }
        else
        {
            candidatePaths =
            [
                Path.GetFullPath(Path.Combine(baseDirectory, baseBuilder.DataSource)),
                Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), baseBuilder.DataSource))
            ];

            if (OperatingSystem.IsWindows())
            {
                candidatePaths.Add(Path.GetFullPath(Path.Combine(Environment.SystemDirectory, baseBuilder.DataSource)));
            }
        }

        return candidatePaths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path =>
            {
                var candidateBuilder = new SqliteConnectionStringBuilder(baseBuilder.ConnectionString)
                {
                    DataSource = path
                };

                return new DatabaseSettings(candidateBuilder.ToString(), path);
            })
            .ToList();
    }

    private async Task<int> TryGetTokenLogCountAsync(string connectionString)
    {
        try
        {
            await using var dbContext = CreateDbContext(connectionString);
            return await dbContext.TokenRefreshLogs.AsNoTracking().CountAsync();
        }
        catch
        {
            return -1;
        }
    }

    private async Task<CurrentTokenEntry?> GetLatestSuccessfulTokenAsync(string connectionString)
    {
        await using var dbContext = CreateDbContext(connectionString);

        return await dbContext.TokenRefreshLogs
            .AsNoTracking()
            .Where(log => log.IsSuccess && !string.IsNullOrWhiteSpace(log.AccessToken))
            .OrderByDescending(log => log.CreatedAt)
            .Select(log => new CurrentTokenEntry(
                log.AccessToken,
                log.RefreshToken,
                log.ExpiresAt,
                log.CreatedAt))
            .FirstOrDefaultAsync();
    }

    private async Task<List<TokenListItem>> GetAllTokenLogsAsync(string connectionString)
    {
        await using var dbContext = CreateDbContext(connectionString);

        return await dbContext.TokenRefreshLogs
            .AsNoTracking()
            .OrderByDescending(log => log.CreatedAt)
            .Select(log => new TokenListItem(
                log.Id,
                log.AccessToken,
                log.RefreshToken,
                string.IsNullOrWhiteSpace(log.AccessToken) ? null : log.AccessToken.GetExpirationDate(),
                log.ExpiresAt,
                log.CreatedAt,
                log.IsSuccess,
                log.ErrorMessage,
                log.ResponseStatusCode))
            .ToListAsync();
    }

    private AnafDbContext CreateDbContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<AnafDbContext>()
            .UseSqlite(connectionString)
            .Options;

        return new AnafDbContext(options);
    }

    private string? TryReadAccessTokenFromConfiguredFile(IConfiguration configuration)
    {
        var configFilePath = configuration["Anaf:ConfigFilePath"];

        if (string.IsNullOrWhiteSpace(configFilePath))
        {
            return null;
        }

        var resolvedConfigPath = Path.IsPathRooted(configFilePath)
            ? Path.GetFullPath(configFilePath)
            : Path.GetFullPath(Path.Combine(baseDirectory, configFilePath));

        if (!File.Exists(resolvedConfigPath))
        {
            return null;
        }

        var content = File.ReadAllText(resolvedConfigPath);
        var match = AccessTokenRegex().Match(content);

        if (!match.Success || match.Groups.Count < 2)
        {
            return null;
        }

        return match.Groups[1].Value.Trim();
    }

    private string BuildExportPath(string filePrefix)
    {
        var fileName = $"{filePrefix}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json";
        return Path.Combine(baseDirectory, fileName);
    }

    private static async Task WriteJsonAsync<T>(string path, T payload)
    {
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, payload, JsonOptions);
    }

    private void WriteHelp()
    {
        var executableName = $"{AppDomain.CurrentDomain.FriendlyName}.exe";

        output.WriteLine("ANAF Auto Token Exporter");
        output.WriteLine();
        output.WriteLine($"Usage: {executableName} [option]");
        output.WriteLine();
        output.WriteLine("Place this tool in the same folder as appsettings.json and tokens.db.");
        output.WriteLine("Each command creates a timestamped JSON file next to the executable.");
        output.WriteLine("The exported files contain sensitive token data. Protect them accordingly.");
        output.WriteLine();
        output.WriteLine("Options:");
        output.WriteLine($"  {ExportCurrentTokenOption}  Export the current access token and refresh token to JSON.");
        output.WriteLine($"  {ExportAllTokensOption}  Export all successfully saved token pairs from SQLite to JSON.");
        output.WriteLine($"  {HelpOption}    Show this help message in English.");
    }

    [GeneratedRegex(@"\[AcessToken\]\s*\r?\n(.+?)(?=\r?\n\[|$)", RegexOptions.Singleline)]
    private static partial Regex AccessTokenRegex();
}

internal sealed record DatabaseSettings(string ConnectionString, string DatabasePath);

internal sealed record CurrentTokenEntry(
    string AccessToken,
    string RefreshToken,
    DateTime StoredExpiresAt,
    DateTime SavedAt);

internal sealed record CurrentTokenExportFile(
    DateTime ExportedAtUtc,
    string? SourceDatabase,
    CurrentTokenPayload CurrentToken);

internal sealed record CurrentTokenPayload(
    string AccessToken,
    string RefreshToken,
    DateTime? AccessTokenExpiresAt,
    DateTime? StoredExpiresAt,
    DateTime? SavedAt,
    string Source);

internal sealed record AllTokensExportFile(
    DateTime ExportedAtUtc,
    string SourceDatabase,
    int Count,
    IReadOnlyList<TokenListItem> TokenEntries);

internal sealed record TokenListItem(
    int Id,
    string AccessToken,
    string RefreshToken,
    DateTime? AccessTokenExpiresAt,
    DateTime StoredExpiresAt,
    DateTime SavedAt,
    bool IsSuccess,
    string? ErrorMessage,
    int? ResponseStatusCode);
