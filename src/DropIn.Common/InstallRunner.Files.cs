namespace DropIn.Common;

/// <summary>File and archive installation operations.</summary>
public static partial class InstallRunner
{
    /// <summary>Installs a plain installer file (.exe/.msi/.msix...). On failure launches it interactively.</summary>
/// <param name="file">Path to a standalone installer file.</param>
    /// <param name="cfg">Configuration controlling installation behavior.</param>
    /// <param name="name">Optional package folder name; defaults to the installer file name.</param>
    /// <param name="progress">Optional receiver of installation progress.</param>
    /// <param name="waitForInteractive">Whether to wait for the installer's own UI when the silent install fails.</param>
    /// <param name="onInteractive">Called right before the installer is opened with its own UI.</param>
    public static InstallResult InstallFile(string file, DropInConfig cfg, string? name = null, IProgress<InstallProgressInfo>? progress = null, bool waitForInteractive = false, Action? onInteractive = null)
    {
        var (productName, version) = InstallerMetadata.Read(file);
        var display = name ?? (string.IsNullOrWhiteSpace(productName) ? Path.GetFileNameWithoutExtension(file) : productName!);
        var folder = name == null ? RawFolderName(file, cfg, display, version) : SafeName(name);
        var dir = Path.Combine(cfg.Installer.InstallDirectory, folder);
        var type = Silent.Normalize(null, file);
        RemoveOldVersions(display, version ?? "", dir, type == "msi" ? file : null, cfg);
        var result = InstallFileCore(file, cfg, folder, waitForInteractive, onInteractive, progress: progress);
        if (result.Success)
            DropInConfig.RecordInstall(new InstalledPackage
            {
                Name = display, Version = version ?? "", Source = CommonDefaults.OtherSource, Directory = dir, InstalledAt = DateTime.Now
            });
        return result;
    }

    /// <summary>Attempts supported silent installation methods and applies the configured fallback policy.</summary>
    /// <param name="file">Installer file path.</param>
    /// <param name="cfg">Configuration controlling installation behavior.</param>
    /// <param name="name">Optional package folder name.</param>
    /// <param name="waitForInteractive">Whether to wait for an interactive installer and report its exit status.</param>
    /// <param name="onInteractive">Optional callback invoked before interactive fallback.</param>
    /// <param name="priorFailure">Earlier silent failure to include in the final diagnostic.</param>
    /// <param name="progress">Optional receiver of installation progress.</param>
    /// <param name="declaredType">Installer type declared by the package manifest, used when the file cannot be recognized.</param>
    static InstallResult InstallFileCore(string file, DropInConfig cfg, string? name, bool waitForInteractive, Action? onInteractive = null, string? priorFailure = null, IProgress<InstallProgressInfo>? progress = null, string? declaredType = null)
    {
        progress ??= InstallProgress.None;
        name = name == null ? RawFolderName(file, cfg, Path.GetFileNameWithoutExtension(file), null) : SafeName(name);
        var type = Silent.Normalize(null, file);
        var dir = Path.Combine(cfg.Installer.InstallDirectory, name);
        string reason;

        // Dispatch by detected installer type; unknown formats proceed directly to the configured fallback.
        switch (type)
        {
            case "zip":
                return InstallPortable(file, name, type, cfg, progress);
            case "msi":
            case "msix":
            {
                var r = Run(file, type, Silent.DefaultSwitch(type) ?? "", dir, cfg, progress);
                if (r.Success) return r;
                reason = r.Message;
                break;
            }
            case "exe":
            {
                var errors = new List<string>();
                // Recognize the setup framework first; the manifest type is the fallback. All other known switches follow, then the interactive fallback.
                var detected = InstallerSniffer.Detect(file) ?? (declaredType is "inno" or "nsis" or "burn" ? declaredType : null);
                var candidates = detected != null && Silent.DefaultSwitch(detected) is { } known
                    ? Silent.ExeCandidates.Prepend(known).Distinct()
                    : Silent.ExeCandidates;
                foreach (var sw in candidates)
                {
                    var r = Run(file, "exe", sw, dir, cfg, progress);
                    if (r.Success) return r;
                    errors.Add(r.Message);
                    if (r.Message.StartsWith("Could not start")) break;
                }
                reason = "The installer could not be completed with the available installation options:\n" + string.Join("\n", errors);
                break;
            }
            default:
                reason = $"Unsupported or unknown installer type '{Path.GetExtension(file)}'.";
                break;
        }

        if (priorFailure != null) reason = priorFailure + "\n" + reason;
        if (!cfg.Installer.SilentFallback)
            return new InstallResult(false, reason + "\nInteractive installation is disabled (SilentInstall.Fallback = false).");

        if (waitForInteractive) onInteractive?.Invoke();
        var interactive = LaunchInteractive(file, waitForExit: waitForInteractive);
        if (waitForInteractive) return interactive.Success
            ? interactive
            : new InstallResult(false, reason + "\n" + interactive.Message);
        return new InstallResult(false, reason + "\nLaunched the installer in interactive mode.");
    }

