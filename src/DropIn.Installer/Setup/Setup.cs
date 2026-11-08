using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Win32;

namespace DropIn.Installer;

/// <summary>Registers/unregisters the .app file association and the directory-watcher service.</summary>
[SupportedOSPlatform("windows")]
static partial class Setup
{
    /// <summary>Reports whether the current process has administrator privileges.</summary>
    public static bool IsAdmin()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>Win32 error code returned when the user declines the UAC prompt.</summary>
    public const int UacDeclined = 1223;

    /// <summary>Why the last <see cref="Elevate"/> call could not start the elevated process, or null.</summary>
    public static string? LastElevationError { get; private set; }

    /// <summary>Re-runs this program elevated with the given arguments; returns its exit code.</summary>
    public static int Elevate(string args)
    {
        LastElevationError = null;
        try
        {
            using var p = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, args) { Verb = "runas", UseShellExecute = true });
            p!.WaitForExit();
            return p.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            LastElevationError = ex.Message;
            return ex.NativeErrorCode;
        }
    }

    /// <summary>Gets the path of the currently running executable.</summary>
    public static string Exe => Environment.ProcessPath!;

    /// <summary>Reports whether the DropIn watcher service is registered with Windows.</summary>
    public static bool IsServiceInstalled()
    {
        try { return System.ServiceProcess.ServiceController.GetServices().Any(s => s.ServiceName.Equals(WatcherService.ServiceId, StringComparison.OrdinalIgnoreCase)); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
    }

    /// <summary>Checks that the watcher uses this executable and the current user's data directory.</summary>
    public static bool IsServiceConfigured(string dataDirectory)
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + WatcherService.ServiceId);
        var imagePath = key?.GetValue("ImagePath") as string;
        var expected = $"\"{Exe}\" {InstallerConstants.ServiceSwitch} \"{dataDirectory}\"";
        return string.Equals(imagePath, expected, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Creates, describes, and starts the automatic watcher service.</summary>
    public static void InstallService(string dataDirectory)
    {
        // 1073 = the service already exists, 1056 = it is already running; both are fine.
        var binPath = $"\\\"{Exe}\\\" {InstallerConstants.ServiceSwitch} \\\"{dataDirectory}\\\"";
        Sc($"create {WatcherService.ServiceId} binPath= \"{binPath}\" start= auto DisplayName= \"{InstallerConstants.WatcherDisplayName}\"", 1073);
        using (var service = new System.ServiceProcess.ServiceController(WatcherService.ServiceId))
        {
            if (service.Status != System.ServiceProcess.ServiceControllerStatus.Stopped)
            {
                if (service.Status != System.ServiceProcess.ServiceControllerStatus.StopPending)
                    service.Stop();
                service.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
            }
        }
        Sc($"config {WatcherService.ServiceId} binPath= \"{binPath}\" start= auto");
        Sc($"description {WatcherService.ServiceId} \"{InstallerConstants.WatcherDescription}\"");
        Sc($"start {WatcherService.ServiceId}", 1056);
    }

    /// <summary>Stops and removes the watcher service.</summary>
    public static void UninstallService()
    {
        // 1060 = the service does not exist, 1062 = it is not running; both mean there is nothing to undo.
        Sc($"stop {WatcherService.ServiceId}", 1062, 1060);
        Sc($"delete {WatcherService.ServiceId}", 1060);
    }

    /// <summary>Runs a Service Control utility command and waits for it to exit.</summary>
    /// <param name="args">Arguments for sc.exe.</param>
    /// <param name="tolerated">Exit codes that are not treated as failures.</param>
    static void Sc(string args, params int[] tolerated)
    {
        using var p = Process.Start(new ProcessStartInfo(InstallerConstants.ServiceControlExecutable, args) { UseShellExecute = false, CreateNoWindow = true });
        p?.WaitForExit();
        if (p is { ExitCode: not 0 } && !tolerated.Contains(p.ExitCode))
            throw new InvalidOperationException($"Service command \"sc {args.Split(' ')[0]}\" failed with exit code {p.ExitCode}.");
    }

    /// <summary>Notifies Windows shell components that file associations have changed.</summary>
    [System.Runtime.InteropServices.DllImport("shell32.dll")]
    static extern void SHChangeNotify(int eventId, uint flags, IntPtr a, IntPtr b);
    /// <summary>Broadcasts an association-change notification to refresh the shell.</summary>
    static void RefreshShell() => SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
}
