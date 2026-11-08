using System.Runtime.InteropServices;
using DropIn.Common;

namespace DropIn.Installer;

/// <summary>Performs the configured actions after a package installation completes.</summary>
internal static class PostInstallActions
{
    /// <summary>Displays the native message box used for post-install prompts.</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    /// <summary>Follows AfterInstall: opens the folder, offers to run a lone .exe, or does nothing.</summary>
    internal static void ShowInstalled(string label, string? directory, bool portable)
    {
        var cfg = DropInConfig.Current;
        var hasFiles = false;
        try { hasFiles = !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        if (hasFiles && portable) CreateShortcuts(directory!, cfg);
        if (cfg.Is(cfg.Installer.AfterInstall, "None")) return;
        if (!hasFiles)
        {
            Program.Notify(label + ": installation completed.");
            return;
        }
        var exe = SingleExecutable(directory!);
        if (exe == null || cfg.Is(cfg.Installer.AfterInstall, "OpenFolder")) OpenInstallDirectory(directory);
        else if (cfg.Is(cfg.Installer.AfterInstall, "RunSingleExe") ||
                 MessageBox(IntPtr.Zero, label + ": installation completed.\n\nRun " + Path.GetFileName(exe) + " now?", "DropIn", 0x24u) == 6)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe) }); }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { Program.Notify("Could not run " + Path.GetFileName(exe) + ": " + ex.Message, true); }
        }
    }

    /// <summary>
    /// Adds Start menu items for a portable package (PortableAppStartMenuItem): one shortcut for a lone .exe, or a Start menu
    /// folder holding a shortcut for every .exe when there are several.
    /// </summary>
    static void CreateShortcuts(string directory, DropInConfig cfg)
    {
        if (!cfg.Installer.PortableAppStartMenuItem || Type.GetTypeFromProgID(InstallerConstants.ShortcutShellProgId) is not { } shellType) return;
        try
        {
            var exes = Directory.EnumerateFiles(directory, "*.exe", SearchOption.AllDirectories).OrderBy(e => e, StringComparer.OrdinalIgnoreCase).ToList();
            if (exes.Count == 0) return;
            var programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            var folder = exes.Count == 1 ? programs : Path.Combine(programs, new DirectoryInfo(directory).Name);
            Directory.CreateDirectory(folder);
            dynamic shell = Activator.CreateInstance(shellType)!;
            foreach (var exe in exes)
            {
                dynamic link = shell.CreateShortcut(Path.Combine(folder, Path.GetFileNameWithoutExtension(exe) + ".lnk"));
                link.TargetPath = exe;
                link.WorkingDirectory = Path.GetDirectoryName(exe);
                link.Save();
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or IOException or UnauthorizedAccessException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            AppLog.Error("Could not create a shortcut: " + ex.Message);
        }
    }
    /// <summary>Returns the only executable found recursively, or null when the count is not exactly one.</summary>
    static string? SingleExecutable(string directory)
    {
        try
        {
            var exes = Directory.EnumerateFiles(directory, "*.exe", SearchOption.AllDirectories).Take(2).ToList();
            return exes.Count == 1 ? exes[0] : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Opens an existing installation directory in File Explorer.</summary>
    internal static void OpenInstallDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return;
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo(InstallerConstants.FileExplorerExecutable) { UseShellExecute = true };
            startInfo.ArgumentList.Add(directory);
            System.Diagnostics.Process.Start(startInfo);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Program.Notify("Installation completed, but its folder could not be opened: " + ex.Message, true);
        }
    }

}
