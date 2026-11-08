namespace DropIn.Common;

/// <summary>Resilient full and ranged download operations.</summary>
public static partial class Net
{
    /// <summary>Determines whether a download error is eligible for retry.</summary>
static bool IsTransient(Exception ex) => ex is HttpRequestException or IOException or InvalidDataException
        || ex is TaskCanceledException;

    /// <summary>Retries transient network failures (connection resets, truncated bodies) with a short backoff.</summary>
    static async Task DownloadFullAsync(string url, string path, long? expectedTotal,
        IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try { await DownloadFullOnceAsync(url, path, expectedTotal, progress, ct); return; }
            catch (Exception ex) when (attempt < MaxAttempts && !ct.IsCancellationRequested && IsTransient(ex))
            {
                await Task.Delay(TimeSpan.FromSeconds(attempt * 2), ct);
            }
        }
    }

    /// <summary>Performs one full-file request and validates the response size when known.</summary>
    static async Task DownloadFullOnceAsync(string url, string path, long? expectedTotal,
        IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        if (response.StatusCode == System.Net.HttpStatusCode.PartialContent)
            throw new InvalidDataException("Server returned a partial response to a full download request.");
        var total = response.Content.Headers.ContentLength;
        if (expectedTotal.HasValue && total.HasValue && total != expectedTotal)
            throw new InvalidDataException("Server returned an unexpected download size.");
        await using var output = File.Create(path);
        await CopyWithProgressAsync(response.Content, output, total ?? expectedTotal, progress, ct);
    }

    /// <summary>Downloads and assembles disjoint byte ranges using temporary part files.</summary>
    static async Task DownloadRangesAsync(string url, string path, long total, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        var tempDir = path + "." + Guid.NewGuid().ToString("N") + ".parts";
        Directory.CreateDirectory(tempDir);
        var ranges = Enumerable.Range(0, ParallelConnections)
            .Select(i => (Start: total * i / ParallelConnections, End: total * (i + 1) / ParallelConnections - 1))
            .Where(r => r.Start <= r.End).ToArray();
        long received = 0;
        try
        {
            await Task.WhenAll(ranges.Select(async (range, index) =>
            {
                var partPath = Path.Combine(tempDir, index.ToString());
                var length = range.End - range.Start + 1;
                long written = 0;
                // A dropped connection resumes the part from where it stopped instead of failing the whole download.
                for (var attempt = 1; written < length; attempt++)
                {
                    try
                    {
                        using var request = new HttpRequestMessage(HttpMethod.Get, url);
                        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(range.Start + written, range.End);
                        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                        response.EnsureSuccessStatusCode();
                        if (response.StatusCode != System.Net.HttpStatusCode.PartialContent ||
                            response.Content.Headers.ContentRange?.From != range.Start + written ||
                            response.Content.Headers.ContentRange?.To != range.End ||
                            response.Content.Headers.ContentRange?.Length != total)
                            throw new InvalidDataException("Server did not honor a requested download range.");
                        await using var input = await response.Content.ReadAsStreamAsync(ct);
                        await using var output = new FileStream(partPath, written == 0 ? FileMode.Create : FileMode.Append);
                        var buffer = new byte[BufferSize];
                        int read;
                        while ((read = await input.ReadAsync(buffer, ct)) > 0)
                        {
                            await output.WriteAsync(buffer.AsMemory(0, read), ct);
                            written += read;
                            var current = Interlocked.Add(ref received, read);
                            progress?.Report(new DownloadProgress(current, total));
                        }
                    }
                    catch (Exception ex) when (attempt < MaxAttempts && !ct.IsCancellationRequested && IsTransient(ex))
                    {
                        await Task.Delay(TimeSpan.FromSeconds(attempt * 2), ct);
                        written = new FileInfo(partPath).Exists ? new FileInfo(partPath).Length : 0;
                    }
                }
                if (new FileInfo(partPath).Length != length)
                    throw new InvalidDataException("Server returned an incomplete download range.");
            }));

            // Reassemble parts in range order to restore the original byte sequence.
            await using var destination = File.Create(path);
            foreach (var part in Enumerable.Range(0, ranges.Length))
                await using (var input = File.OpenRead(Path.Combine(tempDir, part.ToString())))
                    await input.CopyToAsync(destination, ct);
            progress?.Report(new DownloadProgress(total, total));
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch (IOException) { }
        }
    }

    /// <summary>Copies a response stream while reporting progress and validating its final length.</summary>
    static async Task CopyWithProgressAsync(HttpContent content, Stream destination, long? total,
        IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        await using var input = await content.ReadAsStreamAsync(ct);
        var buffer = new byte[BufferSize];
        long received = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, ct)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read), ct);
            received += read;
            progress?.Report(new DownloadProgress(received, total));
        }
        if (total.HasValue && received != total.Value)
            throw new InvalidDataException("The downloaded file was incomplete.");
        progress?.Report(new DownloadProgress(received, total));
    }
}
