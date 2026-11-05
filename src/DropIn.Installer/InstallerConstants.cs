namespace DropIn.Installer;

/// <summary>Centralized names, switches, and timing values used by the installer.</summary>
internal static class InstallerConstants
{
    /// <summary>The package file extension.</summary>
    internal const string PackageExtension = ".app";
    /// <summary>The Windows service name used by the package watcher.</summary>
    internal const string WatcherServiceName = "DropInWatcher";
    /// <summary>The registry classes root used for machine-wide file associations.</summary>
    internal const string ClassesRegistryKey = @"Software\Classes";
    /// <summary>The package file type identifier.</summary>
    internal const string PackageProgId = "DropIn.Package";
    /// <summary>The package icon handler COM class identifier.</summary>
    internal const string IconHandlerClsid = "{6A282523-BF7F-4F91-A7E5-8C945C57A801}";
    /// <summary>The COM class identifier of the removed drop handler; still cleaned up from earlier registrations.</summary>
    internal const string DropHandlerClsid = "{6A282523-BF7F-4F91-A7E5-8C945C57A802}";
    /// <summary>The registry key of the removed drop handler; still cleaned up from earlier registrations.</summary>
    internal const string DropHandlerKey = @"Directory\shellex\DragDropHandlers\DropIn";
    /// <summary>The registry subkey that associates the icon handler.</summary>
    internal const string IconHandlerSubKey = @"ShellEx\IconHandler";
    /// <summary>The registry key prefix for COM class registrations.</summary>
    internal const string ClsidRegistryKey = @"CLSID\";
    /// <summary>The registry key containing the COM in-process server path.</summary>
    internal const string InprocServerSubKey = "InprocServer32";
    /// <summary>The registry command key used to open a package.</summary>
    internal const string OpenCommandSubKey = @"shell\open\command";
    /// <summary>The COM threading model used by the icon handler.</summary>
    internal const string ComThreadingModel = "Both";
    /// <summary>The display name assigned to the watcher service.</summary>
    internal const string WatcherDisplayName = "DropIn Watcher";
    /// <summary>The description assigned to the watcher service.</summary>
    internal const string WatcherDescription = "Installs .app packages dropped into the DropIn install directory.";
    /// <summary>The shell shortcut automation COM program identifier.</summary>
    internal const string ShortcutShellProgId = "WScript.Shell";
    /// <summary>The service-control executable name.</summary>
    internal const string ServiceControlExecutable = "sc.exe";
    /// <summary>The file-manager executable name.</summary>
    internal const string FileExplorerExecutable = "explorer.exe";
    /// <summary>The service state directory name below the install directory.</summary>
    internal const string StateDirectoryName = DropIn.Common.InstallHistory.StateDirectoryName;
    /// <summary>The directory for watcher status files.</summary>
    internal const string StatusDirectoryName = "status";
    /// <summary>The status file extension.</summary>
    internal const string StatusFileExtension = ".txt";
    /// <summary>The suffix used to signal that the progress window should hide.</summary>
    internal const string StatusHideSuffix = ".hide";
    /// <summary>The suffix of the file that relays installation progress from the service to the progress window.</summary>
    internal const string StatusProgressSuffix = ".progress";
    /// <summary>Suffix of the file the progress window writes once it has read the package icon and screenshots.</summary>
    internal const string StatusReadySuffix = ".ready";
    /// <summary>How long the service waits for the progress window to read the package before deleting it.</summary>
    internal static readonly TimeSpan MediaReadyTimeout = TimeSpan.FromSeconds(20);
    /// <summary>The suffix for atomically published temporary status files.</summary>
    internal const string StatusTemporarySuffix = ".tmp";
    /// <summary>The watcher log file name.</summary>
    internal const string ServiceLogFileName = "service.log";
    /// <summary>The progress mode command-line switch.</summary>
    internal const string ProgressSwitch = "--progress";
    /// <summary>The watcher service command-line switch.</summary>
    internal const string ServiceSwitch = "--service";
    /// <summary>The register handler and service command-line switch.</summary>
    internal const string RegisterSwitch = "--register";
    /// <summary>The unregister handler and service command-line switch.</summary>
    internal const string UnregisterSwitch = "--unregister";
    /// <summary>The setup command-line switch.</summary>
    internal const string SetupSwitch = "--setup";
    /// <summary>The uninstall command-line switch.</summary>
    internal const string UninstallSwitch = "--uninstall";
    /// <summary>The install service command-line switch.</summary>
    internal const string InstallServiceSwitch = "--install-service";
    /// <summary>The uninstall service command-line switch.</summary>
    internal const string UninstallServiceSwitch = "--uninstall-service";
    /// <summary>The install package command-line switch.</summary>
    internal const string InstallSwitch = "--install";
    /// <summary>The default label for progress requests without an explicit package title.</summary>
    internal const string DefaultPackageLabel = "package";
    /// <summary>The prefix indicating a long command-line option.</summary>
    internal const string LongOptionPrefix = "--";
    /// <summary>The marquee progress animation speed.</summary>
    internal const int ProgressMarqueeAnimationSpeed = 30;
    /// <summary>The interval between progress status polls.</summary>
    internal static readonly TimeSpan StatusPollInterval = TimeSpan.FromMilliseconds(500);
    /// <summary>The maximum time to wait for a watcher status result.</summary>
    internal static readonly TimeSpan StatusTimeout = TimeSpan.FromHours(6);
    /// <summary>The interval between retries while deleting a processed package.</summary>
    internal static readonly TimeSpan PackageDeleteRetryInterval = TimeSpan.FromMilliseconds(500);
    /// <summary>Installer and archive file extensions the watcher installs besides .app packages.</summary>
    internal static readonly string[] RawInstallerExtensions = { ".exe", ".msi", ".msix", ".msixbundle", ".appx", ".appxbundle", ".zip" };
    /// <summary>Command-line switch of the helper process that asks the signed-in user a yes/no question for the service.</summary>
    internal const string ConfirmSwitch = "--confirm";
    /// <summary>Suffix of the file that holds the question text.</summary>
    internal const string QuestionSuffix = ".question";
    /// <summary>Suffix of the file that receives the answer.</summary>
    internal const string AnswerSuffix = ".answer";
    /// <summary>How long the service waits for an answer before treating it as Yes.</summary>
    internal static readonly TimeSpan ConfirmTimeout = TimeSpan.FromMinutes(5);
    /// <summary>File name prefix of DropIn's own programs, which the watcher must never treat as installers.</summary>
    internal const string OwnProgramPrefix = "DropIn";
    /// <summary>The number of package deletion attempts made by the watcher.</summary>
    internal const int PackageDeleteAttempts = 10;
    /// <summary>The maximum number of half-second package-readiness attempts.</summary>
    internal const int PackageReadinessAttempts = 600;
    /// <summary>The time allowed for the watcher worker to stop.</summary>
    internal static readonly TimeSpan WorkerStopTimeout = TimeSpan.FromSeconds(20);
    /// <summary>The time allowed for helper hosts to release the icon handler.</summary>
    internal static readonly TimeSpan IconHostStopTimeout = TimeSpan.FromSeconds(3);
    /// <summary>The time allowed for Explorer to stop before restart.</summary>
    internal static readonly TimeSpan ExplorerStopTimeout = TimeSpan.FromSeconds(5);
    /// <summary>The delay before restarting Explorer if needed.</summary>
    internal static readonly TimeSpan ExplorerRestartDelay = TimeSpan.FromMilliseconds(1500);
}
