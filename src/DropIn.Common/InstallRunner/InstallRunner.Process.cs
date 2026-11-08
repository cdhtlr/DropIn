using System.Diagnostics;

namespace DropIn.Common;

/// <summary>Installer process launching and filename helpers.</summary>
public static partial class InstallRunner
{
    /// <summary>Starts an installer with the selected silent switches and waits for its result.</summary>
    /// <param name="file">Installer path.</param>
    /// <param name="type">Normalized installer type.</param>
    /// <param name="switches">Silent command-line switches.</param>
    /// <param name="dir">Requested installation directory.</param>
    /// <param name="cfg">Configuration supplying timeout and service behavior.</param>
    /// <param name="progress">Receiver told that an external installer is running (its progress cannot be measured).</param>
    static InstallResult Run(string file, string type, string switches, string dir, DropInConfig cfg, IProgress<InstallProgressInfo>? progress = null)
    {
        // MSIX packages have no target-directory option, so only the other setups are checked for a wrong install location.
        if (type == "msix") return RunSetup(file, type, switches, dir, cfg, progress);
        var before = SnapshotLocations();
        return WithLocationNote(RunSetup(file, type, switches, dir, cfg, progress), dir, before);
    }

    /// <summary>Runs the installer process once; see <see cref="Run"/>.</summary>
    static InstallResult RunSetup(string file, string type, string switches, string dir, DropInConfig cfg, IProgress<InstallProgressInfo>? progress)
    {
        ProcessStartInfo psi;
        var msi = type == "msi" ? PrepareMsi(file, cfg) : MsiPlan.None;
        if (type == "msi")
        {
            psi = new ProcessStartInfo("msiexec.exe")
            {
                Arguments = $"/i \"{file}\" {switches} {msi.ExtraArguments} {Silent.DirSwitch("msi", dir)}",
                UseShellExecute = true
            };
            if (!NonInteractive) psi.Verb = "runas";
        }
        else if (type == "msix")
        {
            psi = new ProcessStartInfo("powershell.exe")
            {
                Arguments = $"-NoProfile -Command \"Add-AppxPackage -Path '{file.Replace("'", "''")}'\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
        }
        else
        {
            // A generic "exe" is inspected so Inno Setup and NSIS installers also install into the target directory instead of their own default (such as AppData).
            var kind = type == "exe" ? InstallerSniffer.Detect(file) ?? type : type;
            var dirSw = Silent.DirSwitch(kind, dir);
            if (dirSw != null && switches.Contains("/DIR=", StringComparison.OrdinalIgnoreCase)) dirSw = null;
            psi = new ProcessStartInfo(file)
            {
                Arguments = dirSw == null ? switches : $"{switches} {dirSw}",
                UseShellExecute = false,
                CreateNoWindow = true
            };
        }
        // The service runs as SYSTEM, but installs always run as the logged-on user so Start menu items and the Apps entry belong to that user.
        if (NonInteractive && OperatingSystem.IsWindows())
        {
            if (!InteractiveSession.HasActiveUser)
                return new InstallResult(false, "No user is logged on; installers are only run as the logged-on user, never as SYSTEM.");
            var exe = type == "msi" ? Path.Combine(Environment.SystemDirectory, "msiexec.exe")
                : type == "msix" ? Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe") : file;
            progress?.Report(new InstallProgressInfo(InstallProgress.PackageExtracted, "Running installer", Estimated: true));
            var timeout = (int)Math.Min(int.MaxValue, cfg.Installer.InstallerTimeoutMinutes * 60_000L);
            // MSI always asks for elevation (like the interactive path); other setups ask only when they require it.
            var code = InteractiveSession.RunHiddenAsUser(exe, psi.Arguments, timeout, type == "msi", out var needsElevation);
            if (code == null && needsElevation) code = InteractiveSession.RunHiddenAsUser(exe, psi.Arguments, timeout, true, out _);
            if (code is 0 or 3010 or 1641) FinishMsi(msi);
            return code switch
            {
                0 or 3010 or 1641 => new InstallResult(true, "Installation completed."),
                null => new InstallResult(false, "The installer could not be run in the user session or did not finish in time."),
                _ => new InstallResult(false, $"Installation failed (exit code {code}).")
            };
        }
        try
        {
            using var p = Process.Start(psi) ?? throw new InvalidOperationException("process did not start");
            progress?.Report(new InstallProgressInfo(InstallProgress.PackageExtracted, "Running installer", Estimated: true));
            // A configured timeout prevents a silent installer from blocking the host indefinitely.
            if (cfg.Installer.InstallerTimeoutMinutes > 0)
            {
                if (!p.WaitForExit(TimeSpan.FromMinutes(cfg.Installer.InstallerTimeoutMinutes)))
                {
                    try { p.Kill(true); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                    return new InstallResult(false, $"The installer did not finish within {cfg.Installer.InstallerTimeoutMinutes} minute(s) and was stopped.");
                }
            }
            else p.WaitForExit();
            // 0 = ok, 3010 = ok but reboot required, 1641 = reboot initiated
            if (p.ExitCode is 0 or 3010 or 1641)
            {
                FinishMsi(msi);
                return new InstallResult(true, "Installation completed.");
            }
            return new InstallResult(false, $"Installation failed (exit code {p.ExitCode}).");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new InstallResult(false, $"Could not start installer: {ex.Message}");
        }
    }

    /// <summary>Opens an installer with its own UI, using the active desktop when running as a service.</summary>
    /// <param name="file">Installer path.</param>
    /// <param name="waitForExit">Whether to wait for completion and return the installer exit result.</param>
    public static InstallResult LaunchInteractive(string file, bool waitForExit = false)
    {
        if (NonInteractive && OperatingSystem.IsWindows())
            return InteractiveSession.Launch(file, waitForExit);
        try
        {
            using var process = Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
            if (process == null) return new InstallResult(false, "Could not start the installer.");
            if (!waitForExit) return new InstallResult(false, "Launched the installer in interactive mode.");
            process.WaitForExit();
            return process.ExitCode is 0 or 3010 or 1641
                ? new InstallResult(true, "Interactive installation completed successfully.")
                : new InstallResult(false, $"Interactive installer exited with code {process.ExitCode}.");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            return new InstallResult(false, "Could not start installer interactively: " + ex.Message);
        }
    }

    /// <summary>Makes a pattern value usable as one folder name (separators become underscores).</summary>
    /// <param name="value">Package name, version or source text.</param>
    static string Token(string? value)
    {
        var text = (value ?? "").Trim();
        foreach (var c in Path.GetInvalidFileNameChars()) text = text.Replace(c, '_');
        return text;
    }

    /// <summary>Maps a manifest source to winget, chocolatey, or other when it is not recognized.</summary>
    /// <param name="source">Source text from the manifest.</param>
    public static string SourceName(string? source)
    {
        var s = (source ?? "").Trim();
        if (s.Equals("winget", StringComparison.OrdinalIgnoreCase)) return "winget";
        if (s.Equals("chocolatey", StringComparison.OrdinalIgnoreCase) || s.Equals("choco", StringComparison.OrdinalIgnoreCase)) return "chocolatey";
        return CommonDefaults.OtherSource;
    }

    /// <summary>Turns a folder pattern result into a safe relative path: each "\" or "/" separated segment is cleaned, empty segments are dropped, and a nonempty fallback is provided.</summary>
    /// <param name="name">Candidate relative directory path.</param>
    static string SafeName(string name)
    {
        var parts = new List<string>();
        foreach (var raw in name.Split(new[] { '\\', '/' }))
        {
            var part = raw;
            foreach (var c in Path.GetInvalidFileNameChars()) part = part.Replace(c, '_');
            // Empty tokens leave dangling separators such as "Putty-"; trim them so names stay tidy.
            part = part.Trim().Trim('.').TrimEnd('-', '_', ' ');
            if (part.Length > 0) parts.Add(part);
        }
        return parts.Count == 0 ? CommonDefaults.InstallFolderName : string.Join(Path.DirectorySeparatorChar, parts);
    }
}
