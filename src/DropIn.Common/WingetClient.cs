using System.Text.Json;
using YamlDotNet.Serialization;

namespace DropIn.Common;

/// <summary>Reads manifests from the microsoft/winget-pkgs repository.</summary>
public static class WingetClient
{
    /// <summary>GitHub API base URL for browsing WinGet manifest directories.</summary>
    const string Api = "https://api.github.com/repos/microsoft/winget-pkgs/contents/";
    /// <summary>Raw-content base URL used to download WinGet manifests.</summary>
    const string Raw = "https://raw.githubusercontent.com/microsoft/winget-pkgs/master/";

    /// <summary>Builds the repository path for a package identifier.</summary>
    static string PathFor(string id) => $"manifests/{char.ToLowerInvariant(id[0])}/{id.Replace('.', '/')}";

    /// <summary>Available versions, newest first.</summary>
    public static async Task<List<string>> GetVersionsAsync(string id, CancellationToken ct = default)
    {
        try
        {
            using var doc = JsonDocument.Parse(await Net.GetStringAsync(Api + PathFor(id), ct));
            return doc.RootElement.EnumerateArray()
                .Where(e => e.GetProperty("type").GetString() == "dir")
                .Select(e => e.GetProperty("name").GetString()!)
                .OrderByDescending(Net.ParseVersion).ToList();
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException($"Winget package '{id}' not found (use the exact package identifier, e.g. Publisher.Package).");
        }
    }

    /// <summary>Downloads a package version manifest and converts it into installer options and metadata.</summary>
    public static async Task<ManifestInfo> LoadAsync(string id, string version, CancellationToken ct = default)
    {
        var raw = $"{Raw}{PathFor(id)}/{version}/{id}";
        string installerYaml, versionYaml = "";
        try
        {
            installerYaml = await Net.GetStringAsync(raw + ".installer.yaml", ct);
            try { versionYaml = await Net.GetStringAsync(raw + ".yaml", ct); } catch (HttpRequestException) { }
        }
        catch (HttpRequestException)
        {
            installerYaml = await Net.GetStringAsync(raw + ".yaml", ct); // singleton manifest
        }

        var info = new ManifestInfo { Source = "winget", Id = id, Version = version };
        var root = ParseMap(installerYaml);
        var defaults = new Dictionary<string, string>();
        Flatten("", root.Where(kv => (string)kv.Key != "Installers").ToDictionary(kv => (string)kv.Key, kv => kv.Value), defaults);

        var entries = root.TryGetValue("Installers", out var l) && l is List<object> list ? list : new List<object>();
        foreach (var e in entries.OfType<Dictionary<object, object>>())
        {
            var opt = new InstallerOption();
            foreach (var kv in defaults) opt.Properties[kv.Key] = kv.Value;
            var own = new Dictionary<string, string>();
            Flatten("", e.ToDictionary(kv => (string)kv.Key, kv => kv.Value), own);
            foreach (var kv in own) opt.Properties[kv.Key] = kv.Value;
            var type = Silent.Normalize(opt.Get("InstallerType"), opt.Get("InstallerUrl"));
            opt.Properties[InstallerOption.KindKey] = type is "portable" or "zip" ? "portable" : "installer";
            info.Installers.Add(opt);
        }
        if (info.Installers.Count == 0) throw new InvalidOperationException("No installers in winget manifest");

        // Metadata from the version and default-locale manifests (display name, publisher, license ...).
        string? locale = null;
        if (versionYaml.Length > 0)
        {
            var m = new Dictionary<string, string>();
            Flatten("", ParseMap(versionYaml).ToDictionary(kv => (string)kv.Key, kv => kv.Value), m);
            foreach (var kv in m) info.Metadata[kv.Key] = kv.Value;
            m.TryGetValue("DefaultLocale", out locale);
        }
        if (locale != null)
        {
            try
            {
                var m = new Dictionary<string, string>();
                Flatten("", ParseMap(await Net.GetStringAsync($"{raw}.locale.{locale}.yaml", ct))
                    .ToDictionary(kv => (string)kv.Key, kv => kv.Value), m);
                foreach (var kv in m) info.Metadata.TryAdd(kv.Key, kv.Value);
            }
            catch (HttpRequestException) { }
        }
        return info;
    }

    /// <summary>Deserializes a YAML manifest into a mapping.</summary>
    static Dictionary<object, object> ParseMap(string yaml)
        => new DeserializerBuilder().Build().Deserialize<Dictionary<object, object>>(yaml) ?? new();

    /// <summary>Flattens nested maps/lists to dotted keys: Switches.Silent, Commands[0], ...</summary>
    static void Flatten(string prefix, object? node, Dictionary<string, string> into)
    {
        switch (node)
        {
            case IDictionary<string, object> d:
                foreach (var kv in d) Flatten(prefix.Length == 0 ? kv.Key : prefix + "." + kv.Key, kv.Value, into);
                break;
            case IDictionary<object, object> d:
                foreach (var kv in d) Flatten(prefix.Length == 0 ? kv.Key.ToString()! : prefix + "." + kv.Key, kv.Value, into);
                break;
            case List<object> l:
                for (var i = 0; i < l.Count; i++) Flatten($"{prefix}[{i}]", l[i], into);
                break;
            case null:
                if (prefix.Length > 0) into[prefix] = "";
                break;
            default:
                into[prefix] = node.ToString() ?? "";
                break;
        }
    }
}
