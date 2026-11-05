using System.Text.Json;

namespace DropIn.Common;

/// <summary>Looks up icons and screenshots in the UniGetUI WebBasedData files (github.com/Devolutions/UniGetUI).</summary>
public static partial class UniGetUiDatabase
{
    /// <summary>User-editable entries (same layout as the UniGetUI v1/v2 files); this file is never overwritten by updates.</summary>
    static List<JsonElement> CustomEntries(string source, string id, List<string> keys)
    {
        var result = new List<JsonElement>();
        try
        {
            var path = Path.Combine(DatabaseDirectory, CommonFileNames.CustomDatabase);
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(DatabaseDirectory);
                File.WriteAllText(path, CommonDefaults.CustomDatabaseTemplate);
                return result;
            }
            using var doc = JsonDocument.Parse(File.ReadAllBytes(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return result;
            var candidates = new List<string>(keys) { id.Trim() };
            foreach (var section in doc.RootElement.EnumerateObject())
            {
                if (section.Value.ValueKind != JsonValueKind.Object || section.Name == "package_count") continue;
                var isFlat = section.Name.Equals("icons_and_screenshots", StringComparison.OrdinalIgnoreCase);
                if (!isFlat && !section.Name.Equals(source.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var key in candidates)
                    foreach (var p in section.Value.EnumerateObject())
                        if (p.Value.ValueKind == JsonValueKind.Object && p.Name.Equals(key, StringComparison.OrdinalIgnoreCase))
                            result.Add(p.Value.Clone());
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        return result;
    }

    /// <summary>Directory that stores the downloaded database and custom overlay.</summary>
    static string DatabaseDirectory => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, DropInConfig.Current.Packager.DatabaseDirectory));
    /// <summary>Local directory containing mirrored icon files.</summary>
    static string IconsDirectory => Path.Combine(DatabaseDirectory, CommonFileNames.IconsDirectory);
    /// <summary>Previous cache location used for a one-time migration to the application directory.</summary>
    static readonly string PreviousDatabasePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DropIn", CommonFileNames.LegacyDatabaseDirectory, CommonFileNames.UniGetUiV2Database);
    /// <summary>Serializes database loads and updates.</summary>
    static readonly SemaphoreSlim Gate = new(1, 1);
    /// <summary>In-memory index built from the local database files.</summary>
    static Index? _index;

    /// <summary>Parsed lookup maps for the current and legacy database formats.</summary>
    sealed class Index
    {
        /// <summary>Flat v2 entries, keyed case-insensitively.</summary>
        public Dictionary<string, JsonElement> V2 { get; } = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>v1 entries per package manager (winget, scoop).</summary>
        public Dictionary<string, Dictionary<string, JsonElement>> V1 { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Loads the local database, downloading it only when it is not cached yet.</summary>
    public static async Task<bool> EnsureAvailableAsync(CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct);
        try
        {
            if (_index != null) return false;
            if (await LoadLocalAsync(ct)) return false;
            await DownloadDatabaseAsync(ct);
            return true;
        }
        finally { Gate.Release(); }
    }

    /// <summary>Downloads and installs the latest database files, replacing the cached copy.</summary>
    public static async Task UpdateAsync(CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct);
        try { await DownloadDatabaseAsync(ct); }
        finally { Gate.Release(); }
    }

    /// <summary>Returns null when nothing usable was found; never throws for network or data problems.</summary>
    public static async Task<PackageMedia?> FetchAsync(string source, string id, CancellationToken ct = default,
        bool wantIcon = true, bool wantScreenshots = true, IProgress<string>? log = null)
    {
        try
        {
            var entries = new List<JsonElement>();
            var keys = Keys(source, id).ToList();
            log?.Report("Database keys tried: " + string.Join(", ", keys));
            // User entries are considered first, so they override matching community database entries.
            entries.AddRange(CustomEntries(source, id, keys));
            Index? index = null;
            try { index = await LoadAsync(ct); }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException or UnauthorizedAccessException or InvalidDataException || ex is TaskCanceledException && !ct.IsCancellationRequested)
            {
                log?.Report($"Database could not be loaded ({Describe(ex)}); only custom-database entries are used.");
            }
            if (index != null)
            {
                foreach (var key in keys)
                    if (index.V2.TryGetValue(key, out var e)) entries.Add(e);
                if (index.V1.TryGetValue(source.Trim(), out var section))
                    foreach (var key in keys)
                        if (section.TryGetValue(key, out var e)) entries.Add(e);
            }

            var media = new PackageMedia();
            foreach (var entry in entries)
            {
                if (wantIcon && media.Icon == null && entry.TryGetProperty("icon", out var icon) && icon.ValueKind == JsonValueKind.String)
                {
                    var iconSource = icon.GetString();
                    media.Icon = await DownloadImageAsync(iconSource, ct, log);
                    if (media.Icon != null) media.IconSource = iconSource;
                }
                if (wantScreenshots && media.Screenshots.Count == 0 && entry.TryGetProperty("images", out var images) && images.ValueKind == JsonValueKind.Array)
                    foreach (var image in images.EnumerateArray())
                        if (image.ValueKind == JsonValueKind.String && await DownloadImageAsync(image.GetString(), ct, log) is { } bytes)
                        {
                            media.Screenshots.Add(bytes);
                            media.ScreenshotSources.Add(image.GetString()!);
                        }
            }
            if (entries.Count == 0) log?.Report("No database entry matches these keys.");
            return media.Icon == null && media.Screenshots.Count == 0 ? null : media;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException or InvalidOperationException or UriFormatException or IOException or InvalidDataException)
        {
            log?.Report($"Database lookup failed: {Describe(ex)}");
            return null;
        }
    }

    /// <summary>Returns a short, readable cause for an exception, including the inner cause (for example a TLS failure).</summary>
    static string Describe(Exception ex) => ex.InnerException is { } inner ? $"{ex.Message} {inner.Message}" : ex.Message;

    /// <summary>Returns the file extension for formats the installer can display, or null.</summary>
    public static string? ImageExtension(byte[] d)
    {
        if (d.Length > 8 && d[0] == 0x89 && d[1] == 'P' && d[2] == 'N' && d[3] == 'G') return ".png";
        if (d.Length > 3 && d[0] == 0xFF && d[1] == 0xD8 && d[2] == 0xFF) return ".jpg";
        if (d.Length > 6 && d[0] == 'G' && d[1] == 'I' && d[2] == 'F' && d[3] == '8') return ".gif";
        if (d.Length > 2 && d[0] == 'B' && d[1] == 'M') return ".bmp";
        if (d.Length > 4 && d[0] == 0 && d[1] == 0 && d[2] == 1 && d[3] == 0) return ".ico";
        return null;
    }
}
