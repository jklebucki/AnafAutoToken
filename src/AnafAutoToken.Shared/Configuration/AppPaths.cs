namespace AnafAutoToken.Shared.Configuration;

/// <summary>
/// Jedno miejsce, w którym wszystkie programy (worker, menedżer) ustalają, gdzie leżą dane
/// i konfiguracja. Katalog jest wyliczany raz i nie zależy od katalogu roboczego procesu -
/// usługa Windows startuje z C:\Windows\System32, więc jakakolwiek ścieżka względna albo
/// oparta o CWD prowadziła do tego, że usługa nie widziała własnej konfiguracji.
/// </summary>
public static class AppPaths
{
    /// <summary>Nadpisanie katalogu danych - używane w testach i przy nietypowych instalacjach.</summary>
    public const string DataDirectoryEnvironmentVariable = "ANAFAUTOTOKEN_DATA_DIR";

    public const string SettingsFileName = "appsettings.json";
    public const string DatabaseFileName = "tokens.db";

    private static readonly Lazy<string> LazyDataDirectory = new(ResolveDataDirectory);

    public static string DataDirectory => LazyDataDirectory.Value;

    public static string SettingsFile => Path.Combine(DataDirectory, SettingsFileName);

    public static string DatabaseFile => Path.Combine(DataDirectory, DatabaseFileName);

    public static string BackupDirectory => Path.Combine(DataDirectory, "backups");

    public static string LogDirectory => Path.Combine(DataDirectory, "logs");

    public static string DefaultConnectionString => $"Data Source={DatabaseFile}";

    /// <summary>Tworzy katalog danych wraz z podkatalogami. Idempotentne.</summary>
    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(BackupDirectory);
        Directory.CreateDirectory(LogDirectory);
    }

    private static string ResolveDataDirectory()
    {
        var overridden = Environment.GetEnvironmentVariable(DataDirectoryEnvironmentVariable);

        if (!string.IsNullOrWhiteSpace(overridden))
        {
            return Path.GetFullPath(overridden);
        }

        if (OperatingSystem.IsWindows())
        {
            // CommonApplicationData to C:\ProgramData
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "AnafAutoToken");
        }

        return "/var/lib/anafautotoken";
    }
}
