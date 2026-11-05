using System.Diagnostics;

namespace DropIn.Common;

/// <summary>Keeps MSI installations free of references to old or deleted installer files.</summary>
public static partial class InstallRunner
{
    /// <summary>What was decided before an MSI install and what to clean afterwards.</summary>
    /// <param name="ProductCode">ProductCode of the package being installed, or null when nothing is tracked.</param>
    /// <param name="ExtraArguments">Extra msiexec arguments.</param>
    sealed record MsiPlan(string? ProductCode, string ExtraArguments)
    {
        /// <summary>A plan that changes nothing.</summary>
        internal static readonly MsiPlan None = new(null, "");
    }

    /// <summary>Exit code of msiexec when the product to remove is not installed.</summary>
    const int MsiUnknownProduct = 1605;

    /// <summary>Switches to a reinstall when the same product is already registered, so the update uses the new package instead of the old registration.</summary>
    /// <param name="file">MSI file about to be installed.</param>
    /// <param name="cfg">Configuration (MsiCleanUpgrade).</param>
    static MsiPlan PrepareMsi(string file, DropInConfig cfg)
    {
        if (!cfg.Installer.MsiCleanUpgrade || !OperatingSystem.IsWindows()) return MsiPlan.None;
        try
        {
            if (MsiTools.ReadPackage(file) is not { } pkg) return MsiPlan.None;
            var reinstall = MsiTools.IsInstalled(pkg.ProductCode) ? "REINSTALL=ALL REINSTALLMODE=vomus" : "";
            return new MsiPlan(pkg.ProductCode, reinstall);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            AppLog.Error("MSI preparation skipped: " + ex.Message);
            return MsiPlan.None;
        }
    }

    /// <summary>Clears the installer source registered for the product after a successful install.</summary>
    /// <param name="plan">Plan returned by <see cref="PrepareMsi"/>.</param>
    static void FinishMsi(MsiPlan plan)
    {
        if (plan.ProductCode == null || !OperatingSystem.IsWindows()) return;
        try
        {
            var error = MsiTools.ClearSources(plan.ProductCode);
            if (error != 0) AppLog.Info($"Could not clear the installer source of {plan.ProductCode} (error {error}).");
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }

    /// <summary>Uninstalls an MSI product and returns the msiexec exit code (an unknown product counts as success).</summary>
    /// <param name="productCode">ProductCode GUID.</param>
    /// <param name="cfg">Configuration supplying the installer timeout.</param>
    static int UninstallMsi(string productCode, DropInConfig cfg)
    {
        var exit = RunAndWait(new ProcessStartInfo("msiexec.exe") { Arguments = $"/x {productCode} /qn /norestart", UseShellExecute = true }, cfg);
        return exit == MsiUnknownProduct ? 0 : exit;
    }

    /// <summary>Runs a command line (such as a registered uninstall string) through cmd.exe and returns its exit code.</summary>
    /// <param name="commandLine">Command line to run.</param>
    /// <param name="cfg">Configuration supplying the installer timeout.</param>
    static int RunCommandLine(string commandLine, DropInConfig cfg)
        => RunAndWait(new ProcessStartInfo("cmd.exe") { Arguments = $"/c \"{commandLine}\"", UseShellExecute = true }, cfg);

    /// <summary>Starts a hidden process, elevating when interactive, waits for it and returns its exit code (-1 when it could not run).</summary>
    /// <param name="psi">Process to run; UseShellExecute must be true.</param>
    /// <param name="cfg">Configuration supplying the installer timeout.</param>
    static int RunAndWait(ProcessStartInfo psi, DropInConfig cfg)
    {
        psi.WindowStyle = ProcessWindowStyle.Hidden;
        if (NonInteractive && OperatingSystem.IsWindows())
        {
            // Never run as SYSTEM: use the logged-on user.
            var full = Path.Combine(Environment.SystemDirectory, psi.FileName);
            var code = InteractiveSession.RunHiddenAsUser(full, psi.Arguments, (int)Math.Min(int.MaxValue, cfg.Installer.InstallerTimeoutMinutes * 60_000L), true, out _);
            return code ?? -1;
        }
        if (!NonInteractive) psi.Verb = "runas";
        try
        {
            using var p = Process.Start(psi);
            if (p == null) return -1;
            var timeout = cfg.Installer.InstallerTimeoutMinutes > 0 ? TimeSpan.FromMinutes(cfg.Installer.InstallerTimeoutMinutes) : Timeout.InfiniteTimeSpan;
            if (!p.WaitForExit(timeout)) { try { p.Kill(true); } catch (InvalidOperationException) { } return -1; }
            return p.ExitCode;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            AppLog.Error("Could not run " + psi.FileName + ": " + ex.Message);
            return -1;
        }
    }
}