namespace DropIn.Packager;

/// <summary>Extracts icons from executable files.</summary>
static class ExeIconExtractor
{
    /// <summary>Returns the PNG of an executable's associated icon, or null when it has none.</summary>
    public static byte[]? ExtractPng(string path)
    {
        using var icon = Icon.ExtractAssociatedIcon(path);
        if (icon == null) return null;
        using var bitmap = icon.ToBitmap();
        using var stream = new MemoryStream();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        return stream.ToArray();
    }
}
