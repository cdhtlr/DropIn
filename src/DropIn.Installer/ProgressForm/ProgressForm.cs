using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using DropIn.Common;

namespace DropIn.Installer;

/// <summary>
/// Installation window: package icon, screenshot carousel (when the package has screenshots) above a marquee progress bar.
/// It is brought to the foreground and never exceeds the active screen.
/// </summary>
sealed partial class ProgressForm : Form
{
    /// <summary>Requests that Windows bring the specified window to the foreground.</summary>
    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hWnd);

    /// <summary>The decoded screenshot images displayed by the carousel.</summary>
    readonly List<Image> _shots = new();
    /// <summary>The height and width, in pixels, reserved for the package icon in the header.</summary>
    const int HeaderIconSize = 56;
    /// <summary>The picture control that displays the current package screenshot.</summary>
    readonly PictureBox _carousel = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(0, 4, 0, 4) };
    /// <summary>The label showing the current screenshot index and total count.</summary>
    readonly Label _position = new() { AutoSize = true, Anchor = AnchorStyles.None };
    /// <summary>The progress indicator shown while installation is active.</summary>
    readonly ProgressBar _bar = new() { Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = InstallerConstants.ProgressMarqueeAnimationSpeed, Dock = DockStyle.Fill };
    /// <summary>The read-only result details displayed after installation finishes.</summary>
    readonly TextBox _result = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Visible = false };
    /// <summary>The button that closes the completed progress window.</summary>
    readonly Button _close = new() { Text = "Close", AutoSize = true, Anchor = AnchorStyles.Right, Visible = false };
    /// <summary>The package title shown in the progress window.</summary>
    readonly Label _title = new() { AutoEllipsis = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    /// <summary>The timer that advances the screenshot carousel.</summary>
    readonly System.Windows.Forms.Timer _rotate = new();
    /// <summary>The index of the currently displayed screenshot.</summary>
    int _index;
    /// <summary>Whether the form may close in response to a user close action.</summary>
    bool _allowClose;

    /// <summary>Builds the progress window and initializes any package media.</summary>
    ProgressForm(string text, PackageMedia? media)
    {
        if (!DropInConfig.Current.Installer.ShowScreenshots && media != null) media = new PackageMedia { Icon = media.Icon };
        var slideshow = DropInConfig.Current.Installer.ScreenshotSlideshow;
        _rotate.Interval = DropInConfig.Current.Installer.ScreenshotSlideshowSeconds * 1000;
        Text = "DropIn";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ControlBox = false;
        MaximizeBox = MinimizeBox = false;
        _title.Text = text;

        var icon = LoadImage(media?.Icon) ?? LoadDefaultIcon();
        if (icon != null) TrySetIcon(icon);
        foreach (var data in media?.Screenshots ?? new()) if (LoadImage(data) is { } image) _shots.Add(image);

        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        var hasShots = _shots.Count > 0;
        var width = Math.Min(hasShots ? 720 : 440, area.Width);
        var height = Math.Min(hasShots ? 660 : 255, area.Height);
        MaximumSize = area.Size;
        StartPosition = FormStartPosition.Manual;
        Bounds = new Rectangle(area.Left + (area.Width - width) / 2, area.Top + (area.Height - height) / 2, width, height);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(12) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, HeaderIconSize + 8));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        // Margin 0 plus a fixed row height keeps the icon fully visible instead of being clipped by the screenshot below.
        header.Controls.Add(new PictureBox { Image = icon, SizeMode = PictureBoxSizeMode.Zoom, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 8, 0) }, 0, 0);
        header.Controls.Add(_title, 1, 0);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, HeaderIconSize + 8));
        layout.Controls.Add(header);

        if (hasShots)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(_carousel);
            var nav = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
            nav.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
            nav.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
            nav.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
            var prev = new Button { Image = GlyphImage("\uE76B"), Width = 36, Anchor = AnchorStyles.Right };
            var next = new Button { Image = GlyphImage("\uE76C"), Width = 36, Anchor = AnchorStyles.Left };
            prev.Click += (_, _) => ShowShot(_index - 1);
            next.Click += (_, _) => ShowShot(_index + 1);
            nav.Controls.Add(prev, 0, 0);
            nav.Controls.Add(_position, 1, 0);
            nav.Controls.Add(next, 2, 0);
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            layout.Controls.Add(nav);
            _rotate.Tick += (_, _) => ShowShot(_index + 1);
            ShowShot(0);
        }

        // Progress bar sits below the carousel; the result box and close button only appear when finished.
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        layout.Controls.Add(_status);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.Controls.Add(_bar);
        InitializeProgress();
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(_result);
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(_close);
        _result.Height = 80;
        Controls.Add(layout);

        _close.Click += (_, _) => Finish();
        Shown += (_, _) =>
        {
            if (_shots.Count > 1 && !slideshow.Equals("Off", StringComparison.OrdinalIgnoreCase)) _rotate.Start();
            BringToForeground();
        };
    }

}
