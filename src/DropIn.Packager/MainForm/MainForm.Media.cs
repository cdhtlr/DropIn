using DropIn.Common;

namespace DropIn.Packager;

sealed partial class MainForm
{
    /// <summary>Loads package media from the manifest and fills any gaps from the media database.</summary>
    async Task LookupMediaAsync(string id, CancellationToken ct)
    {
        var media = _info != null ? await ManifestMedia.LoadAsync(_info, ct) : new PackageMedia();
        var needIcon = media.Icon == null;
        var needScreenshots = media.Screenshots.Count == 0;
        Log(needIcon ? "Icon: not provided by " + SourceName + "." : "Icon: found in the " + SourceName + " package.");
        Log(needScreenshots ? "Screenshots: not provided by " + SourceName + "." : $"Screenshots: {media.Screenshots.Count} found in the {SourceName} package.");

        if (needIcon || needScreenshots)
        {
            var db = await UniGetUiDatabase.FetchAsync(SourceName, id, ct, needIcon, needScreenshots, new Progress<string>(Log));
            if (needIcon)
            {
                if (db?.Icon != null) { media.Icon = db.Icon; media.IconSource = db.IconSource; }
                Log(db?.Icon != null ? "Icon: found in the UniGetUI database." : "Icon: not loaded from the UniGetUI database (see the reason above).");
            }
            if (needScreenshots)
            {
                if (db?.Screenshots.Count > 0) { media.Screenshots.AddRange(db.Screenshots); media.ScreenshotSources.AddRange(db.ScreenshotSources); }
                Log(db?.Screenshots.Count > 0 ? $"Screenshots: {db.Screenshots.Count} found in the UniGetUI database." : "Screenshots: not loaded from the UniGetUI database (see the reason above).");
            }
        }
        _media = media.Icon == null && media.Screenshots.Count == 0 ? null : media;
        _iconInput.Text = _media?.IconSource ?? "";
        _screenshotInput.Text = string.Join(PackagerConstants.ScreenshotSeparator, _media?.ScreenshotSources ?? new List<string>());
    }

    /// <summary>Builds package media from the current icon and screenshot inputs.</summary>
    async Task<PackageMedia?> SelectedMediaAsync(CancellationToken ct)
    {
        var media = new PackageMedia();
        if (!string.IsNullOrWhiteSpace(_iconInput.Text))
            media.Icon = await ResolveImageAsync(_iconInput.Text.Trim(), _media?.IconSource, _media?.Icon, ct);
        foreach (var source in _screenshotInput.Text.Split(PackagerConstants.ScreenshotSeparator, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var existingIndex = _media?.ScreenshotSources.FindIndex(s => s.Equals(source, StringComparison.OrdinalIgnoreCase)) ?? -1;
            var bytes = existingIndex >= 0 ? _media!.Screenshots[existingIndex] : await ResolveImageAsync(source, null, null, ct);
            if (bytes != null) media.Screenshots.Add(bytes);
        }
        return media.Icon == null && media.Screenshots.Count == 0 ? null : media;
    }

    /// <summary>Loads image bytes from a local path or HTTP(S) URL, reusing a matching cached image.</summary>
    static async Task<byte[]?> ResolveImageAsync(string source, string? cachedSource, byte[]? cachedBytes, CancellationToken ct)
    {
        // Reuse fetched media only when the user has left its source unchanged.
        if (cachedSource != null && cachedBytes != null && cachedSource.Equals(source, StringComparison.OrdinalIgnoreCase))
            return cachedBytes;
        // Treat only HTTP(S) absolute URIs as remote inputs; all other values are local paths.
        var bytes = Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? await Net.GetImageBytesAsync(source, ct)
            : await File.ReadAllBytesAsync(source, ct);
        return UniGetUiDatabase.ImageExtension(bytes) == null
            ? throw new InvalidDataException($"Unsupported image file: {source}")
            : bytes;
    }
}
