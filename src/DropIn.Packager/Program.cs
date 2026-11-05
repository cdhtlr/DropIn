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
        var cfg = DropInConfig.LoadOrCreate(AppContext.BaseDirectory);
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
