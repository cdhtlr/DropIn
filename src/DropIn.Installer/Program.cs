using System.Runtime.InteropServices;
using System.Diagnostics;
using System.ServiceProcess;
using DropIn.Common;

namespace DropIn.Installer;

static class Program
{
    /// <summary>Displays a native Windows message box.</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    /// <summary>Displays a message using Windows UI when available and the console otherwise.</summary>
    internal static void Notify(string text, bool error = false)
    {
        if (OperatingSystem.IsWindows()) MessageBox(IntPtr.Zero, text, "DropIn", error ? 0x10u : 0x40u);
        else Console.WriteLine(text);
    }

    [DllImport("kernel32.dll")]
    static extern bool AttachConsole(int processId);

    const string HelpText =
        "Usage: DropIn.Installer [file ...]\n" +
        "       DropIn.Installer --install [file ...]\n" +
        "       DropIn.Installer --register | --unregister\n" +
        "       DropIn.Installer -h | --help\n\n" +
        "Supported files: .app, .exe, .msi, .msix, .msixbundle, .appx, .appxbundle, .zip\n\n" +
        "  <file> ...      Install the given files\n" +
        "  --install       Install the given files, or browse for files when none are given\n" +
        "  --register      Register the .app handler and the DropInWatcher service (administrator)\n" +
        "  --unregister    Remove the handler and the service (administrator)\n" +
        "  -h, --help      Show this help\n\n" +
        "Without arguments the Installer window opens.";

