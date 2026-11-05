using System.IO.Compression;
using System.Text.Json;

namespace DropIn.Common;

/// <summary>Metadata stored as manifest.json inside a .app package (a ZIP file).</summary>
public sealed class AppManifest
{
    /// <summary>Archive entry name containing the serialized package metadata.</summary>
    public const string ManifestName = "manifest.json";
    /// <summary>File extension used for DropIn package archives.</summary>
    public const string Extension = ".app";

    /// <summary>Display name of the package.</summary>
    public string Name { get; set; } = "";
    /// <summary>Package version represented by this archive.</summary>
    public string Version { get; set; } = "";
    /// <summary>"winget" or "chocolatey".</summary>
    public string Source { get; set; } = "";
    /// <summary>File name of the installer (or portable payload) inside the package.</summary>
    public string InstallerFile { get; set; } = "";
    /// <summary>exe, msi, msix, inno, nsis, burn, zip, portable ...</summary>
    public string InstallerType { get; set; } = "";
    /// <summary>Silent installation switch(es); empty if unknown.</summary>
    public string SilentArgs { get; set; } = "";
    /// <summary>Whether the payload should be installed as a portable application.</summary>
    public bool Portable { get; set; }
    /// <summary>Target processor architecture of the selected installer.</summary>
    public string Architecture { get; set; } = "";
    /// <summary>Installation scope declared by the source manifest.</summary>
    public string Scope { get; set; } = "";
    /// <summary>Locale declared by the source manifest.</summary>
    public string Locale { get; set; } = "";
    /// <summary>Original installer download URL, when available.</summary>
    public string InstallerUrl { get; set; } = "";
    /// <summary>Extra switches appended to <see cref="SilentArgs"/> (winget Switches.Custom).</summary>
    public string CustomArgs { get; set; } = "";
    /// <summary>Entry name of the package icon inside the package; empty if none.</summary>
    public string IconFile { get; set; } = "";
    /// <summary>Entry names of the screenshots inside the package, in display order.</summary>
    public List<string> ScreenshotFiles { get; set; } = new();
    /// <summary>All manifest properties (flattened) the package was built from, for display.</summary>
    public Dictionary<string, string> Properties { get; set; } = new();

    /// <summary>Formatting options used when writing package metadata.</summary>
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>Creates a ZIP-based <c>.app</c> archive containing a manifest, payload, and optional media.</summary>
    /// <param name="appPath">Destination archive path; an existing file is replaced.</param>
    /// <param name="manifest">Metadata to serialize into the archive.</param>
    /// <param name="payloadPath">Path to the installer or portable payload to include.</param>
    /// <param name="media">Optional icon and screenshots to include with the package.</param>
    public static void CreatePackage(string appPath, AppManifest manifest, string payloadPath, PackageMedia? media = null)
    {
        // Store only media with a recognized image format and record its package-relative entry path.
        if (media?.Icon is { } icon && UniGetUiDatabase.ImageExtension(icon) is { } iconExt)
            manifest.IconFile = "icon" + iconExt;
        manifest.ScreenshotFiles = new List<string>();
        if (media != null)
            foreach (var shot in media.Screenshots)
                if (UniGetUiDatabase.ImageExtension(shot) is { } ext)
                    manifest.ScreenshotFiles.Add($"screenshots/{manifest.ScreenshotFiles.Count + 1}{ext}");

        if (File.Exists(appPath)) File.Delete(appPath);
        using var zip = ZipFile.Open(appPath, ZipArchiveMode.Create);
        var entry = zip.CreateEntry(ManifestName);
        using (var w = new StreamWriter(entry.Open())) w.Write(JsonSerializer.Serialize(manifest, Json));
        zip.CreateEntryFromFile(payloadPath, manifest.InstallerFile);
        if (manifest.IconFile.Length > 0) AddBytes(zip, manifest.IconFile, media!.Icon!);
        var shots = media?.Screenshots.Where(b => UniGetUiDatabase.ImageExtension(b) != null).ToList() ?? new();
        for (var i = 0; i < manifest.ScreenshotFiles.Count; i++) AddBytes(zip, manifest.ScreenshotFiles[i], shots[i]);
    }

    /// <summary>Adds an in-memory media item to the archive without recompression.</summary>
    /// <param name="zip">Archive receiving the entry.</param>
    /// <param name="name">Archive entry path.</param>
    /// <param name="data">Bytes to store.</param>
    static void AddBytes(ZipArchive zip, string name, byte[] data)
    {
        using var s = zip.CreateEntry(name, CompressionLevel.NoCompression).Open();
        s.Write(data);
    }

    /// <summary>Reads the icon and screenshots stored in the package; missing or unreadable entries are skipped.</summary>
    /// <param name="appPath">Path to the package archive.</param>
    public static PackageMedia ReadMedia(string appPath)
    {
        var media = new PackageMedia();
        using var zip = ZipFile.OpenRead(appPath);
        var entry = zip.GetEntry(ManifestName);
        if (entry == null) return media;
        AppManifest? m;
        using (var r = new StreamReader(entry.Open())) m = JsonSerializer.Deserialize<AppManifest>(r.ReadToEnd());
        if (m == null) return media;
        media.Icon = ReadEntry(zip, m.IconFile);
        foreach (var name in m.ScreenshotFiles ?? new())
            if (ReadEntry(zip, name) is { } data) media.Screenshots.Add(data);
        return media;
    }

    /// <summary>Reads a bounded archive entry, returning <see langword="null"/> for missing or oversized data.</summary>
    /// <param name="zip">Archive to read.</param>
    /// <param name="name">Entry path within the archive.</param>
    static byte[]? ReadEntry(ZipArchive zip, string name)
    {
        if (string.IsNullOrEmpty(name) || zip.GetEntry(name) is not { } e || e.Length is 0 or > 16 * 1024 * 1024) return null;
        using var s = e.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>Reads only manifest.json from the package.</summary>
    /// <param name="appPath">Path to the package archive.</param>
    public static AppManifest ReadFrom(string appPath)
    {
        using var zip = ZipFile.OpenRead(appPath);
        var entry = zip.GetEntry(ManifestName) ?? throw new InvalidDataException("Package has no " + ManifestName);
        using var r = new StreamReader(entry.Open());
        return JsonSerializer.Deserialize<AppManifest>(r.ReadToEnd()) ?? throw new InvalidDataException("Invalid manifest");
    }

    /// <summary>Extracts the package into <paramref name="destDir"/> and returns its manifest.</summary>
    /// <param name="appPath">Path to the package archive.</param>
    /// <param name="destDir">Directory in which to extract the archive.</param>
    /// <param name="progress">Optional receiver for the extraction fraction (0 to 1).</param>
    public static AppManifest Extract(string appPath, string destDir, Action<double>? progress = null)
    {
        ArchiveExtractor.Extract(appPath, destDir, progress);
        var mf = Path.Combine(destDir, ManifestName);
        if (!File.Exists(mf)) throw new InvalidDataException("Package has no " + ManifestName);
        var m = JsonSerializer.Deserialize<AppManifest>(File.ReadAllText(mf))
                ?? throw new InvalidDataException("Invalid manifest");
        if (string.IsNullOrEmpty(m.InstallerFile) || Path.GetFileName(m.InstallerFile) != m.InstallerFile)
            throw new InvalidDataException("Invalid installer file name in manifest");
        return m;
    }
}
