namespace DropIn.Common;

/// <summary>One installable variant of a package. <see cref="Properties"/> holds every manifest field (flattened, e.g. "Switches.Silent").</summary>
public sealed class InstallerOption
{
    /// <summary>Property holding "installer" or "portable"; set by the clients.</summary>
    public const string KindKey = "DropIn.Kind";

    /// <summary>Contains manifest fields flattened into dotted keys such as Switches.Silent.</summary>
    public Dictionary<string, string> Properties { get; } = new();

    /// <summary>Returns a property value, or an empty string when the key is absent.</summary>
    public string Get(string key) => Properties.TryGetValue(key, out var v) ? v : "";

    /// <summary>Human-readable label assembled from the installer option fields.</summary>
    public string Label
    {
        get
        {
            var parts = new[] { Get("Architecture"), Get("InstallerType"), Get(KindKey), Get("Scope"), Get("InstallerLocale"), FileHint }
                .Where(s => s.Length > 0);
            return string.Join(" | ", parts);
        }
    }

    /// <summary>File name inferred from the installer URL or embedded payload path.</summary>
    public string FileHint
    {
        get
        {
            var src = Get("InstallerUrl") is { Length: > 0 } u ? u : Get("Url") is { Length: > 0 } u2 ? u2 : Get("EmbeddedFile");
            try { return Path.GetFileName(new Uri(src, UriKind.RelativeOrAbsolute).OriginalString.Split('?')[0]); }
            catch (UriFormatException) { return src; }
        }
    }

    /// <summary>Returns the display label for this installer option.</summary>
    public override string ToString() => Label;
}

/// <summary>Everything known about a package version in its source repository.</summary>
public sealed class ManifestInfo : IDisposable
{
    /// <summary>Package repository or package-manager source identifier.</summary>
    public string Source { get; set; } = "";
    /// <summary>Stable package identifier within the source.</summary>
    public string Id { get; set; } = "";
    /// <summary>Selected package version.</summary>
    public string Version { get; set; } = "";
    /// <summary>Package-level metadata (publisher, description, license ...).</summary>
    public Dictionary<string, string> Metadata { get; } = new();
    /// <summary>Installer variants available for this package version.</summary>
    public List<InstallerOption> Installers { get; } = new();
    /// <summary>Extracted Chocolatey package (null for winget).</summary>
    public string? WorkDir { get; set; }

    /// <summary>Deletes any extracted package working directory owned by this manifest.</summary>
    public void Dispose()
    {
        if (WorkDir == null) return;
        try { Directory.Delete(WorkDir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        WorkDir = null;
    }
}
