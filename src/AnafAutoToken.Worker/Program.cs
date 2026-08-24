using AnafAutoToken.Core.Interfaces;
using AnafAutoToken.Core.Services;
using AnafAutoToken.Infrastructure.Configuration;
using AnafAutoToken.Infrastructure.Data;
using AnafAutoToken.Shared.Configuration;
using AnafAutoToken.Shared.Extensions;
using AnafAutoToken.Shared.Models;
using AnafAutoToken.Worker;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Options;
using Serilog;
using System.Text.Json;

// Katalog danych musi istnieć zanim cokolwiek zacznie z niego czytać - także logger.
var bootstrap = AppDataBootstrapper.Ensure();

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        path: Path.Combine(AppPaths.LogDirectory, "anaf-auto-token-.txt"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    Log.Information("Starting ANAF Auto Token Worker Service");
    Log.Information("Data directory: {DataDirectory}", bootstrap.DataDirectory);

    if (bootstrap.CreatedSettingsFile)
    {
        Log.Warning(
            "Created a new settings file at {SettingsFile}{SeedInfo}. Review it before relying on the service",
            AppPaths.SettingsFile,
            bootstrap.SeededFrom is null ? string.Empty : $" (seeded from {bootstrap.SeededFrom})");
    }

    var builder = WebApplication.CreateBuilder(new WebApplicationOptions
    {
        Args = args,
        // Bez tego ContentRoot to katalog roboczy procesu. Usługa Windows startuje
        // z C:\Windows\System32, więc appsettings.json obok pliku wykonywalnego nigdy
        // nie był wczytywany, a cała sekcja Anaf zostawała pusta.
        ContentRootPath = AppContext.BaseDirectory
    });

    // Plik w katalogu danych jest JEDYNYM plikowym zrodlem konfiguracji. appsettings.json
    // obok pliku wykonywalnego sluzy wylacznie za wzorzec przy pierwszym uruchomieniu -
    // gdyby zostal jako warstwa nizej, klucz usuniety w menedzerze wracalby z wdrozenia,
    // a stare dane (np. adresy SMTP) cicho przezylyby zmiane konfiguracji.
    foreach (var jsonSource in builder.Configuration.Sources.OfType<JsonConfigurationSource>().ToList())
    {
        builder.Configuration.Sources.Remove(jsonSource);
    }

    builder.Configuration.AddJsonFile(AppPaths.SettingsFile, optional: false, reloadOnChange: false);

    // Zmienne srodowiskowe i argumenty dokladamy ponownie, zeby zostaly nadrzedne.
    builder.Configuration.AddEnvironmentVariables();

    if (args.Length > 0)
    {
        builder.Configuration.AddCommandLine(args);
    }

    var apiJsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = null,
        WriteIndented = true
    };

    // Add Serilog
    builder.Services.AddSerilog();

    // Configure API host
    var apiUrl = builder.Configuration["Api:Url"] ?? "http://127.0.0.1:5099";
    builder.WebHost.UseUrls(apiUrl);

    // Configure AnafSettings. Walidacja na starcie zamienia ciche NullReference
    // przy pierwszym tyknięciu harmonogramu na czytelny błąd startu usługi.
    builder.Services.AddOptions<AnafSettings>()
        .Bind(builder.Configuration.GetSection("Anaf"))
        .Validate(
            settings => settings.CheckSchedule is not null,
            "Brak sekcji Anaf:CheckSchedule w konfiguracji.")
        .Validate(
            settings => settings.BasicAuth is not null,
            "Brak sekcji Anaf:BasicAuth w konfiguracji.")
        .Validate(
            settings => !string.IsNullOrWhiteSpace(settings.TokenEndpoint),
            "Brak wartości Anaf:TokenEndpoint w konfiguracji.")
        .Validate(
            settings => !string.IsNullOrWhiteSpace(settings.ConfigFilePath),
            "Brak wartości Anaf:ConfigFilePath w konfiguracji.")
        .ValidateOnStart();

    // Add Core Services
    builder.Services.AddScoped<ITokenService, TokenService>();
    builder.Services.AddScoped<ITokenValidationService, TokenValidationService>();
    builder.Services.AddScoped<IConfigFileService, ConfigFileService>();
    builder.Services.AddScoped<IEmailNotificationService, EmailNotificationService>();
    builder.Services.AddScoped<IRefreshResponseArchive, RefreshResponseArchive>();

    // Add Infrastructure
    var connectionString = TokenDatabase.ResolveConnectionString(
        builder.Configuration.GetConnectionString("TokenDatabase"));
    var databasePath = TokenDatabase.ResolveDatabasePath(connectionString);
    Log.Information("Token database: {DatabasePath}", databasePath);
    builder.Services.AddInfrastructure(connectionString);

    // Add Worker
    builder.Services.AddSingleton<TokenRefreshCoordinator>();
    builder.Services.AddHostedService<Worker>();

    // Configure Windows Service (optional)
    if (OperatingSystem.IsWindows())
    {
        builder.Host.UseWindowsService(options =>
        {
            options.ServiceName = "ANAF Auto Token Service";
        });
    }

    // Configure Systemd Service (optional)
    if (OperatingSystem.IsLinux())
    {
        builder.Host.UseSystemd();
    }

    var app = builder.Build();

    app.MapGet("/api/tokens/current", async (
        IServiceScopeFactory serviceScopeFactory,
        CancellationToken cancellationToken) =>
    {
        try
        {
            var payload = await BuildCurrentTokenExportAsync(
                serviceScopeFactory,
                databasePath,
                cancellationToken);

            return Results.Json(payload, apiJsonOptions);
        }
        catch (InvalidOperationException ex)
        {
            return Results.Problem(
                title: "Current tokens are not available.",
                detail: ex.Message,
                statusCode: StatusCodes.Status404NotFound);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to build current token API response");

            return Results.Problem(
                title: "Token query failed.",
                detail: "Failed to retrieve current tokens.",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    });

    app.MapPost("/api/tokens/refresh", async (
        TokenRefreshCoordinator coordinator,
        IServiceScopeFactory serviceScopeFactory,
        CancellationToken cancellationToken) =>
    {
        Log.Information("Manual token refresh requested through the API");

        var startedAtUtc = DateTime.UtcNow;

        try
        {
            var result = await coordinator.TryRunAsync(
                async token =>
                {
                    using var scope = serviceScopeFactory.CreateScope();
                    var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
                    return await tokenService.CheckAndRefreshTokenIfNeededAsync(TokenCheckTrigger.Manual, token);
                },
                cancellationToken);

            if (result is null)
            {
                Log.Warning("Manual token refresh rejected - another refresh is already running");

                return Results.Problem(
                    title: "Odświeżanie tokenu już trwa.",
                    detail: "Inna operacja odświeżania jest w toku. Spróbuj ponownie za chwilę.",
                    statusCode: StatusCodes.Status409Conflict);
            }

            Log.Information(
                "Manual token refresh finished. Success: {IsSuccess}, refreshed: {TokenWasRefreshed}",
                result.IsSuccess,
                result.TokenWasRefreshed);

            return Results.Json(
                new ManualTokenRefreshResponse(
                    result.IsSuccess,
                    result.TokenWasRefreshed,
                    result.NewExpirationDate,
                    result.ErrorMessage,
                    startedAtUtc,
                    DateTime.UtcNow),
                apiJsonOptions);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Manual token refresh failed");

            return Results.Problem(
                title: "Odświeżanie tokenu nie powiodło się.",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    });

    // Ensure database is created and migrated
    using (var scope = app.Services.CreateScope())
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<AnafDbContext>();
        await dbContext.Database.MigrateAsync();
        Log.Information("Database migrated successfully");
    }

    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}

static async Task<CurrentTokenExportFile> BuildCurrentTokenExportAsync(
    IServiceScopeFactory serviceScopeFactory,
    string databasePath,
    CancellationToken cancellationToken)
{
    using var scope = serviceScopeFactory.CreateScope();

    var tokenRepository = scope.ServiceProvider.GetRequiredService<ITokenRepository>();
    var configFileService = scope.ServiceProvider.GetRequiredService<IConfigFileService>();
    var settings = scope.ServiceProvider.GetRequiredService<IOptions<AnafSettings>>().Value;

    var latestLog = await tokenRepository.GetLatestSuccessfulLogAsync(cancellationToken);

    if (latestLog is not null && !string.IsNullOrWhiteSpace(latestLog.AccessToken))
    {
        return new CurrentTokenExportFile(
            ExportedAtUtc: DateTime.UtcNow,
            SourceDatabase: databasePath,
            CurrentToken: new CurrentTokenPayload(
                latestLog.AccessToken,
                latestLog.RefreshToken,
                latestLog.RefreshToken.GetExpirationDate() ?? latestLog.RefreshTokenExpiresAt ?? latestLog.CreatedAt.AddDays(365),
                latestLog.AccessToken.GetExpirationDate(),
                latestLog.ExpiresAt,
                latestLog.CreatedAt,
                "database"));
    }

    string? accessToken = null;

    try
    {
        accessToken = await configFileService.ReadAccessTokenAsync(cancellationToken);
    }
    catch (Exception ex) when (ex is FileNotFoundException or InvalidOperationException)
    {
        accessToken = null;
    }

    if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(settings.InitialRefreshToken))
    {
        throw new InvalidOperationException("No current token could be resolved from the database or configuration files.");
    }

    return new CurrentTokenExportFile(
        ExportedAtUtc: DateTime.UtcNow,
        SourceDatabase: File.Exists(databasePath) ? databasePath : null,
        CurrentToken: new CurrentTokenPayload(
            accessToken,
            settings.InitialRefreshToken,
            settings.InitialRefreshToken.GetExpirationDate(),
            accessToken.GetExpirationDate(),
            StoredExpiresAt: null,
            SavedAt: null,
            Source: "config-and-settings"));
}
