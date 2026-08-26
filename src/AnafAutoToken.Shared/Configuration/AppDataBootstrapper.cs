using System.Text.Json;
using System.Text.Json.Nodes;

namespace AnafAutoToken.Shared.Configuration;

public sealed record AppDataBootstrapResult(
    string DataDirectory,
    bool CreatedDataDirectory,
    bool CreatedSettingsFile,
    string? SeededFrom);

/// <summary>
/// Przygotowuje katalog danych do pierwszego użycia: tworzy podkatalogi i zakłada
/// <c>appsettings.json</c>, jeśli go nie ma. Istniejącego pliku nigdy nie nadpisuje -
/// konfiguracja w katalogu danych jest jedynym źródłem prawdy i musi przeżyć wdrożenie
/// nowej wersji programów.
/// </summary>
public static class AppDataBootstrapper
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public static AppDataBootstrapResult Ensure(string? seedSettingsFile = null)
    {
        var createdDataDirectory = !Directory.Exists(AppPaths.DataDirectory);

        AppPaths.EnsureDirectories();

        if (File.Exists(AppPaths.SettingsFile))
        {
            return new AppDataBootstrapResult(
                AppPaths.DataDirectory,
                createdDataDirectory,
                CreatedSettingsFile: false,
                SeededFrom: null);
        }

        var seedPath = ResolveSeedFile(seedSettingsFile);
        var settings = LoadSeed(seedPath) ?? CreateDefaultSettings();

        ApplyDataDirectoryDefaults(settings);

        File.WriteAllText(AppPaths.SettingsFile, settings.ToJsonString(WriteOptions));

        return new AppDataBootstrapResult(
            AppPaths.DataDirectory,
            createdDataDirectory,
            CreatedSettingsFile: true,
            SeededFrom: seedPath);
    }

    /// <summary>Wzorzec konfiguracji z placeholderami do uzupełnienia w menedżerze.</summary>
    public static JsonObject CreateDefaultSettings() => new()
    {
        ["Anaf"] = new JsonObject
        {
            ["TokenEndpoint"] = "https://logincert.anaf.ro/anaf-oauth2/v1/token",
            ["BasicAuth"] = new JsonObject
            {
                ["Username"] = "<ANAF_BASIC_AUTH_USERNAME>",
                ["Password"] = "<ANAF_BASIC_AUTH_PASSWORD>"
            },
            ["CheckSchedule"] = new JsonObject
            {
                ["CheckHour"] = 12,
                ["CheckMinute"] = 0
            },
            ["DaysBeforeExpiration"] = 3,
            ["ConfigFilePath"] = "c:\\tmp\\config.ini",
            ["BackupDirectory"] = AppPaths.BackupDirectory,
            ["InitialRefreshToken"] = "<INITIAL_REFRESH_TOKEN>",
            ["Email"] = new JsonObject
            {
                ["SmtpServer"] = "<SMTP_SERVER>",
                ["SmtpPort"] = 587,
                ["Username"] = "<SMTP_USERNAME>",
                ["Password"] = "<SMTP_PASSWORD>",
                ["FromAddress"] = "<FROM_ADDRESS>",
                ["FromName"] = "ANAF Auto Token Service",
                ["ToAddresses"] = new JsonArray("admin@example.com"),
                ["EnableSsl"] = true
            }
        },
        ["ConnectionStrings"] = new JsonObject
        {
            ["TokenDatabase"] = AppPaths.DefaultConnectionString
        },
        ["Api"] = new JsonObject
        {
            // 0.0.0.0 = wszystkie interfejsy. Gwiazdka i plus tez oznaczaja "dowolny adres",
            // ale nie parsuja sie jako Uri, wiec menedzer nie zlozylby z nich adresu zadania.
            ["Url"] = "http://0.0.0.0:5099",
            // Pusta lista = dostep wylacznie z tej maszyny. Petla zwrotna jest dozwolona zawsze.
            ["AllowedNetworks"] = new JsonArray("192.168.21.0/24", "100.100.0.0/24", "192.168.29.0/24")
        },
        ["Logging"] = new JsonObject
        {
            ["LogLevel"] = new JsonObject
            {
                ["Default"] = "Information",
                ["Microsoft.Hosting.Lifetime"] = "Information",
                ["Microsoft.EntityFrameworkCore"] = "Warning"
            }
        }
    };

    private static string? ResolveSeedFile(string? explicitSeed)
    {
        if (!string.IsNullOrWhiteSpace(explicitSeed) && File.Exists(explicitSeed))
        {
            return Path.GetFullPath(explicitSeed);
        }

        // Plik obok pliku wykonywalnego traktujemy jako wzorzec z wdrożenia.
        var next = Path.Combine(AppContext.BaseDirectory, AppPaths.SettingsFileName);
        return File.Exists(next) ? next : null;
    }

    private static JsonObject? LoadSeed(string? path)
    {
        if (path is null)
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
        }
        catch (Exception)
        {
            // Uszkodzony wzorzec nie może zablokować startu - wtedy bierzemy domyślny.
            return null;
        }
    }

    /// <summary>
    /// Ścieżki, które muszą wskazywać katalog danych, ustawiamy niezależnie od tego,
    /// co było we wzorcu - inaczej wdrożeniowy appsettings.json wniósłby względne
    /// "Data Source=tokens.db" albo ścieżkę z maszyny deweloperskiej.
    /// </summary>
    private static void ApplyDataDirectoryDefaults(JsonObject settings)
    {
        var connectionStrings = settings["ConnectionStrings"] as JsonObject;

        if (connectionStrings is null)
        {
            connectionStrings = [];
            settings["ConnectionStrings"] = connectionStrings;
        }

        connectionStrings["TokenDatabase"] = AppPaths.DefaultConnectionString;

        if (settings["Anaf"] is JsonObject anaf)
        {
            anaf["BackupDirectory"] = AppPaths.BackupDirectory;
        }
    }
}
