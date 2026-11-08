using System.Text.Json;

namespace DropIn.Common;

/// <summary>Persistence and parsing operations for the UniGetUI package media database.</summary>
public static partial class UniGetUiDatabase
{
    static async Task<Index?> LoadAsync(CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            // Try the local cache first and download only when the cache cannot be loaded.
            if (_index == null && !await LoadLocalAsync(ct))
                await DownloadDatabaseAsync(ct);
            return _index;
        }
        finally { Gate.Release(); }
    }

    /// <summary>Loads both required database files from disk, migrating the previous cache if needed.</summary>
    static async Task<bool> LoadLocalAsync(CancellationToken ct)
    {
        var v2Path = Path.Combine(DatabaseDirectory, CommonFileNames.UniGetUiV2Database);
        var v1Path = Path.Combine(DatabaseDirectory, CommonFileNames.UniGetUiV1Database);
        if (!File.Exists(v2Path) && File.Exists(PreviousDatabasePath))
        {
            Directory.CreateDirectory(DatabaseDirectory);
            try { File.Move(PreviousDatabasePath, v2Path); }
            catch (IOException) when (File.Exists(v2Path)) { }
            try
            {
                var oldDirectory = Path.GetDirectoryName(PreviousDatabasePath)!;
                Directory.Delete(oldDirectory);
                Directory.Delete(Path.GetDirectoryName(oldDirectory)!);
            }
            catch (IOException) { }
        }
        // Both files are required so an older cache without the v1 file triggers a full download.
        if (!File.Exists(v2Path) || !File.Exists(v1Path)) return false;
        _index = ParseDatabase(await File.ReadAllBytesAsync(v2Path, ct), await File.ReadAllBytesAsync(v1Path, ct));
        return true;
    }

    /// <summary>Downloads, validates, and atomically saves both database versions and icons.</summary>
    static async Task DownloadDatabaseAsync(CancellationToken ct)
    {
        var v2 = await Net.GetBytesAsync(CommonUrls.UniGetUiRawBase + CommonFileNames.UniGetUiV2Database, ct);
        var v1 = await Net.GetBytesAsync(CommonUrls.UniGetUiRawBase + CommonFileNames.UniGetUiV1Database, ct);
        var index = ParseDatabase(v2, v1);
        Directory.CreateDirectory(DatabaseDirectory);
        await WriteAtomicAsync(Path.Combine(DatabaseDirectory, CommonFileNames.UniGetUiV2Database), v2, ct);
        await WriteAtomicAsync(Path.Combine(DatabaseDirectory, CommonFileNames.UniGetUiV1Database), v1, ct);
        await DownloadIconsAsync(ct);
        _index = index;
    }

    /// <summary>Mirrors WebBasedData/icons; the icon files are optional, so failures here are ignored.</summary>
    static async Task DownloadIconsAsync(CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(await Net.GetBytesAsync(CommonUrls.UniGetUiIconsApi, ct));
            Directory.CreateDirectory(IconsDirectory);
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var name = item.TryGetProperty("name", out var n) ? n.GetString() : null;
                var url = item.TryGetProperty("download_url", out var u) ? u.GetString() : null;
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(url) || name != Path.GetFileName(name)) continue;
                await WriteAtomicAsync(Path.Combine(IconsDirectory, name), await Net.GetBytesAsync(url, ct), ct);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException or IOException
                                   || ex is TaskCanceledException && !ct.IsCancellationRequested)
        {
        }
    }

    /// <summary>Writes bytes to a sibling file and renames it into place after completion.</summary>
    static async Task WriteAtomicAsync(string path, byte[] bytes, CancellationToken ct)
    {
        var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(tempPath, bytes, ct);
            File.Move(tempPath, path, true);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    /// <summary>Parses both database formats into case-insensitive lookup maps.</summary>
    static Index ParseDatabase(byte[] v2Bytes, byte[] v1Bytes)
    {
        var index = new Index();
        using (var doc = JsonDocument.Parse(v2Bytes))
        {
            if (!doc.RootElement.TryGetProperty("icons_and_screenshots", out var entries) ||
                entries.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("UniGetUI database has no valid icons_and_screenshots data.");
            foreach (var p in entries.EnumerateObject()) index.V2[p.Name] = p.Value.Clone();
        }
        using (var doc = JsonDocument.Parse(v1Bytes))
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("UniGetUI database has no valid data.");
            foreach (var section in doc.RootElement.EnumerateObject())
            {
                if (section.Value.ValueKind != JsonValueKind.Object || section.Name == "package_count") continue;
                var map = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in section.Value.EnumerateObject()) map[p.Name] = p.Value.Clone();
                index.V1[section.Name] = map;
            }
        }
        return index;
    }
}
