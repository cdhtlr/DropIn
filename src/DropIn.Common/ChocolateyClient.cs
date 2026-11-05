using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace DropIn.Common;

/// <summary>Reads packages from the Chocolatey community repository (NuGet v2 feed).</summary>
public static class ChocolateyClient
{
    /// <summary>Base URL of the Chocolatey community NuGet v2 feed.</summary>
    const string Feed = "https://community.chocolatey.org/api/v2/";

    /// <summary>Available versions, newest first.</summary>
    /// <param name="id">Chocolatey package identifier.</param>
    /// <param name="ct">Token used to cancel the feed request.</param>
    public static async Task<List<string>> GetVersionsAsync(string id, CancellationToken ct = default)
    {
        var xml = await Net.GetStringAsync($"{Feed}FindPackagesById()?id='{Uri.EscapeDataString(id)}'", ct);
        var doc = XDocument.Parse(xml);
        var versions = doc.Descendants().Where(e => e.Name.LocalName == "Version" && e.Parent?.Name.LocalName == "properties")
            .Select(e => e.Value).Distinct().OrderByDescending(Net.ParseVersion).ToList();
        if (versions.Count == 0) throw new InvalidOperationException($"Chocolatey package '{id}' not found");
        return versions;
    }

    /// <summary>Downloads and inspects a Chocolatey package to produce installer options and metadata.</summary>
    /// <param name="id">Chocolatey package identifier.</param>
    /// <param name="version">Requested version, or <see langword="null"/> for the feed default.</param>
    /// <param name="ct">Token used to cancel network operations.</param>
    public static async Task<ManifestInfo> LoadAsync(string id, string? version, CancellationToken ct = default)
    {
        // Keep each package extraction isolated so concurrent lookups cannot collide.
        var work = Path.Combine(Path.GetTempPath(), "dropin_choco_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        var info = new ManifestInfo { Source = "chocolatey", Id = id, WorkDir = work };
        try
        {
            var nupkg = Path.Combine(work, "pkg.nupkg");
            var ex = Path.Combine(work, "pkg");
            try
            {
                await Net.DownloadAsync($"{Feed}package/{id}" + (string.IsNullOrEmpty(version) ? "" : "/" + version), nupkg, null, ct);
            }
            catch (HttpRequestException) { throw new InvalidOperationException($"Chocolatey package '{id}' {version} not found"); }
            ZipFile.ExtractToDirectory(nupkg, ex, true);
            info.WorkDir = ex;
            File.Delete(nupkg);

            info.Version = version ?? "";
            var nuspec = Directory.GetFiles(ex, "*.nuspec").FirstOrDefault();
            if (nuspec != null)
            {
                var meta = XDocument.Load(nuspec).Descendants().FirstOrDefault(e => e.Name.LocalName == "metadata");
                if (meta != null)
                    foreach (var e in meta.Elements().Where(e => !e.HasElements))
                        info.Metadata["Nuspec." + e.Name.LocalName] = e.Value.Trim();
                if (info.Metadata.TryGetValue("Nuspec.version", out var v) && string.IsNullOrEmpty(info.Version)) info.Version = v;
            }
            BuildOptions(info, ex);
            if (info.Installers.Count == 0 && await LoadDependencyAsync(info, ex, id, ct) is { } dependency)
            {
                info.WorkDir = work;
                info.Dispose();
                return dependency;
            }
            return info;
        }
        catch
        {
            info.WorkDir = work;
            info.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Meta packages (e.g. putty) ship no installer and only depend on the real package (putty.install).
    /// Loads that dependency, keeping the requested id as the package name.
    /// </summary>
    /// <param name="info">Metadata for the originally requested meta-package.</param>
    /// <param name="ex">Directory containing the extracted package.</param>
    /// <param name="id">Identifier to retain on the returned manifest information.</param>
    /// <param name="ct">Token used to cancel dependency lookup and download.</param>
    static async Task<ManifestInfo?> LoadDependencyAsync(ManifestInfo info, string ex, string id, CancellationToken ct)
    {
        var nuspec = Directory.GetFiles(ex, "*.nuspec").FirstOrDefault();
        if (nuspec == null) return null;
        var dependency = XDocument.Load(nuspec).Descendants().Where(e => e.Name.LocalName == "dependency")
            .Select(e => (Id: (string?)e.Attribute("id"), Version: (string?)e.Attribute("version")))
            .OrderByDescending(d => d.Id?.EndsWith(".install", StringComparison.OrdinalIgnoreCase) == true)
            .FirstOrDefault(d => !string.IsNullOrEmpty(d.Id) && !d.Id!.StartsWith("chocolatey", StringComparison.OrdinalIgnoreCase));
        if (dependency.Id == null) return null;
        // "[1.2.3]" pins an exact version; ranges and minimums use the latest one.
        var version = dependency.Version is { Length: > 2 } v && v[0] == '[' && v[^1] == ']' && !v.Contains(',') ? v[1..^1] : null;
        if (version == null && dependency.Version == info.Version) version = info.Version;
        var loaded = await LoadAsync(dependency.Id, version ?? (await GetVersionsAsync(dependency.Id, ct))[0], ct);
        loaded.Id = id;
        return loaded;
    }

    /// <summary>Chocolatey install-script variables that may provide package metadata.</summary>
    static readonly string[] ScriptKeys =
        { "packageName", "fileType", "url", "url64bit", "checksum", "checksumType", "checksum64", "checksumType64",
          "silentArgs", "validExitCodes", "unzipLocation", "softwareName" };

    /// <summary>Builds installer choices from the Chocolatey install script or embedded tools.</summary>
    /// <param name="info">Package information to populate.</param>
    /// <param name="ex">Directory containing the extracted package.</param>
    static void BuildOptions(ManifestInfo info, string ex)
    {
        // Select the conventional install script; Chocolatey packages may contain unrelated PowerShell files.
        var scripts = Directory.GetFiles(ex, "*.ps1", SearchOption.AllDirectories)
            .Where(f => Path.GetFileName(f).Equals("chocolateyinstall.ps1", StringComparison.OrdinalIgnoreCase)).ToList();
        var script = scripts.Count > 0 ? File.ReadAllText(scripts[0]) : "";

        string Var(string name) => Regex.Match(script, $@"(?im)^\s*\$?{name}\s*=\s*['""]([^'""]+)['""]").Groups[1].Value;
        var v = ScriptKeys.ToDictionary(k => k, Var);
        // Positional form: Install-ChocolateyPackage 'name' 'type' 'silent' 'url' ['url64']
        var pos = Regex.Match(script, @"Install-ChocolateyPackage\s+['""]([^'""]+)['""]\s+['""]([^'""]+)['""]\s+['""]([^'""]*)['""]\s+['""]([^'""]+)['""](?:\s+['""]([^'""]+)['""])?",
            RegexOptions.IgnoreCase);
        if (pos.Success)
        {
            if (v["fileType"].Length == 0) v["fileType"] = pos.Groups[2].Value;
            if (v["silentArgs"].Length == 0) v["silentArgs"] = pos.Groups[3].Value;
            if (v["url"].Length == 0) v["url"] = pos.Groups[4].Value;
            if (v["url64bit"].Length == 0) v["url64bit"] = pos.Groups[5].Value;
        }
        foreach (var k in new[] { "url", "url64bit" })
            v[k] = v[k].Replace("$version", info.Version, StringComparison.OrdinalIgnoreCase).Replace("$packageVersion", info.Version, StringComparison.OrdinalIgnoreCase);

        // These helpers indicate that the script deploys a portable payload rather than a conventional installation.
        var zipScript = Regex.IsMatch(script, "Install-ChocolateyZipPackage|Get-ChocolateyUnzip", RegexOptions.IgnoreCase);

        // Map a discovered script payload to the common installer-option representation.
        InstallerOption Make(string arch, string url, string sum, string sumType)
        {
            var o = new InstallerOption();
            o.Properties["Architecture"] = arch;
            o.Properties["InstallerUrl"] = url;
            o.Properties["InstallerSha256"] = sumType.Length == 0 || sumType.Equals("sha256", StringComparison.OrdinalIgnoreCase) ? sum : "";
            o.Properties["ChecksumType"] = sumType.Length == 0 ? "sha256" : sumType;
            o.Properties["Checksum"] = sum;
            var type = Silent.Normalize(v["fileType"], url);
            o.Properties["InstallerType"] = v["fileType"].Length > 0 ? v["fileType"] : type;
            o.Properties["Switches.Silent"] = v["silentArgs"];
            foreach (var kv in v.Where(kv => kv.Value.Length > 0 && kv.Key is not ("url" or "url64bit" or "checksum" or "checksum64" or "checksumType" or "checksumType64")))
                o.Properties["Script." + kv.Key] = kv.Value;
            o.Properties[InstallerOption.KindKey] = zipScript || type is "zip" or "portable" ? "portable" : "installer";
            return o;
        }

        if (v["url64bit"].Length > 0) info.Installers.Add(Make("x64", v["url64bit"], v["checksum64"], v["checksumType64"].Length > 0 ? v["checksumType64"] : v["checksumType"]));
        if (v["url"].Length > 0) info.Installers.Add(Make(v["url64bit"].Length > 0 ? "x86" : "neutral", v["url"], v["checksum"], v["checksumType"]));

        if (info.Installers.Count == 0)
        {
            // Installer or application shipped inside the package.
            var tools = Path.Combine(ex, "tools");
            var files = Directory.Exists(tools)
                ? Directory.GetFiles(tools, "*.*", SearchOption.AllDirectories)
                    .Where(f => Path.GetExtension(f).ToLowerInvariant() is ".exe" or ".msi" or ".msix" or ".zip").ToList()
                : new List<string>();
            foreach (var f in files)
            {
                var name = Path.GetFileName(f).ToLowerInvariant();
                var arch = Regex.IsMatch(name, @"64|amd64") ? "x64" : Regex.IsMatch(name, @"32|x86|i386") ? "x86" : "neutral";
                var o = Make(arch, "", "", "");
                o.Properties.Remove("InstallerUrl");
                o.Properties["EmbeddedFile"] = Path.GetRelativePath(ex, f).Replace('\\', '/');
                var type = Silent.Normalize(v["fileType"], f);
                o.Properties["InstallerType"] = type;
                o.Properties[InstallerOption.KindKey] = scripts.Count == 0 || type == "zip" ? "portable" : "installer";
                info.Installers.Add(o);
            }
            if (scripts.Count == 0 && files.Count > 0 || files.Count == 0 && Directory.Exists(tools) && Directory.EnumerateFileSystemEntries(tools).Any())
            {
                var o = Make("neutral", "", "", "");
                o.Properties.Remove("InstallerUrl");
                o.Properties["EmbeddedFile"] = "tools/*";
                o.Properties["InstallerType"] = "zip";
                o.Properties[InstallerOption.KindKey] = "portable";
                info.Installers.Add(o);
            }
        }
    }
}
