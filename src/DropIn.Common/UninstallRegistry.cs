using System.Runtime.Versioning;
using Microsoft.Win32;

namespace DropIn.Common;

/// <summary>Finds the "Add/Remove Programs" registry entries that belong to an install folder.</summary>
[SupportedOSPlatform("windows")]
static class UninstallRegistry
{
    /// <summary>Registry path that lists installed programs.</summary>
    const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    /// <summary>One registered program.</summary>
    /// <param name="Key">Registry subkey name (the ProductCode for MSI products).</param>
    /// <param name="IsMsi">Whether Windows Installer owns the entry.</param>
    /// <param name="Quiet">QuietUninstallString, when present.</param>
    /// <param name="Uninstall">UninstallString, when present.</param>
    internal sealed record Entry(string Key, bool IsMsi, string? Quiet, string? Uninstall);

    /// <summary>Lists registered programs whose install location, or uninstaller, lives inside <paramref name="directory"/>.</summary>
    /// <param name="directory">Install folder to match.</param>
    internal static List<Entry> FindIn(string directory)
    {
        var found = new List<Entry>();
        var views = new[] { (RegistryHive.LocalMachine, RegistryView.Registry64), (RegistryHive.LocalMachine, RegistryView.Registry32), (RegistryHive.CurrentUser, RegistryView.Default) };
        foreach (var (hive, view) in views)
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var list = root.OpenSubKey(UninstallPath);
                if (list == null) continue;
                foreach (var name in list.GetSubKeyNames())
                {
                    using var key = list.OpenSubKey(name);
                    if (key == null) continue;
                    var quiet = key.GetValue("QuietUninstallString") as string;
                    var uninstall = key.GetValue("UninstallString") as string;
                    var inside = IsInside(directory, key.GetValue("InstallLocation") as string)
                                 || IsInside(directory, ExecutableOf(quiet ?? uninstall));
                    if (!inside || found.Any(e => e.Key.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
                    found.Add(new Entry(name, key.GetValue("WindowsInstaller") is 1, quiet, uninstall));
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        }
        return found;
    }

    /// <summary>Maps every registered program to the folder it reports (InstallLocation, else the folder of its uninstaller).</summary>
    internal static Dictionary<string, string> Locations()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var views = new[] { (RegistryHive.LocalMachine, RegistryView.Registry64), (RegistryHive.LocalMachine, RegistryView.Registry32), (RegistryHive.CurrentUser, RegistryView.Default) };
        foreach (var (hive, view) in views)
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var list = root.OpenSubKey(UninstallPath);
                if (list == null) continue;
                foreach (var name in list.GetSubKeyNames())
                {
                    using var key = list.OpenSubKey(name);
                    var location = (key?.GetValue("InstallLocation") as string)?.Trim().Trim('"');
                    if (string.IsNullOrWhiteSpace(location))
                    {
                        var exe = ExecutableOf(key?.GetValue("UninstallString") as string);
                        location = exe == null ? null : Path.GetDirectoryName(exe);
                    }
                    if (!string.IsNullOrWhiteSpace(location)) result[hive + "|" + name] = location;
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        }
        return result;
    }

    /// <summary>Whether <paramref name="path"/> is the directory or a location below it.</summary>
    /// <param name="directory">Directory to test against.</param>
    /// <param name="path">Candidate path.</param>
    static bool IsInside(string directory, string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
            var full = Path.GetFullPath(path.Trim().Trim('"')).TrimEnd(Path.DirectorySeparatorChar);
            return full.Equals(root, StringComparison.OrdinalIgnoreCase) || full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    /// <summary>Extracts the program path from an uninstall command line.</summary>
    /// <param name="commandLine">Command line, quoted or not.</param>
    static string? ExecutableOf(string? commandLine)
    {
        var text = commandLine?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        if (text[0] == '"') { var end = text.IndexOf('"', 1); return end > 1 ? text[1..end] : null; }
        var exe = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exe > 0 ? text[..(exe + 4)] : null;
    }
}