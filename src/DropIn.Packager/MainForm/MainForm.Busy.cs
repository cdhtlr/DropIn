namespace DropIn.Packager;

sealed partial class MainForm
{
    /// <summary>The cancellation source for the currently running search, fetch, or build action.</summary>
    CancellationTokenSource? _cts;
    /// <summary>The original text and image saved while an action button displays Cancel.</summary>
    readonly Dictionary<Button, (string Text, Image? Image)> _idleLook = new();
    /// <summary>The glyph image used when an active action button becomes a Cancel button.</summary>
    Image? _cancelImage;

    /// <summary>While an action runs, its button turns into Cancel and everything else is disabled.</summary>
    void Busy(bool on, Button? active = null)
    {
        _progress.Visible = on;
        if (on)
        {
            _progress.Style = ProgressBarStyle.Marquee;
            _progress.Value = 0;
        }
        _downloadStatus.Text = "";
        _searchResults.Enabled = _source.Enabled = _package.Enabled = _version.Enabled = !on;
        foreach (var b in new[] { _search, _fetch, _create })
        {
            var isActive = on && b == active;
            if (isActive)
            {
                _idleLook[b] = (b.Text, b.Image);
                b.Text = PackagerConstants.CancelText;
                b.Image = _cancelImage;
            }
            else if (_idleLook.Remove(b, out var look))
            {
                b.Text = look.Text;
                b.Image = look.Image;
            }
            b.Enabled = isActive || (!on && (b != _create || _current != null));
        }
    }

    /// <summary>Runs an action cancelable through its button; a second click on that button cancels it.</summary>
    async Task RunAsync(Button button, Func<CancellationToken, Task> work)
    {
        if (_cts != null) { _cts.Cancel(); return; }
        using var cts = new CancellationTokenSource();
        _cts = cts;
        Busy(true, button);
        try
        {
            Cfg.EnsureDataAvailable();
            await work(cts.Token);
        }
        catch (OperationCanceledException) { Log("Canceled."); }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or IOException or UnauthorizedAccessException or UriFormatException or TimeoutException or InvalidDataException or YamlDotNet.Core.YamlException or System.Text.Json.JsonException or System.ComponentModel.Win32Exception or System.Security.SecurityException)
        {
            Log("Error: " + ex.Message + (ex.InnerException != null ? " (" + ex.InnerException.Message + ")" : ""));
        }
        finally
        {
            _cts = null;
            Busy(false);
        }
    }
}
