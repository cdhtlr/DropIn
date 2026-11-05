using System.Text.Json;

namespace DropIn.Common;

/// <summary>Configuration stored in dropin.config.json next to the executables, split into Installer and Packager sections.</summary>
public sealed partial class DropInConfig
{
    /// <summary>Name of the JSON file used to persist application settings.</summary>
    public const string FileName = CommonFileNames.DropInConfig;

    /// <summary>Options that only the installer and the watcher service use.</summary>
    public InstallerSettings Installer { get; set; } = new();

    /// <summary>Options that only the packager uses.</summary>
    public PackagerSettings Packager { get; set; } = new();
}