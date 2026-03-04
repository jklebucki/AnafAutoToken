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
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serilog;
using System.Text.Json;

// Configure Serilog
var logPath = Path.Combine(
    AppContext.BaseDirectory,
    "logs",
    "anaf-auto-token-.txt"
);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        path: logPath,
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();


try
{
    Log.Information("Starting ANAF Auto Token Worker Service");

    var builder = WebApplication.CreateBuilder(args);
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

    // Configure AnafSettings
    builder.Services.Configure<AnafSettings>(
        builder.Configuration.GetSection("Anaf"));

    // Add Core Services
    builder.Services.AddScoped<ITokenService, TokenService>();
    builder.Services.AddScoped<ITokenValidationService, TokenValidationService>();
    builder.Services.AddScoped<IConfigFileService, ConfigFileService>();
    builder.Services.AddScoped<IEmailNotificationService, EmailNotificationService>();

    // Add Infrastructure
    var connectionString = builder.Configuration.GetConnectionString("TokenDatabase")
        ?? "Data Source=C:\\ProgramData\\AnafAutoToken\\tokens.db";
    EnsureDatabaseDirectoryExists(connectionString);
    builder.Services.AddInfrastructure(connectionString);

    // Add Worker
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
        IConfiguration configuration,
        CancellationToken cancellationToken) =>
    {
        try
        {
            var payload = await BuildCurrentTokenExportAsync(
                serviceScopeFactory,
                configuration,
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

static void EnsureDatabaseDirectoryExists(string connectionString)
{
    var sqliteBuilder = new SqliteConnectionStringBuilder(connectionString);

    if (string.IsNullOrWhiteSpace(sqliteBuilder.DataSource))
    {
        return;
    }

    var fullPath = Path.IsPathRooted(sqliteBuilder.DataSource)
        ? Path.GetFullPath(sqliteBuilder.DataSource)
        : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, sqliteBuilder.DataSource));

    var directoryPath = Path.GetDirectoryName(fullPath);

    if (!string.IsNullOrWhiteSpace(directoryPath))
    {
        Directory.CreateDirectory(directoryPath);
    }
}

static async Task<CurrentTokenExportFile> BuildCurrentTokenExportAsync(
    IServiceScopeFactory serviceScopeFactory,
    IConfiguration configuration,
    CancellationToken cancellationToken)
{
    using var scope = serviceScopeFactory.CreateScope();

    var tokenRepository = scope.ServiceProvider.GetRequiredService<ITokenRepository>();
    var configFileService = scope.ServiceProvider.GetRequiredService<IConfigFileService>();
    var settings = scope.ServiceProvider.GetRequiredService<IOptions<AnafSettings>>().Value;
    var sourceDatabasePath = ResolveDatabasePath(configuration);

    var latestLog = await tokenRepository.GetLatestSuccessfulLogAsync(cancellationToken);

    if (latestLog is not null && !string.IsNullOrWhiteSpace(latestLog.AccessToken))
    {
        return new CurrentTokenExportFile(
            ExportedAtUtc: DateTime.UtcNow,
            SourceDatabase: sourceDatabasePath,
            CurrentToken: new CurrentTokenPayload(
                latestLog.AccessToken,
                latestLog.RefreshToken,
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
        SourceDatabase: File.Exists(sourceDatabasePath) ? sourceDatabasePath : null,
        CurrentToken: new CurrentTokenPayload(
            accessToken,
            settings.InitialRefreshToken,
            accessToken.GetExpirationDate(),
            StoredExpiresAt: null,
            SavedAt: null,
            Source: "config-and-settings"));
}

static string ResolveDatabasePath(IConfiguration configuration)
{
    var connectionString = configuration.GetConnectionString("TokenDatabase")
        ?? "Data Source=C:\\ProgramData\\AnafAutoToken\\tokens.db";

    var sqliteBuilder = new SqliteConnectionStringBuilder(connectionString);

    if (string.IsNullOrWhiteSpace(sqliteBuilder.DataSource))
    {
        sqliteBuilder.DataSource = "C:\\ProgramData\\AnafAutoToken\\tokens.db";
    }

    return Path.IsPathRooted(sqliteBuilder.DataSource)
        ? Path.GetFullPath(sqliteBuilder.DataSource)
        : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, sqliteBuilder.DataSource));
}

