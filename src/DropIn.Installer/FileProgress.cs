using DropIn.Common;

namespace DropIn.Installer;

/// <summary>
/// Relays installation progress from the service (session 0, no UI) to the progress window in the user session
/// by writing "percent", "stage" and "estimated" lines to a file that the window polls.
/// </summary>
sealed class FileProgress(string path) : IProgress<InstallProgressInfo>
{
    /// <summary>The last percentage written, used to skip redundant writes.</summary>
    int _lastPercent = -1;

    /// <summary>The last stage written, used to skip redundant writes.</summary>
    string _lastStage = "";

    /// <summary>Writes the update when the percentage or stage changed; failures are ignored because progress is only cosmetic.</summary>
    public void Report(InstallProgressInfo value)
    {
        if (value.Percent == _lastPercent && value.Stage == _lastStage) return;
        _lastPercent = value.Percent;
        _lastStage = value.Stage;
        try { File.WriteAllText(path, $"{value.Percent}\n{value.Stage}\n{(value.Estimated ? 1 : 0)}"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
