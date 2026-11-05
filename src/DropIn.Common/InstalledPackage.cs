namespace DropIn.Common;

public sealed class InstalledPackage
{
    /// <summary>Installed package name used to identify its history entry.</summary>
    public string Name { get; set; } = "";
    /// <summary>Installed package version.</summary>
    public string Version { get; set; } = "";
    /// <summary>Package source from which the package was obtained.</summary>
    public string Source { get; set; } = "";
    /// <summary>Directory containing the installed package.</summary>
    public string Directory { get; set; } = "";
    /// <summary>Local timestamp when the installation completed.</summary>
    public DateTime InstalledAt { get; set; }
}
