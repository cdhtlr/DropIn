using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Win32;

namespace DropIn.Installer;

[SupportedOSPlatform("windows")]
static partial class Setup
{
    /// <summary>Checks that the package association and its icon handler are registered and available.</summary>
    public static bool IsHandlerRegistered()
    {
        using var root = Registry.LocalMachine.OpenSubKey(InstallerConstants.ClassesRegistryKey);
        using var extension = root?.OpenSubKey(InstallerConstants.PackageExtension);
        if (!string.Equals(extension?.GetValue("", null)?.ToString(), InstallerConstants.PackageProgId, StringComparison.OrdinalIgnoreCase))
            return false;
        using var iconHandler = extension?.OpenSubKey(InstallerConstants.IconHandlerSubKey);
        if (!string.Equals(iconHandler?.GetValue("", null)?.ToString(), InstallerConstants.IconHandlerClsid, StringComparison.OrdinalIgnoreCase))
            return false;
        using var server = root?.OpenSubKey(InstallerConstants.ClsidRegistryKey + InstallerConstants.IconHandlerClsid + @"\" + InstallerConstants.InprocServerSubKey);
        var hostPath = server?.GetValue("", null)?.ToString();
        return !string.IsNullOrWhiteSpace(hostPath) && File.Exists(hostPath);
    }

    /// <summary>
    /// Lets every local user modify the install directory (inherited by new files). Without it, files created by the
    /// service or an administrator cannot be overwritten by a normal user, e.g. on C:\.
    /// </summary>
    public static void GrantInstallDirectoryAccess(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
            var info = new DirectoryInfo(directory);
            var security = info.GetAccessControl();
            // Inherit modify access so ordinary users can replace files created by the service or an administrator.
            security.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(users,
                System.Security.AccessControl.FileSystemRights.Modify,
                System.Security.AccessControl.InheritanceFlags.ContainerInherit | System.Security.AccessControl.InheritanceFlags.ObjectInherit,
                System.Security.AccessControl.PropagationFlags.None, System.Security.AccessControl.AccessControlType.Allow));
            info.SetAccessControl(security);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or InvalidOperationException or System.Security.SecurityException) { }
    }

    /// <summary>Registers the .app association, icon handler, and shell open command.</summary>
    public static void RegisterHandler()
    {
        var comHost = Path.Combine(AppContext.BaseDirectory, "DropIn.IconHandler.comhost.dll");
        if (!File.Exists(comHost)) throw new FileNotFoundException("The package icon handler COM host is missing.", comHost);
        using var root = Registry.LocalMachine.CreateSubKey(InstallerConstants.ClassesRegistryKey);
        using (var ext = root.CreateSubKey(InstallerConstants.PackageExtension))
        {
            ext.SetValue("", InstallerConstants.PackageProgId);
            using var handler = ext.CreateSubKey(InstallerConstants.IconHandlerSubKey);
            handler.SetValue("", InstallerConstants.IconHandlerClsid);
        }
        using (var id = root.CreateSubKey(InstallerConstants.PackageProgId))
        {
            id.SetValue("", "DropIn package");
            using var icon = id.CreateSubKey("DefaultIcon");
            var fileIcon = Path.Combine(AppContext.BaseDirectory, "file_icon.ico");
            icon.SetValue("", File.Exists(fileIcon) ? $"\"{fileIcon}\"" : $"\"{Exe}\",0");
            using var cmd = id.CreateSubKey(InstallerConstants.OpenCommandSubKey); cmd.SetValue("", $"\"{Exe}\" \"%1\"");
            using var handler = id.CreateSubKey(InstallerConstants.IconHandlerSubKey);
            handler.SetValue("", InstallerConstants.IconHandlerClsid);
        }
        using (var clsid = root.CreateSubKey(InstallerConstants.ClsidRegistryKey + InstallerConstants.IconHandlerClsid))
        {
            clsid.SetValue("", "DropIn package icon handler");
            using var server = clsid.CreateSubKey(InstallerConstants.InprocServerSubKey);
            server.SetValue("", comHost);
            server.SetValue("ThreadingModel", InstallerConstants.ComThreadingModel);
        }
        RefreshShell();
    }

    /// <summary>Removes DropIn package association and icon-handler registry entries.</summary>
    public static void UnregisterHandler()
    {
        using var root = Registry.LocalMachine.CreateSubKey(InstallerConstants.ClassesRegistryKey);
        root.DeleteSubKeyTree(InstallerConstants.PackageProgId, false);
        root.DeleteSubKeyTree(InstallerConstants.PackageExtension, false);
        root.DeleteSubKeyTree(InstallerConstants.ClsidRegistryKey + InstallerConstants.IconHandlerClsid, false);
        root.DeleteSubKeyTree(InstallerConstants.DropHandlerKey, false);
        root.DeleteSubKeyTree(InstallerConstants.ClsidRegistryKey + InstallerConstants.DropHandlerClsid, false);
        RefreshShell();
        ReleaseHandlerFiles();
    }

    /// <summary>
    /// Explorer and shell helper hosts keep the icon handler DLLs loaded after unregistering, which blocks deleting them.
    /// Helper hosts are ended (they restart on demand) and Explorer is restarted so the DLLs are released.
    /// </summary>
    static void ReleaseHandlerFiles()
    {
        var names = new[] { "DropIn.IconHandler.comhost.dll", "DropIn.IconHandler.dll" };
        var helpers = new[] { "dllhost", "prevhost", "SearchProtocolHost", "SearchFilterHost" };
        var restartExplorer = false;
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                try
                {
                    if (p.Id == Environment.ProcessId) continue;
                    var loaded = p.Modules.Cast<ProcessModule>()
                        .Any(m => names.Contains(m.ModuleName, StringComparer.OrdinalIgnoreCase));
                    if (!loaded) continue;
                    if (p.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase)) restartExplorer = true;
                    else if (helpers.Contains(p.ProcessName, StringComparer.OrdinalIgnoreCase)) { p.Kill(); p.WaitForExit((int)InstallerConstants.IconHostStopTimeout.TotalMilliseconds); }
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException) { }
            }
        }
        if (!restartExplorer) return;
        foreach (var p in Process.GetProcessesByName("explorer"))
        {
            using (p)
            {
                try { p.Kill(); p.WaitForExit((int)InstallerConstants.ExplorerStopTimeout.TotalMilliseconds); }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
            }
        }
        // Explorer restarts by itself; start it explicitly if it did not.
        Thread.Sleep(InstallerConstants.ExplorerRestartDelay);
        if (Process.GetProcessesByName("explorer").Length == 0)
            try { Process.Start(new ProcessStartInfo(InstallerConstants.FileExplorerExecutable) { UseShellExecute = true }); }
            catch (System.ComponentModel.Win32Exception) { }
    }

}
