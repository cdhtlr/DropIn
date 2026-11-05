namespace DropIn.Common;

/// <summary>Removal of older installed versions before a new version is installed.</summary>
public static partial class InstallRunner
{
    /// <summary>Set by hosts that can show a prompt; receives the question and returns true to remove the older versions.</summary>
    public static Func<string, bool>? ConfirmRemoveOld { get; set; }

    /// <summary>An older installation found for the package being installed.</summary>
    /// <param name="Description">Text shown to the user.</param>
    /// <param name="ProductCode">MSI ProductCode to uninstall, or null.</param>
    /// <param name="Directory">Install folder to uninstall from and delete, or null.</param>
    sealed record OldVersion(string Description, string? ProductCode, string? Directory);

    /// <summary>Finds older versions of a package and, depending on RemoveOldVersions, asks to remove them.</summary>
    /// <param name="name">Package name.</param>
    /// <param name="version">Version about to be installed.</param>
    /// <param name="newDir">Folder the new version goes to.</param>
    /// <param name="msiFile">The MSI being installed, or null for other installer types.</param>
    /// <param name="cfg">Configuration (RemoveOldVersions).</param>
    static void RemoveOldVersions(string name, string version, string newDir, string? msiFile, DropInConfig cfg)
    {
        if (cfg.Installer.RemoveOldVersions == AskChoice.No) return;
        var old = FindOldVersions(name, version, newDir, msiFile, cfg);
        if (old.Count == 0) return;

        if (cfg.Installer.RemoveOldVersions == AskChoice.Ask)
        {
            var list = string.Join("\n", old.Select(o => "  - " + o.Description));
            var question = $"Older version(s) of {name} are installed:\n{list}\n\nRemove them before installing {name} {version}?\n\n" +
                           "Yes uninstalls them and deletes their folder. No keeps them.";
            if (ConfirmRemoveOld is not { } ask || !ask(question))
            {
                AppLog.Info($"{name}: older versions were kept.");
                return;
            }
        }
        foreach (var o in old) Remove(o, cfg);
    }

    /// <summary>Collects older versions from the install history and from Windows Installer.</summary>
    static List<OldVersion> FindOldVersions(string name, string version, string newDir, string? msiFile, DropInConfig cfg)
    {
        var list = new List<OldVersion>();
        var root = Path.GetFullPath(cfg.Installer.InstallDirectory).TrimEnd(Path.DirectorySeparatorChar);
        var target = Path.GetFullPath(newDir).TrimEnd(Path.DirectorySeparatorChar);
        foreach (var p in InstallHistory.Load(cfg.Installer.InstallDirectory).Where(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(p.Directory)))
        {
            var dir = Path.GetFullPath(p.Directory).TrimEnd(Path.DirectorySeparatorChar);
            // Only folders below the install root are ever deleted, and never the folder the new version goes to.
            if (!Directory.Exists(dir) || !IsBelow(root, dir) || IsBelow(dir, target) || IsBelow(target, dir) || dir.Equals(target, StringComparison.OrdinalIgnoreCase)) continue;
            if (IsNewer(p.Version, version)) continue;
            list.Add(new OldVersion($"{name} {p.Version} ({dir})", null, dir));
        }
        if (msiFile != null && OperatingSystem.IsWindows())
        {
            try
            {
                if (MsiTools.ReadPackage(msiFile) is { } pkg)
                    foreach (var (code, installed) in MsiTools.InstalledRelated(pkg.UpgradeCode))
                        if (!code.Equals(pkg.ProductCode, StringComparison.OrdinalIgnoreCase) && !IsNewer(installed, pkg.ProductVersion))
                            list.Add(new OldVersion($"{pkg.ProductName} {installed} (Windows Installer)", code, null));
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or System.ComponentModel.Win32Exception)
            {
                AppLog.Error("Could not look for older MSI versions: " + ex.Message);
            }
        }
        return list;
    }

    /// <summary>Whether <paramref name="path"/> is located below <paramref name="parent"/>.</summary>
    static bool IsBelow(string parent, string path)
        => path.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether <paramref name="candidate"/> is a higher version than <paramref name="reference"/> (unparsable versions are never newer).</summary>
    static bool IsNewer(string candidate, string reference)
        => Version.TryParse(candidate, out var a) && Version.TryParse(reference, out var b) && a > b;

    /// <summary>Uninstalls one older version and deletes its folder.</summary>
    static void Remove(OldVersion old, DropInConfig cfg)
    {
        if (old.ProductCode != null && OperatingSystem.IsWindows())
            AppLog.Info($"Uninstalled {old.Description}: exit code {UninstallMsi(old.ProductCode, cfg)}.");
        if (old.Directory == null) return;
        if (OperatingSystem.IsWindows())
            foreach (var entry in UninstallRegistry.FindIn(old.Directory))
            {
                // An uninstaller without a silent switch is only run when someone can interact with it.
                var exit = entry.IsMsi ? UninstallMsi(entry.Key, cfg)
                    : entry.Quiet != null ? RunCommandLine(entry.Quiet, cfg)
                    : entry.Uninstall != null && !NonInteractive ? RunCommandLine(entry.Uninstall, cfg) : -2;
                AppLog.Info($"Uninstalled {old.Description} ({entry.Key}): exit code {exit}.");
            }
        try { if (Directory.Exists(old.Directory)) Directory.Delete(old.Directory, true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Error($"Could not delete '{old.Directory}': {ex.Message}"); }
        RemoveIfEmpty(Path.GetDirectoryName(old.Directory) ?? old.Directory);
    }
}