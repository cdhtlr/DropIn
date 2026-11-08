using System.IO.Compression;

namespace DropIn.Common;

/// <summary>Turns a selected installer option (with user edits) into a .app package.</summary>
public static class PackageBuilder
{
    /// <summary>Output filename template; supported substitutions are package ID, version, and source.</summary>
    static string _namePattern = "{Id}";
    /// <summary>Whether an existing output package may be replaced.</summary>
    static bool _overwrite = true;
    /// <summary>Whether installer checksums are verified when available.</summary>
    static bool _verify = true;
    /// <summary>Optional directory for retaining downloaded installer files.</summary>
    static string? _keepDirectory;

    /// <summary>Optional callback that asks whether to extract the installer icon when no other icon was found.</summary>
    public static Func<string, byte[]?>? IconExtractor { get; set; }

    /// <summary>Sets package naming, overwrite, verification, and download-retention options.</summary>
    public static void Configure(string namePattern, bool overwrite, bool verifyChecksum, string? keepDirectory)
    {
        _namePattern = string.IsNullOrWhiteSpace(namePattern) ? "{Id}" : namePattern;
        _overwrite = overwrite;
        _verify = verifyChecksum;
        _keepDirectory = keepDirectory;
    }

    /// <summary>Builds a collision-safe output path from the configured name template.</summary>
    static string OutputPath(ManifestInfo info, string outDir)
    {
        var name = _namePattern.Replace("{Id}", info.Id).Replace("{Version}", info.Version).Replace("{Source}", info.Source);
        name = SafeFile(name).Trim();
        if (name.Length == 0) name = SafeFile(info.Id);
        var path = Path.Combine(outDir, name + AppManifest.Extension);
        if (_overwrite) return path;
        for (var i = 2; File.Exists(path); i++) path = Path.Combine(outDir, $"{name} ({i}){AppManifest.Extension}");
        return path;
    }

    /// <summary>Asked with the registry/cache warning once the real installer type is known; returning false cancels package creation.</summary>
    public static Func<string, string, bool>? ConfirmInstallerTraces { get; set; }

    /// <summary>Creates an app package from the chosen installer option and optional media.</summary>
    public static async Task<string> BuildAsync(ManifestInfo info, InstallerOption opt, string outDir,
        PackageMedia? media = null, IProgress<string>? log = null, CancellationToken ct = default,
        IProgress<DownloadProgress>? downloadProgress = null)
    {
        Directory.CreateDirectory(outDir);
        var tmp = Path.Combine(Path.GetTempPath(), "dropin_pkg_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var url = opt.Get("InstallerUrl");
            var embedded = opt.Get("EmbeddedFile");
            var declaredType = opt.Get("InstallerType");
            string payload, fileName;

            if (embedded.Length > 0)
            {
                if (info.WorkDir == null) throw new InvalidOperationException("Package contents are no longer available; fetch again.");
                if (embedded.EndsWith('*'))
                {
                    var dir = Path.Combine(info.WorkDir, embedded.TrimEnd('*').TrimEnd('/'));
                    fileName = SafeFile(info.Id) + ".zip";
                    payload = Path.Combine(tmp, fileName);
                    ZipTools(dir, payload);
                    declaredType = "zip";
                }
                else
                {
                    // Resolve and validate the embedded path before copying it out of the extracted package.
                    var src = Path.GetFullPath(Path.Combine(info.WorkDir, embedded));
                    if (!src.StartsWith(Path.GetFullPath(info.WorkDir), StringComparison.Ordinal))
                        throw new InvalidOperationException("Invalid embedded file path");
                    fileName = Path.GetFileName(src);
                    payload = Path.Combine(tmp, fileName);
                    File.Copy(src, payload);
                }
            }
            else
            {
                if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The selected installer has no download URL (e.g. Microsoft Store packages are not supported).");
                fileName = Net.FileNameFromUrl(url);
                if (string.IsNullOrEmpty(Path.GetExtension(fileName)))
                {
                    var n = Silent.Normalize(declaredType);
                    fileName += "." + (n is "msi" or "msix" or "zip" ? n : "exe");
                }
                payload = Path.Combine(tmp, fileName);
                await Net.DownloadAsync(url, payload, log, ct, downloadProgress);
                if (!_verify) log?.Report("Checksum verification is disabled in the configuration.");
                else if (opt.Get("Checksum").Length > 0) Net.VerifyHash(payload, opt.Get("Checksum"), opt.Get("ChecksumType"), log);
                else Net.VerifyHash(payload, opt.Get("InstallerSha256"), "sha256", log);
                if (_keepDirectory != null)
                {
                    try
                    {
                        Directory.CreateDirectory(_keepDirectory);
                        File.Copy(payload, Path.Combine(_keepDirectory, fileName), true);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { log?.Report("Could not keep the downloaded installer: " + ex.Message); }
                }
            }

            var type = Silent.Normalize(declaredType, fileName);
            var silent = new[] { opt.Get("Switches.Silent"), opt.Get("Switches.SilentWithProgress") }.FirstOrDefault(s => s.Length > 0)
                         ?? Silent.DefaultSwitch(type) ?? "";
            var portable = opt.Get(InstallerOption.KindKey) == "portable" || type is "portable" or "zip";
            if (InstallTraces.Describe(type, portable) is { } warning && ConfirmInstallerTraces is { } confirm && !confirm(type, warning))
                throw new OperationCanceledException("Canceled: the installer would leave registry entries and cache.");

            // Copy package metadata first; installer-specific properties override duplicate keys.
            var props = new Dictionary<string, string>();
            foreach (var kv in info.Metadata) props[kv.Key] = kv.Value;
            foreach (var kv in opt.Properties) props[kv.Key] = kv.Value;

            var manifest = new AppManifest
            {
                Name = info.Id, Version = info.Version, Source = info.Source, InstallerFile = fileName,
                InstallerType = type, SilentArgs = silent, CustomArgs = opt.Get("Switches.Custom"), Portable = portable,
                Architecture = opt.Get("Architecture"), Scope = opt.Get("Scope"), Locale = opt.Get("InstallerLocale"),
                InstallerUrl = url, Properties = props
            };
            var app = OutputPath(info, outDir);
            if (media?.Icon == null && fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    if (IconExtractor?.Invoke(payload) is { Length: > 0 } icon)
                    {
                        media ??= new PackageMedia();
                        media.Icon = icon;
                        log?.Report("Icon: taken from the installer file.");
                    }
                    else
                        log?.Report("Using the default DropIn package icon.");
                }
                catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    log?.Report("Could not extract the installer icon: " + ex.Message + " Using the default DropIn package icon.");
                }
            }
            // The archive is created only after the payload and final manifest are ready.
            AppManifest.CreatePackage(app, manifest, payload, media);
            log?.Report($"Created {app} (silent args: '{silent}')");
            return app;
        }
        finally { try { Directory.Delete(tmp, true); } catch (IOException) { } }
    }

    /// <summary>Archives extracted package files while excluding Chocolatey installation scripts.</summary>
    static void ZipTools(string dir, string zipPath)
    {
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(f);
            if (name.StartsWith("chocolatey", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase)) continue;
            zip.CreateEntryFromFile(f, Path.GetRelativePath(dir, f).Replace('\\', '/'));
        }
    }

    /// <summary>Replaces characters that are invalid in a file name.</summary>
    static string SafeFile(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s;
    }
}
