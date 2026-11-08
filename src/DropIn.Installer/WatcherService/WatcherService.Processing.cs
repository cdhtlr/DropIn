using DropIn.Common;

namespace DropIn.Installer;

sealed partial class WatcherService
{
    /// <summary>Waits for a package copy to finish, installs it, and reports the result.</summary>
    void Process(string path, CancellationToken ct)
    {
        _cfg.EnsureDataAvailable();
        if (!WaitUntilReadable(path, ct)) return;
        _cfg.EnsureDataAvailable();

        var title = Path.GetFileNameWithoutExtension(path);
        var isPackage = path.EndsWith(AppManifest.Extension, StringComparison.OrdinalIgnoreCase);
        if (isPackage)
        try { var m = AppManifest.ReadFrom(path); if (!string.IsNullOrWhiteSpace(m.Name)) title = m.Name; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException) { }

        // Progress/result window runs in the user session (a service cannot show UI) and ends when the status file appears.
        var statusFile = Path.Combine(StateDir(InstallerConstants.StatusDirectoryName), Guid.NewGuid().ToString("N") + InstallerConstants.StatusFileExtension);
        Directory.CreateDirectory(Path.GetDirectoryName(statusFile)!);
        var shown = InteractiveSession.Start(Setup.Exe,
            $"{InstallerConstants.ProgressSwitch} \"{statusFile}\" \"{title.Replace('"', '\'')}\" \"{path}\"");

        InstallResult r;
        try
        {
            Action hide = () =>
            {
                if (!shown) return;
                try { File.WriteAllText(statusFile + InstallerConstants.StatusHideSuffix, ""); }
                catch (IOException) { }
            };
            var progress = new FileProgress(statusFile + InstallerConstants.StatusProgressSuffix);
            r = isPackage
                ? InstallRunner.InstallPackage(path, _cfg, hide, progress)
                : InstallRunner.InstallFile(path, _cfg, null, progress, waitForInteractive: true, onInteractive: hide);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        { r = new InstallResult(false, ex.Message); }

        Log(path, (r.Success ? "OK: " : "FAILED: ") + r.Message);
        // A fast install can finish before the window has read the icon and screenshots from the package.
        if (shown) WaitForMediaRead(statusFile + InstallerConstants.StatusReadySuffix);
        DeletePackage(path);

        try
        {
            if (shown)
            {
                var tmp = statusFile + InstallerConstants.StatusTemporarySuffix;
                File.WriteAllText(tmp, (r.Success ? "OK\n" : "FAILED\n") + (r.InstallDirectory ?? "") + "\n" + (r.Portable ? "portable" : "") + "\n" + r.Message);
                // Publish by rename so the UI never reads a partially written status file.
                File.Move(tmp, statusFile, true);
            }
        }
        catch (IOException ex) { Log(path, "Could not report the result: " + ex.Message); }

        // Without a UI session nothing consumes the helper files, so remove them here.
        if (!shown)
            foreach (var f in new[] { statusFile + InstallerConstants.StatusProgressSuffix, statusFile + InstallerConstants.StatusHideSuffix, statusFile + InstallerConstants.StatusReadySuffix })
                try { File.Delete(f); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Shows a Yes/No question in the signed-in user's session and waits for the answer; no answer within the timeout, or no user session to ask, counts as Yes.</summary>
    bool AskUser(string question)
    {
        var basePath = Path.Combine(StateDir(InstallerConstants.StatusDirectoryName), Guid.NewGuid().ToString("N"));
        var answerFile = basePath + InstallerConstants.AnswerSuffix;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(basePath)!);
            File.WriteAllText(basePath + InstallerConstants.QuestionSuffix, question);
            if (!InteractiveSession.Start(Setup.Exe, $"{InstallerConstants.ConfirmSwitch} \"{basePath}\"")) return true;
            var deadline = DateTime.UtcNow + InstallerConstants.ConfirmTimeout;
            while (DateTime.UtcNow < deadline && _cts is { IsCancellationRequested: false })
            {
                if (File.Exists(answerFile)) return File.ReadAllText(answerFile).Trim() == "yes";
                Thread.Sleep(InstallerConstants.StatusPollInterval);
            }
            return _cts is { IsCancellationRequested: false };
        }
        catch (IOException) { return true; }
        finally
        {
            foreach (var f in new[] { answerFile, basePath + InstallerConstants.QuestionSuffix })
                try { File.Delete(f); } catch (IOException) { }
        }
    }

    /// <summary>Waits until the progress window reports it has read the package (or the timeout passes), then removes the marker.</summary>
    void WaitForMediaRead(string readyFile)
    {
        var deadline = DateTime.UtcNow + InstallerConstants.MediaReadyTimeout;
        while (!File.Exists(readyFile) && DateTime.UtcNow < deadline && _cts is { IsCancellationRequested: false })
            Thread.Sleep(InstallerConstants.StatusPollInterval);
        try { File.Delete(readyFile); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>The dropped file is always removed after an attempt; dropping it again installs it again.</summary>
    void DeletePackage(string path)
    {
        for (var i = 0; i < InstallerConstants.PackageDeleteAttempts; i++)
        {
            try { File.Delete(path); return; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (i == InstallerConstants.PackageDeleteAttempts - 1) Log(path, "Could not delete the package: " + ex.Message);
                else Thread.Sleep(InstallerConstants.PackageDeleteRetryInterval);
            }
        }
    }

    /// <summary>Waits until the file is no longer being copied (can be opened exclusively).</summary>
    static bool WaitUntilReadable(string path, CancellationToken ct)
    {
        for (var i = 0; i < InstallerConstants.PackageReadinessAttempts && !ct.IsCancellationRequested; i++)
        {
            if (!File.Exists(path)) return false;
            try
            {
                // Exclusive access indicates the producer has finished writing the dropped package.
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
                return fs.Length > 0;
            }
            catch (IOException) { ct.WaitHandle.WaitOne(InstallerConstants.StatusPollInterval); }
        }
        return false;
    }

    /// <summary>Writes a package-specific message to the application and service logs.</summary>
    void Log(string path, string message)
    {
        try { _cfg.EnsureDataAvailable(); }
        catch (DataDirectoryUnavailableException ex)
        {
            Program.ReportServiceError(ex.Message + " " + Path.GetFileName(path) + ": " + message);
            return;
        }
        AppLog.Info(Path.GetFileName(path) + ": " + message);
        try
        {
            Directory.CreateDirectory(StateDir(""));
            File.AppendAllText(Path.Combine(StateDir(""), InstallerConstants.ServiceLogFileName),
                $"{DateTime.Now:s} {Path.GetFileName(path)}: {message}{Environment.NewLine}");
        }
        catch (IOException) { }
    }
}
