using System.Runtime.ExceptionServices;
using DropIn.Common;

namespace DropIn.Installer;

sealed partial class ProgressForm
{
    /// <summary>Runs <paramref name="work"/> in the background while the progress window is displayed (blocks until done).</summary>
    public static T Run<T>(string text, PackageMedia? media, Func<Action, IProgress<InstallProgressInfo>, T> work)
    {
        if (!DropInConfig.Current.Installer.ShowProgressWindow) return work(() => { }, InstallProgress.None);
        Application.EnableVisualStyles();
        T result = default!;
        Exception? error = null;
        using var form = new ProgressForm(text, media);
        form.Shown += async (_, _) =>
        {
            try { result = await Task.Run(() => work(form.HideForInteractive, form.Reporter)); }
            catch (Exception ex) { error = ex; }
            form.Finish();
        };
        Application.Run(form);
        if (error != null) ExceptionDispatchInfo.Throw(error);
        return result;
    }

    /// <summary>
    /// Shows a non-blocking window for an installation inside a running message loop. The window stays open
    /// with the result until the user closes it, so further packages can be installed meanwhile.
    /// </summary>
    public static void Start(string title, PackageMedia? media, Func<Action, IProgress<InstallProgressInfo>, InstallResult> work, Action? onStarted = null,
        Action? onFinished = null, Action<InstallResult>? onCompleted = null)
    {
        if (!DropInConfig.Current.Installer.ShowProgressWindow)
        {
            var context = SynchronizationContext.Current;
            onStarted?.Invoke();
            Task.Run(() =>
            {
                InstallResult r;
                try { r = work(() => { }, InstallProgress.None); }
                catch (Exception ex) { r = new InstallResult(false, ex.Message); }
                void Done()
                {
                    if (!r.Success) MessageBox.Show(title + ": " + r.Message, "DropIn", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    onCompleted?.Invoke(r);
                    onFinished?.Invoke();
                }
                if (context != null) context.Post(_ => Done(), null);
                else Done();
            });
            return;
        }
        var form = new ProgressForm("Installing " + title + "...", media);
        form.Shown += async (_, _) =>
        {
            onStarted?.Invoke();
            InstallResult r;
            try { r = await Task.Run(() => work(form.HideForInteractive, form.Reporter)); }
            catch (Exception ex) { r = new InstallResult(false, ex.Message); }
            if (r.Success && (form._hiddenForInteractive || DropInConfig.Current.Installer.AutoCloseProgressWindow)) form.Finish();
            else
            {
                form._title.Text = title + (r.Success ? ": installed" : ": failed");
                form.ShowResult(r.Success, r.Message);
                if (form._hiddenForInteractive) { form.Show(); form.BringToForeground(); }
            }
            onCompleted?.Invoke(r);
            onFinished?.Invoke();
        };
        form.FormClosed += (_, _) => form.Dispose();
        form.Show();
    }

    /// <summary>
    /// Shown by the watcher service in the user session: displays progress until <paramref name="statusFile"/>
    /// appears (first line OK/FAILED, then the message) and returns that result.
    /// </summary>
    public static (bool Success, string Text, string? Directory, bool Portable)? WaitForStatus(string statusFile, string title, string? package)
    {
        if (!DropInConfig.Current.Installer.ShowProgressWindow)
        {
            SignalReady(statusFile);
            for (var waited = TimeSpan.Zero; waited < InstallerConstants.StatusTimeout; waited += InstallerConstants.StatusPollInterval)
            {
                try
                {
                    if (File.Exists(statusFile))
                    {
                        var lines = File.ReadAllText(statusFile).Split('\n', 4);
                        var directory = lines.Length > 1 ? lines[1].Trim() : "";
                        try { File.Delete(statusFile); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                        return (lines[0].Trim() == "OK", title + ": " + (lines.Length > 3 ? lines[3].Trim() : ""), directory.Length > 0 ? directory : null, lines.Length > 2 && lines[2].Trim() == "portable");
                    }
                }
                catch (IOException) { }
                Thread.Sleep(InstallerConstants.PackageDeleteRetryInterval);
            }
            return null;
        }
        Application.EnableVisualStyles();
        (bool, string, string?, bool)? result = null;
        var started = DateTime.UtcNow;
        PackageMedia? media = null;
        try { if (package != null) media = AppManifest.ReadMedia(package); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException) { }
        SignalReady(statusFile);
        using var form = new ProgressForm("Installing " + title + "...", media);
        using var timer = new System.Windows.Forms.Timer { Interval = (int)InstallerConstants.StatusPollInterval.TotalMilliseconds };
        timer.Tick += (_, _) =>
        {
            try
            {
                var hideFile = statusFile + InstallerConstants.StatusHideSuffix;
                if (File.Exists(hideFile))
                {
                    try { File.Delete(hideFile); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                    form.Hide();
                }
                ReadProgress(statusFile + InstallerConstants.StatusProgressSuffix, form);
                if (File.Exists(statusFile))
                {
                    var lines = File.ReadAllText(statusFile).Split('\n', 4);
                    var directory = lines.Length > 1 ? lines[1].Trim() : "";
                    result = (lines[0].Trim() == "OK", title + ": " + (lines.Length > 3 ? lines[3].Trim() : ""),
                        directory.Length > 0 ? directory : null, lines.Length > 2 && lines[2].Trim() == "portable");
                    try { File.Delete(statusFile); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                    form.Finish();
                }
                else if (DateTime.UtcNow - started > InstallerConstants.StatusTimeout) form.Finish();
            }
            catch (IOException) { } // still being written
        };
        form.Shown += (_, _) => timer.Start();
        Application.Run(form);
        try { File.Delete(statusFile + InstallerConstants.StatusProgressSuffix); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return result;
    }

    /// <summary>Tells the service the package file is no longer needed by this window, so the service may delete it.</summary>
    static void SignalReady(string statusFile)
    {
        try { File.WriteAllText(statusFile + InstallerConstants.StatusReadySuffix, ""); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Reads the progress file written by the service ("percent", "stage", "estimated" lines) and shows it.</summary>
    static void ReadProgress(string progressFile, ProgressForm form)
    {
        if (!File.Exists(progressFile)) return;
        string[] lines;
        try { lines = File.ReadAllText(progressFile).Split('\n'); }
        catch (IOException) { return; } // the service is writing it right now
        if (lines.Length >= 2 && int.TryParse(lines[0].Trim(), out var percent))
            form.Apply(new InstallProgressInfo(percent, lines[1].Trim(), lines.Length > 2 && lines[2].Trim() == "1"));
    }
}
