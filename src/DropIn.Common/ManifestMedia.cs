using System.Text.RegularExpressions;

namespace DropIn.Common;

/// <summary>Icon and screenshots published by the package source itself (winget manifest or Chocolatey nuspec).</summary>
public static class ManifestMedia
{
    /// <summary>Loads source-published icons and screenshots from package metadata or local package files.</summary>
    /// <param name="info">Package metadata and optional extraction directory.</param>
    /// <param name="ct">Token used to cancel file and network operations.</param>
    /// <returns>Media bytes and their original source locations.</returns>
    public static async Task<PackageMedia> LoadAsync(ManifestInfo info, CancellationToken ct = default)
    {
        var media = new PackageMedia();

        var iconCandidates = new List<string>();
        // Sort indexed metadata keys so candidate images are considered in their declared order.
        foreach (var kv in info.Metadata.Where(kv => Regex.IsMatch(kv.Key, @"^Icons\[\d+\]\.IconUrl$")).OrderBy(kv => kv.Key))
            iconCandidates.Add(kv.Value);
        if (info.Metadata.TryGetValue("Nuspec.icon", out var embedded) && embedded.Length > 0 && info.WorkDir != null)
        {
            var path = Path.GetFullPath(Path.Combine(info.WorkDir, embedded.Replace('/', Path.DirectorySeparatorChar)));
            // Accept an embedded icon only when its resolved path remains inside the extracted package directory.
            if (path.StartsWith(Path.GetFullPath(info.WorkDir), StringComparison.OrdinalIgnoreCase) && File.Exists(path))
            {
                var bytes = await File.ReadAllBytesAsync(path, ct);
                if (UniGetUiDatabase.ImageExtension(bytes) != null) { media.Icon = bytes; media.IconSource = path; }
            }
        }
        if (info.Metadata.TryGetValue("Nuspec.iconUrl", out var iconUrl)) iconCandidates.Add(iconUrl);
        foreach (var url in iconCandidates)
        {
            if (media.Icon != null) break;
            if (await DownloadAsync(url, ct) is { } bytes) { media.Icon = bytes; media.IconSource = url; }
        }

        foreach (var kv in info.Metadata.Where(kv => Regex.IsMatch(kv.Key, @"^Screenshots?\[\d+\](\.ScreenshotUrl)?$")).OrderBy(kv => kv.Key))
            if (await DownloadAsync(kv.Value, ct) is { } bytes)
            {
                media.Screenshots.Add(bytes);
                media.ScreenshotSources.Add(kv.Value);
            }
        return media;
    }

    /// <summary>Downloads a URL only when it is HTTP(S) and the response contains a recognized image.</summary>
    /// <param name="url">Candidate image URL from source metadata.</param>
    /// <param name="ct">Token used to cancel the request.</param>
    /// <returns>Image bytes, or <see langword="null"/> for invalid, unavailable, or non-image content.</returns>
    static async Task<byte[]?> DownloadAsync(string url, CancellationToken ct)
    {
        // Do not fetch local files or non-web schemes from manifest metadata.
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return null;
        try
        {
            var bytes = await Net.GetBytesAsync(url, ct);
            return UniGetUiDatabase.ImageExtension(bytes) == null ? null : bytes;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested) { return null; }
    }
}
