using System.Reflection;
using System.Runtime.InteropServices;

namespace DropIn.Installer;

sealed partial class ProgressForm
{
    /// <summary>Decodes image bytes into a detached image, or returns null for missing or invalid data.</summary>
    static Image? LoadImage(byte[]? data)
    {
        if (data == null) return null;
        try
        {
            using var ms = new MemoryStream(data);
            using var source = Image.FromStream(ms);
            return new Bitmap(source);
        }

        catch (Exception ex) when (ex is ArgumentException or ExternalException or OutOfMemoryException) { return null; }
    }

    /// <summary>Renders a Segoe MDL2 glyph into a small image.</summary>
    static Image GlyphImage(string glyph)
    {
        var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        using var font = new Font("Segoe MDL2 Assets", 11f, GraphicsUnit.Pixel);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(glyph, font, SystemBrushes.ControlText, new RectangleF(0, 0, 16, 16), format);
        return bmp;
    }

    /// <summary>Loads the package file icon from disk or the embedded application icon.</summary>
    static Image? LoadDefaultIcon()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "file_icon.ico");
            if (File.Exists(path)) { using var i = new Icon(path, 48, 48); return i.ToBitmap(); }
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("app_icon.ico");
            if (s != null) { using var i = new Icon(s, 48, 48); return i.ToBitmap(); }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or ExternalException) { }
        return null;
    }

    /// <summary>Sets the form icon from an image while ignoring unsupported image handles.</summary>
    void TrySetIcon(Image image)
    {
        try { Icon = Icon.FromHandle(new Bitmap(image, 32, 32).GetHicon()); }
        catch (Exception ex) when (ex is ArgumentException or ExternalException) { }
    }

    /// <summary>Displays the requested screenshot, wrapping the index around the carousel.</summary>
    void ShowShot(int index)
    {
        if (_shots.Count == 0) return;
        _index = (index % _shots.Count + _shots.Count) % _shots.Count;
        _carousel.Image = _shots[_index];
        _position.Text = $"{_index + 1} / {_shots.Count}";
        if (_rotate.Enabled) { _rotate.Stop(); _rotate.Start(); }
    }

    /// <summary>Restores and briefly promotes the window so it is visible to the user.</summary>
    void BringToForeground()
    {
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        TopMost = true;
        Activate();
        SetForegroundWindow(Handle);
        TopMost = false;
    }

}
