using AnafAutoToken.Shared.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.RegularExpressions;

namespace AnafAutoToken.Core.Services;

public partial class ConfigFileService(
    IOptions<AnafSettings> settings,
    ILogger<ConfigFileService> logger) : IConfigFileService
{
    private readonly AnafSettings _settings = settings.Value;

    public async Task<string> ReadAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(_settings.ConfigFilePath))
            {
                logger.LogError("Config file not found: {ConfigFilePath}", _settings.ConfigFilePath);
                throw new FileNotFoundException($"Config file not found: {_settings.ConfigFilePath}");
            }

            var content = await File.ReadAllTextAsync(_settings.ConfigFilePath, cancellationToken);
            var match = AccessTokenRegex().Match(content);

            if (!match.Success || match.Groups.Count < 2)
            {
                logger.LogError("Access token not found in config file");
                throw new InvalidOperationException("Access token not found in config file");
            }

            var token = match.Groups[1].Value.Trim();
            logger.LogInformation("Access token successfully read from config file");

            return token;
        }
        catch (Exception ex) when (ex is not FileNotFoundException and not InvalidOperationException)
        {
            logger.LogError(ex, "Error reading access token from config file");
            throw;
        }
    }

    public async Task UpdateAccessTokenAsync(string newAccessToken, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(_settings.ConfigFilePath))
            {
                logger.LogError("Config file not found: {ConfigFilePath}", _settings.ConfigFilePath);
                throw new FileNotFoundException($"Config file not found: {_settings.ConfigFilePath}");
            }

            var content = await File.ReadAllTextAsync(_settings.ConfigFilePath, cancellationToken);
            var updatedContent = AccessTokenRegex().Replace(
                content,
                $"[AcessToken]{Environment.NewLine}{newAccessToken}",
                1);

            await File.WriteAllTextAsync(_settings.ConfigFilePath, updatedContent, Encoding.UTF8, cancellationToken);

            logger.LogInformation("Access token successfully updated in config file");
        }
        catch (Exception ex) when (ex is not FileNotFoundException)
        {
            logger.LogError(ex, "Error updating access token in config file");
            throw;
        }
    }

    public async Task CreateBackupAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(_settings.ConfigFilePath))
            {
                logger.LogWarning("Config file not found for backup: {ConfigFilePath}", _settings.ConfigFilePath);
                return;
            }

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var backupFileName = $"bak_config_ini_{timestamp}.txt";
            var backupPath = Path.Combine(_settings.BackupDirectory, backupFileName);

            // Ensure backup directory exists
            Directory.CreateDirectory(_settings.BackupDirectory);

            await Task.Run(() => File.Copy(_settings.ConfigFilePath, backupPath, overwrite: false), cancellationToken);

            logger.LogInformation("Config file backed up to: {BackupPath}", backupPath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating backup of config file");
            throw;
        }
    }

    [GeneratedRegex(@"\[AcessToken\]\s*\r?\n(.+?)(?=\r?\n\[|$)", RegexOptions.Singleline)]
    private static partial Regex AccessTokenRegex();
}
