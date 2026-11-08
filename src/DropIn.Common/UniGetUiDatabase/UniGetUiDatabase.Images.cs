using System.Text.Json;

namespace DropIn.Common;

/// <summary>Image loading and identifier normalization for the UniGetUI package media database.</summary>
public static partial class UniGetUiDatabase
{
    /// <summary>Loads a validated image from a local path, mirrored file, or remote URL.</summary>

    static async Task<byte[]?> DownloadImageAsync(string? url, CancellationToken ct, IProgress<string>? log = null)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                if (!Path.IsPathRooted(url) || !File.Exists(url))
                {
                    log?.Report($"Image skipped (local file not found or path is not absolute): {url}");
                    return null;
                }
                var data = await File.ReadAllBytesAsync(url, ct);
                if (data.Length is > 0 and <= CommonDefaults.MaxImageBytes && ImageExtension(data) != null) return data;
                log?.Report($"Image skipped (not a supported PNG/JPG/GIF/BMP/ICO within the size limit): {url}");
                return null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                log?.Report($"Image could not be read ({ex.Message}): {url}");
                return null;
            }
        }
        try
        {
            // Prefer a mirrored icon when this URL points into the community icon directory.
            var marker = url.IndexOf(CommonUrls.UniGetUiIconsPathMarker, StringComparison.OrdinalIgnoreCase);
            if (marker >= 0)
            {
                var local = Path.Combine(IconsDirectory, Path.GetFileName(url[(marker + CommonUrls.UniGetUiIconsPathMarker.Length)..]));
                if (File.Exists(local))
                {
                    var data = await File.ReadAllBytesAsync(local, ct);
                    if (data.Length is > 0 and <= CommonDefaults.MaxImageBytes && ImageExtension(data) != null) return data;
                }
            }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            var bytes = await Net.GetImageBytesAsync(url, timeout.Token);
            if (bytes.Length is > 0 and <= CommonDefaults.MaxImageBytes && ImageExtension(bytes) != null) return bytes;
            log?.Report($"Image skipped (not a supported PNG/JPG/GIF/BMP/ICO within the size limit): {url}");
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or UriFormatException or IOException || ex is TaskCanceledException && !ct.IsCancellationRequested)
        {
            log?.Report(ex.InnerException is System.Security.Authentication.AuthenticationException
                ? $"Image blocked: the TLS certificate of {HostOf(url)} is invalid ({ex.InnerException.Message}). This usually means the host is blocked or intercepted by your network (ISP/DNS, proxy, antivirus). Use another network/DNS or VPN, set a local image path, or (unsafe) set Packager.Network.AllowInvalidCertificates to true: {url}"
                : $"Image download failed ({Describe(ex)}): {url}");
            return null;
        }
    }

    /// <summary>Returns the host name of a URL for log messages.</summary>
    static string HostOf(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : url;

    /// <summary>Candidate database keys, mirroring how UniGetUI derives icon ids.</summary>
    static IEnumerable<string> Keys(string source, string id)
    {
        var baseId = id.Trim().ToLowerInvariant();
        source = source.Trim().ToLowerInvariant();
        var ids = new List<string> { baseId };
        if (source == "winget" && baseId.Contains('.')) ids.Insert(0, baseId[(baseId.IndexOf('.') + 1)..]);
        if (source == "chocolatey")
            foreach (var suffix in new[] { ".install", ".portable", ".commandline" })
                if (baseId.EndsWith(suffix)) ids.Insert(0, baseId[..^suffix.Length]);
        foreach (var candidate in ids.ToList())
        {
            ids.Add(candidate.Replace('.', '-').Replace('_', '-').Replace(' ', '-'));
            ids.Add(candidate.Replace(".", "").Replace("_", "").Replace(" ", ""));
        }
        return ids.Distinct();
    }
}
