namespace DropIn.Common;

/// <summary>Options of the installer, the watcher service and the install history ("Installer" section).</summary>
public sealed class InstallerSettings
{
    /// <summary>Root directory where package contents are installed; a relative path is resolved against the application directory.</summary>
    public string InstallDirectory { get; set; } = CommonDefaults.InstallDirectory;

    /// <summary>The InstallDirectory text as written in the file, kept so saving does not turn it into an absolute path.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    internal string? RawInstallDirectory { get; set; }

    /// <summary>After a package install: OpenFolder, AskRunSingleExe (ask when the folder has one .exe, else open it), RunSingleExe, or None.</summary>
    public string AfterInstall { get; set; } = CommonDefaults.AfterInstall;

    /// <summary>Whether interactive installations display a progress window.</summary>
    public bool ShowProgressWindow { get; set; } = true;
    /// <summary>Whether the progress window closes by itself after a successful installation (failures always stay open).</summary>
    public bool AutoCloseProgressWindow { get; set; } = true;
    /// <summary>Whether package screenshots are displayed in interactive installation UI.</summary>
    public bool ShowScreenshots { get; set; } = true;

    /// <summary>Screenshot slideshow in the installation window: Loop (keeps rotating until the window is closed), UntilFinished (stops when the installation ends) or Off (manual navigation only).</summary>
    public string ScreenshotSlideshow { get; set; } = CommonDefaults.ScreenshotSlideshow;

    /// <summary>Seconds each screenshot stays visible while the slideshow rotates.</summary>
    public int ScreenshotSlideshowSeconds { get; set; } = CommonDefaults.ScreenshotSlideshowSeconds;

    /// <summary>Adds a Start menu item for extracted (portable/zip) packages: one shortcut for a lone .exe, otherwise a Start menu folder with a shortcut per .exe. Setup-type installers are left to their own setup.</summary>
    public bool PortableAppStartMenuItem { get; set; } = true;

    /// <summary>When the silent install fails: true opens the installer's own UI, false reports the failure.</summary>
    [System.Text.Json.Serialization.JsonConverter(typeof(LenientBoolConverter))]
    public bool SilentFallback { get; set; } = true;

    /// <summary>Kills a silent installer after this many minutes; 0 waits indefinitely.</summary>
    public int InstallerTimeoutMinutes { get; set; }

    /// <summary>Install folder (relative to InstallDirectory, "\" creates subfolders); tokens {Name}, {Version}, {Source} (winget, chocolatey or other). Applies to .app packages and raw installer files.</summary>
    public string InstallSubfolderPattern { get; set; } = CommonDefaults.InstallSubfolderPattern;

    /// <summary>For MSI installs: reinstall an already registered product with the new package and clear its installer-source registration afterwards, so Windows Installer never looks for an old or deleted installer.</summary>
    public bool MsiCleanUpgrade { get; set; } = true;

    /// <summary>Older versions of the same package: true (default), false, or Ask (the service asks in your session; no answer within 5 minutes counts as Yes). Removal uninstalls the registered product (MSI or uninstall entry) and deletes its folder.</summary>
    public AskChoice RemoveOldVersions { get; set; } = AskChoice.Yes;

    /// <summary>When the package folder already has files: true overwrites (default), false skips, Ask prompts (and overwrites when no prompt is possible, e.g. in the service).</summary>
    public AskChoice OverwriteExisting { get; set; } = AskChoice.Yes;

    /// <summary>Extra silent switches appended per package, keyed by the package id.</summary>
    public Dictionary<string, string> ExtraSilentSwitches { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Log file of the installer and service, relative to the program folder (empty disables logging).</summary>
    public string LogFile { get; set; } = CommonDefaults.InstallerLogFile;
    /// <summary>Maximum severity written to the log: Error, Warning, Info or Debug.</summary>
    public string LogLevel { get; set; } = CommonDefaults.LogLevel;

    /// <summary>Replaces null collections or strings that a hand-edited file may contain.</summary>
    internal void Normalize()
    {
        LogFile ??= "";
        LogLevel ??= CommonDefaults.LogLevel;
        ExtraSilentSwitches = new Dictionary<string, string>(ExtraSilentSwitches ?? new(), StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(InstallDirectory)) InstallDirectory = CommonDefaults.InstallDirectory;
        AfterInstall ??= CommonDefaults.AfterInstall;
        InstallSubfolderPattern = string.IsNullOrWhiteSpace(InstallSubfolderPattern) ? CommonDefaults.InstallSubfolderPattern : InstallSubfolderPattern;
        ScreenshotSlideshow ??= CommonDefaults.ScreenshotSlideshow;
        ScreenshotSlideshowSeconds = Math.Clamp(ScreenshotSlideshowSeconds, 1, 60);
    }
}
