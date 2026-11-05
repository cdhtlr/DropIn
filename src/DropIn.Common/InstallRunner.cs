namespace DropIn.Common;

/// <summary>Installs packages or raw installer files.</summary>
public static partial class InstallRunner
{
    /// <summary>Set by the Windows service (session 0): never show UI, request elevation or launch interactive installers.</summary>
    public static bool NonInteractive { get; set; }

    /// <summary>Set by interactive hosts to ask whether an existing package folder may be overwritten (OverwriteExisting = Ask).</summary>
    public static Func<string, bool>? ConfirmOverwrite { get; set; }

    /// <summary>Creates a safe installation folder name from the manifest and configured pattern.</summary>
    /// <param name="manifest">Package metadata supplying replacement tokens.</param>
    /// <param name="cfg">Configuration containing the folder-name pattern.</param>
    /// <param name="fallbackName">Name to use when the manifest has no display name.</param>
    public static string FolderName(AppManifest manifest, DropInConfig cfg, string? fallbackName = null)
    {
        var baseName = string.IsNullOrWhiteSpace(manifest.Name) ? fallbackName ?? CommonDefaults.InstallFolderName : manifest.Name;
        var pattern = string.IsNullOrWhiteSpace(cfg.Installer.InstallSubfolderPattern) ? CommonDefaults.InstallSubfolderPattern : cfg.Installer.InstallSubfolderPattern;
        var name = pattern.Replace("{Name}", Token(baseName)).Replace("{Version}", Token(manifest.Version)).Replace("{Source}", Token(SourceName(manifest.Source)));
        return SafeName(name);
    }

    /// <summary>Combines the configured install root with the package-specific folder name.</summary>
    /// <param name="manifest">Package metadata supplying replacement tokens.</param>
    /// <param name="cfg">Configuration containing the install root and naming pattern.</param>
    /// <param name="fallbackName">Name to use when the manifest has no display name.</param>
    public static string GetPackageInstallDirectory(AppManifest manifest, DropInConfig cfg, string? fallbackName = null)
        => Path.Combine(cfg.Installer.InstallDirectory, FolderName(manifest, cfg, fallbackName));

