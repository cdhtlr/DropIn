namespace DropIn.Common;

/// <summary>Options of the packager: package creation, search and window state ("Packager" section).</summary>
public sealed class PackagerSettings
{
    /// <summary>Preferred installer properties used by the packager to pick a manifest installer.</summary>
    public Dictionary<string, string> PreferredOptions { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        [InstallerOption.KindKey] = "portable",
        ["Architecture"] = "x64",
        ["InstallerType"] = "exe"
    };

    /// <summary>Installer type assumed when a package does not declare one and it cannot be detected from the file.</summary>
    public string UnknownInstallerType { get; set; } = CommonDefaults.InstallerType;
    /// <summary>Optional proxy URL used for network requests.</summary>
    public string ProxyUrl { get; set; } = "";
    /// <summary>Accepts invalid or untrusted TLS certificates for every DropIn download. Unsafe: only enable it on a network you trust.</summary>
    public bool AllowInvalidCertificates { get; set; }
    /// <summary>User-Agent header sent with network requests.</summary>
    public string UserAgent { get; set; } = CommonDefaults.UserAgent;
    /// <summary>Maximum number of concurrent downloads.</summary>
    public int MaxParallelDownloads { get; set; } = 4;
    /// <summary>Number of attempts made for a failed download.</summary>
    public int DownloadRetries { get; set; } = 4;

    /// <summary>Where the packager saves .app files; a relative path is resolved against the packager directory.</summary>
    public string PackageOutputDirectory { get; set; } = CommonDefaults.OutputDirectory;


    /// <summary>Output file name without extension; tokens {Id}, {Version}, {Source}.</summary>
    public string OutputNamePattern { get; set; } = CommonDefaults.OutputNamePattern;

    /// <summary>Whether package creation may replace an existing output archive.</summary>
    public bool OverwriteOutput { get; set; } = true;
    /// <summary>Whether downloaded installers are checked against source checksums when available.</summary>
    public bool VerifyChecksum { get; set; } = true;

    /// <summary>Directory of the icon and screenshot database; a relative path is resolved against the application directory.</summary>
    public string DatabaseDirectory { get; set; } = CommonDefaults.DatabaseDirectory;

    /// <summary>Keeps a copy of each downloaded installer in <see cref="DownloadsDirectory"/>.</summary>
    public bool KeepDownloadedInstallers { get; set; }

    /// <summary>Where kept installer downloads are stored; a relative path is resolved against the packager directory.</summary>
    public string DownloadsDirectory { get; set; } = CommonDefaults.DownloadsDirectory;

    /// <summary>Asks before creating a package whose installer leaves registry entries or caches (MSI, MSIX, Burn, setup .exe).</summary>
    public bool WarnInstallerTraces { get; set; } = true;

    /// <summary>Maximum number of package search results returned to the user.</summary>
    public int SearchResultLimit { get; set; } = 100;
    /// <summary>Optional directory containing a portable package-manager client.</summary>
    public string PortableClientDirectory { get; set; } = CommonDefaults.PortableClientDirectory;
    /// <summary>Whether to prefer the portable client over an installed package-manager client.</summary>
    public bool PreferPortableClient { get; set; }
    /// <summary>Whether the package database is updated automatically.</summary>
    public bool AutoUpdateDatabase { get; set; } = true;
    /// <summary>Minimum interval, in days, between automatic database updates.</summary>
    public int DatabaseUpdateIntervalDays { get; set; } = 30;
    /// <summary>Timestamp of the most recent successful database update.</summary>
    public DateTime? LastDatabaseUpdate { get; set; }

    /// <summary>Last used package source (winget or chocolatey).</summary>
    public string LastSource { get; set; } = CommonDefaults.LastSource;
    /// <summary>Last package name, id or install command that was fetched.</summary>
    public string LastInput { get; set; } = "";
    /// <summary>Preferred width of the packager window, in device-independent units.</summary>
    public int WindowWidth { get; set; }
    /// <summary>Preferred height of the packager window, in device-independent units.</summary>
    public int WindowHeight { get; set; }

    /// <summary>Log file of the packager, relative to the program folder (empty disables logging).</summary>
    public string LogFile { get; set; } = CommonDefaults.PackagerLogFile;
    /// <summary>Maximum severity written to the log: Error, Warning, Info or Debug.</summary>
    public string LogLevel { get; set; } = CommonDefaults.LogLevel;

    /// <summary>Replaces null collections or strings that a hand-edited file may contain.</summary>
    internal void Normalize()
    {
        PreferredOptions ??= new PackagerSettings().PreferredOptions;
        if (string.IsNullOrWhiteSpace(PackageOutputDirectory)) PackageOutputDirectory = CommonDefaults.OutputDirectory;
        if (string.IsNullOrWhiteSpace(DownloadsDirectory)) DownloadsDirectory = CommonDefaults.DownloadsDirectory;
        OutputNamePattern = string.IsNullOrWhiteSpace(OutputNamePattern) ? CommonDefaults.OutputNamePattern : OutputNamePattern;
        if (string.IsNullOrWhiteSpace(PortableClientDirectory)) PortableClientDirectory = CommonDefaults.PortableClientDirectory;
        if (string.IsNullOrWhiteSpace(DatabaseDirectory)) DatabaseDirectory = CommonDefaults.DatabaseDirectory;
        LastSource ??= CommonDefaults.LastSource;
        LastInput ??= "";
        LogFile ??= "";
        LogLevel ??= CommonDefaults.LogLevel;
        UnknownInstallerType ??= CommonDefaults.InstallerType;
        ProxyUrl ??= "";
        UserAgent = string.IsNullOrWhiteSpace(UserAgent) ? CommonDefaults.UserAgent : UserAgent;
        MaxParallelDownloads = Math.Clamp(MaxParallelDownloads, 1, 16);
        DownloadRetries = Math.Clamp(DownloadRetries, 1, 10);
    }
}
