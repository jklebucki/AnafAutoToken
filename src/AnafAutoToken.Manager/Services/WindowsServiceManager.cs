using System.Diagnostics;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using Microsoft.Win32;

namespace AnafAutoToken.Manager.Services;

/// <summary>
/// Thin wrapper over the Windows service control manager.
/// Status, start and stop go through <see cref="ServiceController"/>; registering and
/// removing a service is not exposed by that API, so those two use <c>sc.exe</c> - the
/// same tool the installation scripts drive.
/// </summary>
internal static class WindowsServiceManager
{
    private static readonly TimeSpan TransitionTimeout = TimeSpan.FromSeconds(30);

    public static bool IsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static ServiceSnapshot Query(string serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            return ServiceSnapshot.NotInstalled;
        }

        try
        {
            using var controller = new ServiceController(serviceName);

            // Touching Status is what actually hits the SCM and throws when the
            // service does not exist - the constructor alone never fails.
            var status = controller.Status;

            return new ServiceSnapshot(
                IsInstalled: true,
                Status: status,
                StartType: controller.StartType,
                DisplayName: controller.DisplayName,
                BinaryPath: ReadBinaryPath(serviceName));
        }
        catch (InvalidOperationException)
        {
            return ServiceSnapshot.NotInstalled;
        }
    }

    public static void Register(string serviceName, string displayName, string description, string binaryPath)
    {
        if (!File.Exists(binaryPath))
        {
            throw new FileNotFoundException(
                $"Nie znaleziono pliku wykonywalnego: {binaryPath}{Environment.NewLine}{Environment.NewLine}"
                + "AnafAutoToken.Worker.exe powinien leżeć w tym samym katalogu co menedżer. "
                + "Jeśli trzymasz go gdzie indziej, wskaż go przyciskiem obok pola "
                + "„Plik wykonywalny workera”.",
                binaryPath);
        }

        RunServiceControl(
            "create", serviceName,
            "binPath=", binaryPath,
            "start=", "auto",
            "DisplayName=", displayName);

        if (!string.IsNullOrWhiteSpace(description))
        {
            RunServiceControl("description", serviceName, description);
        }

        // Restart three times, one minute apart, then reset the failure counter daily -
        // identical to what install-windows-service.ps1 configures.
        RunServiceControl(
            "failure", serviceName,
            "reset=", "86400",
            "actions=", "restart/60000/restart/60000/restart/60000");
    }

    public static void Unregister(string serviceName) => RunServiceControl("delete", serviceName);

    public static void Start(string serviceName)
    {
        using var controller = new ServiceController(serviceName);

        if (controller.Status is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending)
        {
            return;
        }

        controller.Start();
        controller.WaitForStatus(ServiceControllerStatus.Running, TransitionTimeout);
    }

    public static void Stop(string serviceName)
    {
        using var controller = new ServiceController(serviceName);

        if (controller.Status is ServiceControllerStatus.Stopped or ServiceControllerStatus.StopPending)
        {
            return;
        }

        if (!controller.CanStop)
        {
            throw new InvalidOperationException($"Serwis {serviceName} nie pozwala się w tej chwili zatrzymać.");
        }

        controller.Stop();
        controller.WaitForStatus(ServiceControllerStatus.Stopped, TransitionTimeout);
    }

    public static void Restart(string serviceName)
    {
        Stop(serviceName);
        Start(serviceName);
    }

    private static string? ReadBinaryPath(string serviceName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}");
            return key?.GetValue("ImagePath") as string;
        }
        catch (Exception)
        {
            // Reading the path is a convenience for the operator, never a hard requirement.
            return null;
        }
    }

    private static void RunServiceControl(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("sc.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Nie udało się uruchomić sc.exe.");

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode == 0)
        {
            return;
        }

        var details = string.Join(
            Environment.NewLine,
            new[] { output, error }
                .Select(text => text.Trim())
                .Where(text => text.Length > 0));

        var message = $"sc.exe {arguments[0]} zakończone kodem {process.ExitCode}.";

        // 5 = ERROR_ACCESS_DENIED, 1073 = ERROR_SERVICE_EXISTS, 1060 = ERROR_SERVICE_DOES_NOT_EXIST
        message += process.ExitCode switch
        {
            5 => " Brak uprawnień - uruchom menedżera jako Administrator.",
            1073 => " Serwis o tej nazwie już istnieje.",
            1060 => " Serwis o tej nazwie nie istnieje.",
            _ => string.Empty
        };

        throw new InvalidOperationException(
            details.Length > 0 ? $"{message}{Environment.NewLine}{details}" : message);
    }
}

internal sealed record ServiceSnapshot(
    bool IsInstalled,
    ServiceControllerStatus? Status,
    ServiceStartMode? StartType,
    string? DisplayName,
    string? BinaryPath)
{
    public static readonly ServiceSnapshot NotInstalled = new(false, null, null, null, null);

    public bool IsRunning => Status == ServiceControllerStatus.Running;

    public bool IsStopped => Status == ServiceControllerStatus.Stopped;

    public bool IsTransitioning => Status is ServiceControllerStatus.StartPending
        or ServiceControllerStatus.StopPending
        or ServiceControllerStatus.ContinuePending
        or ServiceControllerStatus.PausePending;

    public string StatusText => !IsInstalled
        ? "NIE ZAREJESTROWANY"
        : Status switch
        {
            ServiceControllerStatus.Running => "DZIAŁA",
            ServiceControllerStatus.Stopped => "ZATRZYMANY",
            ServiceControllerStatus.StartPending => "URUCHAMIANIE…",
            ServiceControllerStatus.StopPending => "ZATRZYMYWANIE…",
            ServiceControllerStatus.Paused => "WSTRZYMANY",
            ServiceControllerStatus.PausePending => "WSTRZYMYWANIE…",
            ServiceControllerStatus.ContinuePending => "WZNAWIANIE…",
            _ => "NIEZNANY"
        };

    public string StartTypeText => StartType switch
    {
        ServiceStartMode.Automatic => "automatyczny",
        ServiceStartMode.Manual => "ręczny",
        ServiceStartMode.Disabled => "wyłączony",
        ServiceStartMode.Boot => "boot",
        ServiceStartMode.System => "system",
        _ => "-"
    };
}