    /// <summary>Builds the install folder for a raw installer file using the configured pattern; name and version come from the file metadata when available, the source is "other".</summary>
    /// <param name="file">Installer file path.</param>
    /// <param name="cfg">Configuration containing the folder pattern.</param>
    /// <param name="name">Product name, or the file name without extension when the metadata has none.</param>
    /// <param name="version">Product version from the metadata, or null.</param>
    static string RawFolderName(string file, DropInConfig cfg, string name, string? version)
        => FolderName(new AppManifest { Name = name, Version = version ?? "", Source = CommonDefaults.OtherSource }, cfg, Path.GetFileNameWithoutExtension(file));

    /// <summary>Installs a portable file or extracts a ZIP payload into its package directory.</summary>
    /// <param name="file">Payload path.</param>
    /// <param name="name">Package folder name.</param>
    /// <param name="type">Normalized payload type.</param>
    /// <param name="cfg">Configuration supplying the install root.</param>
    /// <param name="progress">Optional receiver of installation progress.</param>
    public static InstallResult InstallPortable(string file, string name, string type, DropInConfig cfg, IProgress<InstallProgressInfo>? progress = null)
    {
        progress ??= InstallProgress.None;
        var dir = Path.Combine(cfg.Installer.InstallDirectory, name);
        Directory.CreateDirectory(dir);
        const string stage = "Copying files";
        progress.Report(new InstallProgressInfo(InstallProgress.PackageExtracted, stage));
        if (type == "zip" || file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            ExtractZip(file, dir, f => progress.Report(new InstallProgressInfo(InstallProgress.Scale(f, InstallProgress.PackageExtracted, InstallProgress.Complete), stage)));
        else
            CopyOver(file, Path.Combine(dir, Path.GetFileName(file)));
        progress.Report(new InstallProgressInfo(InstallProgress.Complete, "Completed"));
        return new InstallResult(true, "Installation completed.", dir, Portable: true);
    }

    /// <summary>Extracts a ZIP archive while rejecting entries that would escape the destination directory.</summary>
    /// <param name="zipPath">Archive to extract.</param>
    /// <param name="dir">Destination directory.</param>
    /// <param name="report">Receives the completed fraction (0 to 1).</param>
    static void ExtractZip(string zipPath, string dir, Action<double>? report = null)
        => ArchiveExtractor.Extract(zipPath, dir, report, WriteOver);

    /// <summary>Copies a file over an existing destination using the shared overwrite handling.</summary>
    /// <param name="source">File to copy.</param>
    /// <param name="target">Destination file path.</param>
    static void CopyOver(string source, string target) => WriteOver(target, () => File.Copy(source, target, true));

    /// <summary>Overwrites a file, clearing a read-only flag, and explains permission or in-use failures.</summary>
    /// <param name="target">Destination file path.</param>
    /// <param name="write">Operation that writes the replacement file.</param>
    static void WriteOver(string target, Action write)
    {
        try
        {
            if (File.Exists(target) && (File.GetAttributes(target) & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(target, FileAttributes.Normal);
            write();
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new UnauthorizedAccessException(
                $"Cannot write '{target}'. The file is in use, or the install directory does not grant your user write access " +
                "(re-run DropIn setup as administrator to fix the permissions).", ex);
        }
    }
}
