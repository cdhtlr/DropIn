using DropIn.Common;

namespace DropIn.Packager;

/// <summary>Provides the graphical and command-line entry points for the packager.</summary>
static class Program
{
    /// <summary>
    /// Loads configuration and starts the GUI or command-line packager.
    /// Main must stay synchronous: [STAThread] is ignored on an async Main, and WinForms needs an STA thread (COM, dialogs, autocomplete).
    /// </summary>
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] is "-h" or "--help")
            return CliRunner.RunAsync(args).GetAwaiter().GetResult();
        DropInConfig cfg;
        try { cfg = DropInConfig.LoadSelected(); }
        catch (DataDirectoryUnavailableException ex) when (args.Length == 0)
        {
            try
            {
                var recovered = DataDirectoryRecovery.Prompt(ex);
                if (recovered == null) return PackagerConstants.ExitPackageFailed;
                cfg = recovered;
            }
            catch (Exception recoveryError) when (recoveryError is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or NotSupportedException)
            {
                MessageBox.Show("Could not recover the DropIn data directory: " + recoveryError.Message,
                    PackagerConstants.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return PackagerConstants.ExitPackageFailed;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            if (args.Length > 0)
            {
                NativeMethods.AttachConsole(PackagerConstants.AttachParentProcess);
                Console.Error.WriteLine("Could not load DropIn configuration: " + ex.Message);
            }
            else
                MessageBox.Show("Could not load DropIn configuration: " + ex.Message,
                    PackagerConstants.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return PackagerConstants.ExitPackageFailed;
        }
        if (args.Length == 0)
        {
            ApplicationConfiguration.Initialize();
            var denied = Elevation.DeniedFolders(cfg.PackagerWriteDirectories(cfg.ResolveOutputDirectory()));
            if (denied.Count > 0)
            {
                // Without write access the packager cannot work, so it only starts elevated; otherwise it exits.
                string? failure = null;
                if (MessageBox.Show(Elevation.Explain(denied) + "\n\nRestart as administrator?\nThe packager cannot run without it.", PackagerConstants.AppTitle,
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    if (Elevation.RestartElevated(null, out failure) != null) return PackagerConstants.ExitSuccess;
                }
                else failure = "administrator rights were not granted.";
                MessageBox.Show("The packager cannot start: it cannot write to " + string.Join(", ", denied) + ": " + failure, PackagerConstants.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return PackagerConstants.ExitPackageFailed;
            }
            Application.Run(new MainForm());
            return PackagerConstants.ExitSuccess;
        }
        return CliRunner.RunAsync(args).GetAwaiter().GetResult();
    }
}
