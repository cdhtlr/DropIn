namespace DropIn.Common;

/// <summary>Icon and screenshots of a package, as raw image bytes.</summary>
public sealed class PackageMedia
{
    /// <summary>Downloaded icon image bytes, when available.</summary>
    public byte[]? Icon { get; set; }
    /// <summary>Downloaded screenshot image bytes.</summary>
    public List<byte[]> Screenshots { get; } = new();
    /// <summary>URL or local path from which the icon was loaded.</summary>
    public string? IconSource { get; set; }
    /// <summary>URLs or local paths corresponding to the screenshots.</summary>
    public List<string> ScreenshotSources { get; } = new();
}