    /// <summary>Checks whether a directory contains at least one entry, treating access failures as empty.</summary>
    /// <param name="dir">Directory to inspect.</param>
    static bool HasFiles(string dir)
    {
        try { return Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>Gets nonblank extra silent switches configured for the package name.</summary>
    /// <param name="m">Package manifest used to select the switch entry.</param>
    /// <param name="cfg">Configuration containing per-package switches.</param>
    static string? SilentExtra(AppManifest m, DropInConfig cfg)
        => cfg.Installer.ExtraSilentSwitches.TryGetValue(m.Name, out var extra) && !string.IsNullOrWhiteSpace(extra) ? extra : null;

    /// <summary>Extracts and installs a DropIn package, recording successful installations.</summary>
    /// <param name="appPath">Path to the package archive.</param>
    /// <param name="cfg">Configuration controlling installation behavior and destinations.</param>
    /// <param name="onInteractive">Called right before the installer is opened with its own UI (silent install failed).</param>
    /// <param name="progress">Optional receiver of installation progress.</param>
    public static InstallResult InstallPackage(string appPath, DropInConfig cfg, Action? onInteractive = null, IProgress<InstallProgressInfo>? progress = null)
    {
        progress ??= InstallProgress.None;
        // The service runs as SYSTEM but starts installers as the user: SYSTEM's temp folder is unreadable to the user, so extract below the install directory.
        var tmpRoot = NonInteractive ? Path.Combine(cfg.Installer.InstallDirectory, InstallHistory.StateDirectoryName, "tmp") : Path.GetTempPath();
        var tmp = Path.Combine(tmpRoot, "dropin_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            progress.Report(new InstallProgressInfo(0, "Extracting package"));
            var m = AppManifest.Extract(appPath, tmp, f =>
                progress.Report(new InstallProgressInfo(InstallProgress.Scale(f, 0, InstallProgress.PackageExtracted), "Extracting package")));
            var file = Path.Combine(tmp, m.InstallerFile);
            var name = FolderName(m, cfg, Path.GetFileNameWithoutExtension(appPath));
            var type = Silent.Normalize(m.InstallerType, m.InstallerFile);
            var existing = Path.Combine(cfg.Installer.InstallDirectory, name);

            if (HasFiles(existing) && (cfg.Installer.OverwriteExisting == AskChoice.No ||
                cfg.Installer.OverwriteExisting == AskChoice.Ask && ConfirmOverwrite is { } ask && !ask(existing)))
            {
                AppLog.Info($"{m.Name}: skipped, '{existing}' already exists.");
                return new InstallResult(true, "Already installed; skipped (OverwriteExisting).", existing);
            }

            RemoveOldVersions(m.Name, m.Version, existing, type == "msi" && !m.Portable ? file : null, cfg);
            var result = InstallPackageCore(m, file, name, type, appPath, cfg, onInteractive, progress);
            if (result.Success) progress.Report(new InstallProgressInfo(InstallProgress.Complete, "Completed"));
            AppLog.Write(result.Success ? "Info" : "Error", $"{m.Name} {m.Version}: {result.Message}");
            if (result.Success)
                DropInConfig.RecordInstall(new InstalledPackage
                {
                    Name = m.Name, Version = m.Version, Source = m.Source,
                    Directory = result.InstallDirectory ?? "", InstalledAt = DateTime.Now
                });
            return result;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            AppLog.Error("Could not install package: " + ex.Message);
            return new InstallResult(false, "Could not install package: " + ex.Message);
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>Installs the extracted payload using portable, silent, or interactive handling.</summary>
    /// <param name="m">Manifest describing the package payload and installation options.</param>
    /// <param name="file">Path to the extracted installer or payload.</param>
    /// <param name="name">Safe package folder name.</param>
    /// <param name="type">Normalized installer type.</param>
    /// <param name="appPath">Package path associated with the installation request.</param>
    /// <param name="cfg">Configuration controlling installation behavior.</param>
    /// <param name="onInteractive">Callback invoked before interactive fallback.</param>
    /// <param name="progress">Receiver of installation progress.</param>
    static InstallResult InstallPackageCore(AppManifest m, string file, string name, string type, string appPath, DropInConfig cfg, Action? onInteractive, IProgress<InstallProgressInfo> progress)
    {
        if (m.Portable || type == "portable" || type == "zip")
            return InstallPortable(file, name, type, cfg, progress);

        var dir = Path.Combine(cfg.Installer.InstallDirectory, name);
        var createdDir = !Directory.Exists(dir);
        Directory.CreateDirectory(dir);
        try
        {
            var args = string.IsNullOrWhiteSpace(m.SilentArgs) ? null : (m.SilentArgs + " " + m.CustomArgs + " " + SilentExtra(m, cfg)).Trim();
            if (args != null)
            {
                var r = Run(file, type, args, dir, cfg, progress);
                if (r.Success) return r with { InstallDirectory = dir };
                AppLog.Write("Warning", $"{m.Name}: silent install failed: {r.Message}");
                var h = InstallFileCore(file, cfg, name, waitForInteractive: true, onInteractive, r.Message, progress, m.InstallerType);
                return h.Success ? h with { InstallDirectory = dir } : new InstallResult(false, h.Message);
            }
            var result = InstallFileCore(file, cfg, name, waitForInteractive: true, onInteractive, progress: progress, declaredType: m.InstallerType);
            return result.Success ? result with { InstallDirectory = dir } : result;
        }
        finally
        {
            // The installer may have used another location; do not leave an empty folder behind.
            if (createdDir) RemoveIfEmpty(dir);
        }
    }

    /// <summary>Deletes a directory only when it exists and contains no entries.</summary>
    /// <param name="dir">Directory to remove if empty.</param>
    static void RemoveIfEmpty(string dir)
    {
        try
        {
            var root = Path.GetFullPath(DropInConfig.Current.Installer.InstallDirectory).TrimEnd(Path.DirectorySeparatorChar);
            // Also remove emptied source folders (for example "winget") but never the install root.
            for (var d = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar);
                 d.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && Directory.Exists(d) && !Directory.EnumerateFileSystemEntries(d).Any();
                 d = Path.GetDirectoryName(d)!)
                Directory.Delete(d);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
