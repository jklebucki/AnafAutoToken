using AnafAutoToken.Core.Services;
using AnafAutoToken.Infrastructure.Configuration;
using AnafAutoToken.Infrastructure.Data;
using AnafAutoToken.Shared.Configuration;
using AnafAutoToken.Worker;
using Microsoft.EntityFrameworkCore;
using Serilog;

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        path: "logs/anaf-auto-token-.txt",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    Log.Information("Starting ANAF Auto Token Worker Service");

    var builder = Host.CreateApplicationBuilder(args);

    // Add Serilog
    builder.Services.AddSerilog();

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
        ?? "Data Source=tokens.db";
    builder.Services.AddInfrastructure(connectionString);

    // Add Worker
    builder.Services.AddHostedService<Worker>();

    // Configure Windows Service (optional)
    if (OperatingSystem.IsWindows())
    {
        builder.Services.AddWindowsService(options =>
        {
            options.ServiceName = "ANAF Auto Token Service";
        });
    }

    // Configure Systemd Service (optional)
    if (OperatingSystem.IsLinux())
    {
        builder.Services.AddSystemd();
    }

    var host = builder.Build();

    // Ensure database is created and migrated
    using (var scope = host.Services.CreateScope())
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<AnafDbContext>();
        await dbContext.Database.MigrateAsync();
        Log.Information("Database migrated successfully");
    }

    await host.RunAsync();
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