    /// <summary>Prints the usage to the parent console when there is one, otherwise shows it in a message box.</summary>
    static void ShowHelp()
    {
        if (OperatingSystem.IsWindows() && AttachConsole(-1))
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine(HelpText);
        }
        else Notify(HelpText);
    }

    /// <summary>Loads configuration and dispatches GUI, package, progress, and setup modes.</summary>
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] is "-h" or "--help" or "-?" or "/?")
        {
            ShowHelp();
            return 0;
        }
        if (args.Length > 0 && args[0].ToLowerInvariant() is InstallerConstants.UnregisterSwitch
            or InstallerConstants.UninstallSwitch or InstallerConstants.UninstallServiceSwitch)
            return Command(args, new DropInConfig());
        if (args.Length >= 2 && args[0].Equals(InstallerConstants.ConfirmSwitch, StringComparison.OrdinalIgnoreCase))
            return Confirm(args[1]);
        DropInConfig cfg;
        try
        {
            if (args.Length == 1 && args[0].Equals(InstallerConstants.ServiceSwitch, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The watcher service requires an explicit data directory.");
            var explicitDirectory = args.Length >= 2 && (args[0].Equals(InstallerConstants.ServiceSwitch, StringComparison.OrdinalIgnoreCase)
                || args[0].Equals(InstallerConstants.RegisterSwitch, StringComparison.OrdinalIgnoreCase)
                || args[0].Equals(InstallerConstants.SetupSwitch, StringComparison.OrdinalIgnoreCase));
            cfg = explicitDirectory ? DropInConfig.LoadExisting(args[1]) : DropInConfig.LoadSelected();
        }
        catch (DataDirectoryUnavailableException ex) when (args.Length == 0 ||
            !args[0].StartsWith(InstallerConstants.LongOptionPrefix, StringComparison.Ordinal) ||
            args[0].Equals(InstallerConstants.InstallSwitch, StringComparison.OrdinalIgnoreCase) ||
            (args.Length == 1 && args[0].Equals(InstallerConstants.RegisterSwitch, StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var recovered = DataDirectoryRecovery.Prompt(ex);
                if (recovered == null) return 1;
                cfg = recovered;
            }
            catch (Exception recoveryError) when (recoveryError is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or NotSupportedException)
            {
                Notify("Could not recover the DropIn data directory: " + recoveryError.Message, true);
                return 1;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            if (args.Length > 0 && args[0].Equals(InstallerConstants.ServiceSwitch, StringComparison.OrdinalIgnoreCase))
            {
                ReportServiceError("Could not load the watcher configuration: " + ex.Message);
                return 1;
            }
            Notify("Could not load DropIn configuration: " + ex.Message, true);
            return 1;
        }
        if (args.Length >= 2 && args[0].Equals(InstallerConstants.ProgressSwitch, StringComparison.OrdinalIgnoreCase))
        {
            var message = ProgressForm.WaitForStatus(args[1], args.Length > 2 ? args[2] : InstallerConstants.DefaultPackageLabel, args.Length > 3 ? args[3] : null);
            if (message is { Success: true }) ShowInstalled(args.Length > 2 ? args[2] : InstallerConstants.DefaultPackageLabel, message.Value.Directory, message.Value.Portable);
            else if (message is { Success: false }) Notify(message.Value.Text, true);
            return 0;
        }
        if (args.Length > 0 && args[0].StartsWith(InstallerConstants.LongOptionPrefix, StringComparison.Ordinal))
        {
            if (args[0].Equals(InstallerConstants.RegisterSwitch, StringComparison.OrdinalIgnoreCase) && args.Length == 1
                && !SelectDataDirectory(ref cfg, null)) return 1;
            return Command(args, cfg);
        }
        EnableOverwritePrompt();
        if (args.Length == 0) return RunLauncher(cfg, Array.Empty<string>(), false);
        if (!EnsureInstallAccess(cfg)) return 1;

        var exit = 0;
        foreach (var file in args)
        {
            if (!File.Exists(file)) { Notify("File not found: " + file); exit = 1; continue; }
            var isPackage = file.EndsWith(AppManifest.Extension, StringComparison.OrdinalIgnoreCase);
            try
            {
                cfg.EnsureDataAvailable();
                Directory.CreateDirectory(cfg.Installer.InstallDirectory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                Notify("Could not access the installation directory " + cfg.Installer.InstallDirectory + ": " + ex.Message, true);
                return 1;
            }
            var label = Path.GetFileName(file);
            PackageMedia? media = null;
            if (isPackage)
                try { media = AppManifest.ReadMedia(file); }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException) { }
            var r = ProgressForm.Run("Installing " + label + "...", media, (hide, progress) => isPackage
                ? InstallRunner.InstallPackage(file, cfg, hide, progress)
                : InstallRunner.InstallFile(file, cfg, null, progress));
            if (r.Success)
            {
                if (isPackage) ShowInstalled(label, r.InstallDirectory, r.Portable);
                else Notify(label + ": " + r.Message);
            }

            else
            {
                Notify(label + ": " + r.Message, true);
                exit = 1;
            }
        }
        return exit;
    }

    internal static void ReportServiceError(string message)
    {
        try { EventLog.WriteEntry("Application", "DropIn watcher: " + message, EventLogEntryType.Error); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or System.Security.SecurityException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(message + " (Could not write Windows event log: " + ex.Message + ")");
        }
    }

    internal static bool SelectDataDirectory(ref DropInConfig cfg, IWin32Window? owner)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose a DropIn data folder. Existing packages and data are copied there; installed applications remain at their original locations.",
            UseDescriptionForTitle = true,
            SelectedPath = cfg.BaseDirectory,
            ShowNewFolderButton = true
        };
        if (dialog.ShowDialog(owner) != DialogResult.OK) return false;
        try
        {
            cfg = cfg.SelectDataDirectory(dialog.SelectedPath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or NotSupportedException)
        {
            Notify("Could not select the DropIn data directory: " + ex.Message, true);
            return false;
        }
    }

    /// <summary>Whether the install folder is writable; when it is not, runs the elevated setup (which grants access) and checks again.</summary>
    internal static bool NeedsInstallAccess(DropInConfig cfg)
        => OperatingSystem.IsWindows() && !Setup.IsAdmin() && !WriteAccess.IsWritable(cfg.Installer.InstallDirectory);

    /// <summary>Makes sure the install folder is writable before installing, asking for administrator rights when needed and reporting why it failed.</summary>
    static bool EnsureInstallAccess(DropInConfig cfg)
    {
        if (!NeedsInstallAccess(cfg)) return true;
        var code = Setup.Elevate(InstallerConstants.SetupSwitch + " \"" + cfg.BaseDirectory + "\"");
        if (code == Setup.UacDeclined)
            Notify("Cannot write to " + cfg.Installer.InstallDirectory + ": you declined the administrator (UAC) prompt.", true);
        else if (Setup.LastElevationError != null)
            Notify("Cannot write to " + cfg.Installer.InstallDirectory + ": administrator rights could not be obtained: " + Setup.LastElevationError, true);
        else if (!WriteAccess.IsWritable(cfg.Installer.InstallDirectory))
            Notify("Cannot write to " + cfg.Installer.InstallDirectory + " even after setup. Choose another InstallDirectory in dropin.config.json.", true);
        else return true;
        return false;
    }

    /// <summary>Asks before overwriting an existing package folder (OverwriteExisting = Ask) or removing older versions (RemoveOldVersions = Ask).</summary>
    internal static void EnableOverwritePrompt()
    {
        InstallRunner.ConfirmOverwrite = dir =>
            MessageBox(IntPtr.Zero, "'" + dir + "' already contains files.\n\nOverwrite it?", "DropIn", 0x24u) == 6;
        InstallRunner.ConfirmRemoveOld = question => MessageBox(IntPtr.Zero, question, "DropIn", 0x24u) == 6;
    }

    /// <summary>Helper started by the service in the user session: shows the saved question and writes Yes or No to the answer file.</summary>
    static int Confirm(string basePath)
    {
        var question = "";
        try { question = File.ReadAllText(basePath + InstallerConstants.QuestionSuffix); }
        catch (IOException) { return 1; }
        var yes = MessageBox(IntPtr.Zero, question, "DropIn", 0x24u) == 6;
        var temp = basePath + InstallerConstants.StatusTemporarySuffix;
        File.WriteAllText(temp, yes ? "yes" : "no");
        File.Move(temp, basePath + InstallerConstants.AnswerSuffix, true);
        return 0;
    }

    /// <summary>Follows the configured post-install action for an installed package.</summary>
    internal static void ShowInstalled(string label, string? directory, bool portable) => PostInstallActions.ShowInstalled(label, directory, portable);

    /// <summary>Opens an existing installation directory in File Explorer.</summary>
    internal static void OpenInstallDirectory(string? directory) => PostInstallActions.OpenInstallDirectory(directory);

    /// <summary>Starts the launcher with any initial files and optional startup browsing.</summary>
    static int RunLauncher(DropInConfig cfg, IEnumerable<string> files, bool browse)
    {
        EnableOverwritePrompt();
        Application.EnableVisualStyles();
        Application.Run(new LauncherForm(cfg, files, browse));
        return 0;
    }

    /// <summary>--register, --unregister, --install [files] and the internal --service.</summary>
    internal static int Command(string[] args, DropInConfig cfg)
    {
        var cmd = args[0].ToLowerInvariant();
        if (!OperatingSystem.IsWindows()) { Console.Error.WriteLine("Windows only."); return 1; }
        if (cmd == InstallerConstants.ServiceSwitch)
        {
            ServiceBase.Run(new WatcherService(cfg));
            return 0;
        }
        if (cmd == InstallerConstants.InstallSwitch) return RunLauncher(cfg, args.Skip(1), args.Length == 1);
        if (cmd is not (InstallerConstants.RegisterSwitch or InstallerConstants.UnregisterSwitch or InstallerConstants.SetupSwitch or InstallerConstants.UninstallSwitch or InstallerConstants.InstallServiceSwitch or InstallerConstants.UninstallServiceSwitch))
        {
            Notify("Unknown option " + args[0] + "\n\n" + HelpText, true);
            return 1;
        }
        if (!Setup.IsAdmin())
        {
            var elevationArgs = cmd is InstallerConstants.RegisterSwitch or InstallerConstants.SetupSwitch
                ? cmd + " \"" + cfg.BaseDirectory + "\""
                : string.Join(' ', args);
            var code = Setup.Elevate(elevationArgs);
            // The elevated process reports its own failures; only the cases it cannot report are explained here.
            if (code == Setup.UacDeclined) Notify("Setup was cancelled: you declined the administrator (UAC) prompt.", true);
            else if (Setup.LastElevationError != null) Notify("Setup could not start with administrator rights: " + Setup.LastElevationError, true);
            return code;
        }

        try
        {
            if (cmd is InstallerConstants.RegisterSwitch or InstallerConstants.SetupSwitch or InstallerConstants.InstallServiceSwitch)
            {
                Directory.CreateDirectory(cfg.Installer.InstallDirectory);
                Setup.GrantInstallDirectoryAccess(cfg.Installer.InstallDirectory);
            }
            if (cmd is InstallerConstants.RegisterSwitch or InstallerConstants.SetupSwitch) { Setup.RegisterHandler(); Setup.InstallService(cfg.BaseDirectory); }
            else if (cmd is InstallerConstants.UnregisterSwitch or InstallerConstants.UninstallSwitch) { Setup.UnregisterHandler(); Setup.UninstallService(); }
            else if (cmd == InstallerConstants.InstallServiceSwitch) Setup.InstallService(cfg.BaseDirectory);
            else Setup.UninstallService();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException
                                   or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Notify("Failed: " + ex.Message);
            return 1;
        }
        Notify(cmd is InstallerConstants.RegisterSwitch or InstallerConstants.SetupSwitch ? "Handler registered and service installed."
            : cmd is InstallerConstants.UnregisterSwitch or InstallerConstants.UninstallSwitch ? "Handler unregistered and service uninstalled." : "Done (" + cmd + ").");
        return 0;
    }
}
