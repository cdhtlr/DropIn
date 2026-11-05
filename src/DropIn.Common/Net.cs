using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace DropIn.Common;

/// <summary>Progress reported while downloading a file.</summary>
/// <param name="BytesReceived">Bytes downloaded so far.</param>
/// <param name="TotalBytes">Expected total download size, or null when it is unknown.</param>
public sealed record DownloadProgress(long BytesReceived, long? TotalBytes)
{
    /// <summary>Download completion percentage, or null when the total size is unknown.</summary>
    public int? Percentage => TotalBytes is > 0 ? (int)Math.Clamp(BytesReceived * 100 / TotalBytes.Value, 0, 100) : null;
}

/// <summary>Provides configured HTTP requests, resilient downloads, and checksum utilities.</summary>
public static partial class Net
{
    /// <summary>Maximum number of simultaneous range requests for a large download.</summary>
    static int ParallelConnections = CommonDefaults.ParallelConnections;
    /// <summary>Buffer size used when copying response streams to disk.</summary>
    const int BufferSize = CommonDefaults.DownloadBufferSize;
    /// <summary>Maximum number of attempts for each transient download failure.</summary>
    static int MaxAttempts = CommonDefaults.DownloadRetries;
    /// <summary>Small downloads at or below this size use a single request.</summary>
    const long ParallelDownloadThreshold = CommonDefaults.ParallelDownloadThreshold;
    /// <summary>Shared HTTP client configured for package metadata and file downloads.</summary>
    static HttpClient Http = Create("", CommonDefaults.UserAgent);

    /// <summary>Applies the proxy, user agent and download tuning from the configuration.</summary>
    public static void Configure(string? proxyUrl, string? userAgent, int parallelConnections, int attempts, bool allowInvalidCertificates = false)
    {
        ParallelConnections = Math.Clamp(parallelConnections, 1, 16);
        MaxAttempts = Math.Clamp(attempts, 1, 10);
        Http = Create(proxyUrl ?? "", string.IsNullOrWhiteSpace(userAgent) ? CommonDefaults.UserAgent : userAgent, allowInvalidCertificates);
    }

    /// <summary>Creates an HTTP client with the selected proxy and user agent.</summary>
    static HttpClient Create(string proxyUrl, string userAgent, bool allowInvalidCertificates = false)
    {
        var handler = new HttpClientHandler();
        if (allowInvalidCertificates) handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        if (Uri.TryCreate(proxyUrl, UriKind.Absolute, out var proxy))
        {
            handler.Proxy = new System.Net.WebProxy(proxy);
            handler.UseProxy = true;
        }
        var c = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(30) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        return c;
    }

    /// <summary>Downloads a response body as text.</summary>
    public static Task<string> GetStringAsync(string url, CancellationToken ct = default) => Http.GetStringAsync(url, ct);

    /// <summary>Downloads a response body as a byte array.</summary>
    public static Task<byte[]> GetBytesAsync(string url, CancellationToken ct = default) => Http.GetByteArrayAsync(url, ct);

    /// <summary>Browser-like identity for image hosts that reject unknown clients.</summary>
    const string BrowserUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36";

    /// <summary>Downloads an image, presenting browser-like headers because many image hosts return 403 to other clients.</summary>
    public static async Task<byte[]> GetImageBytesAsync(string url, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd(BrowserUserAgent);
        request.Headers.Accept.ParseAdd("image/avif,image/webp,image/png,image/jpeg,image/*;q=0.8");
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri)) request.Headers.Referrer = new Uri(uri.GetLeftPart(UriPartial.Authority) + "/");
        using var response = await Http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    /// <summary>Downloads a file, using parallel byte ranges when the server supports them.</summary>
    public static async Task DownloadAsync(string url, string path, IProgress<string>? log = null, CancellationToken ct = default,
        IProgress<DownloadProgress>? progress = null)
    {
        log?.Report("Downloading " + url);
        using var probe = new HttpRequestMessage(HttpMethod.Get, url);
        probe.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);
        using var first = await Http.SendAsync(probe, HttpCompletionOption.ResponseHeadersRead, ct);
        first.EnsureSuccessStatusCode();

        // Range downloads are used only when the server confirms the exact one-byte probe response.
        var total = first.Content.Headers.ContentRange?.Length;
        if (first.StatusCode == System.Net.HttpStatusCode.PartialContent && total is > 0 &&
            first.Content.Headers.ContentRange?.From == 0 && first.Content.Headers.ContentRange?.To == 0)
        {
            if (total == 1)
            {
                await using var singleByteOutput = File.Create(path);
                await CopyWithProgressAsync(first.Content, singleByteOutput, total, progress, ct);
                return;
            }
            if (total <= ParallelDownloadThreshold)
            {
                await DownloadFullAsync(url, path, total, progress, ct);
                return;
            }
            try
            {
                await DownloadRangesAsync(url, path, total.Value, progress, ct);
            }
            catch (Exception ex) when (ex is InvalidDataException or HttpRequestException)
            {
                log?.Report($"Parallel download failed ({ex.Message}); retrying as a single download.");
                await DownloadFullAsync(url, path, total, progress, ct);
            }
            return;
        }

        if (first.StatusCode == System.Net.HttpStatusCode.PartialContent)
            throw new InvalidDataException("Server returned an invalid partial response for the download.");

        await using var fs = File.Create(path);
        await CopyWithProgressAsync(first.Content, fs, first.Content.Headers.ContentLength, progress, ct);
    }
}
