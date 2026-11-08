using DropIn.Common;
namespace DropIn.Installer;

sealed partial class ProgressForm
{
    /// <summary>Prevents user dismissal until the result permits closing.</summary>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_allowClose && e.CloseReason == CloseReason.UserClosing) e.Cancel = true;
        base.OnFormClosing(e);
    }

    /// <summary>Releases the screenshot rotation timer when the form closes.</summary>
    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _rotate.Dispose();
        _estimate.Dispose();
        base.OnFormClosed(e);
    }

    /// <summary>Allows the form to close and closes it immediately.</summary>
    void Finish() { _allowClose = true; Close(); }

    /// <summary>Records whether an interactive installer caused this window to be hidden.</summary>
    bool _hiddenForInteractive;

    /// <summary>Hides the window while the installer shows its own UI (called from any thread).</summary>
    void HideForInteractive()
    {
        if (!IsHandleCreated) return;
        BeginInvoke(() => { _hiddenForInteractive = true; Hide(); });
    }

    /// <summary>Stops the progress bar and shows the result; the user closes the window.</summary>
    void ShowResult(bool success, string text)
    {
        // The slideshow keeps rotating after completion in Loop mode; UntilFinished stops it here.
        if (DropInConfig.Current.Installer.ScreenshotSlideshow.Equals("UntilFinished", StringComparison.OrdinalIgnoreCase)) _rotate.Stop();
        CompleteProgress(success);
        _result.Text = text;
        _result.Visible = _close.Visible = true;
        ControlBox = true;
        _allowClose = true;
        _close.Focus();
    }

}
