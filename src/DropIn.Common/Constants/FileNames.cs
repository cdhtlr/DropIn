namespace DropIn.Common;

/// <summary>File and directory names shared by DropIn components.</summary>
internal static class CommonFileNames
{
    /// <summary>Name of the DropIn configuration file.</summary>
    internal const string DropInConfig = "dropin.config.json";
    /// <summary>Directory containing package-manager database files.</summary>
    internal const string DatabaseDirectory = "Database";
    /// <summary>Name of the current flat UniGetUI metadata database.</summary>
    internal const string UniGetUiV2Database = "screenshot-database-v2.json";
    /// <summary>Name of the legacy UniGetUI metadata database.</summary>
    internal const string UniGetUiV1Database = "screenshot-database.json";
    /// <summary>Name of the user-maintained metadata overlay.</summary>
    internal const string CustomDatabase = "custom-database.json";
    /// <summary>Legacy application-data location segment used during cache migration.</summary>
    internal const string LegacyDatabaseDirectory = "UniGetUI";
    /// <summary>Mirrored icon subdirectory name.</summary>
    internal const string IconsDirectory = "Icons";
    /// <summary>Directory name for locally provisioned package-manager clients.</summary>
    internal const string PortableClientDirectory = "PortableClient";
    /// <summary>Chocolatey package-manager client directory name.</summary>
    internal const string ChocolateyDirectory = "Chocolatey";
    /// <summary>WinGet package-manager client directory name.</summary>
    internal const string WingetDirectory = "WinGet";
    /// <summary>Expected WinGet App Installer bundle asset name.</summary>
    internal const string WingetBundle = "Microsoft.DesktopAppInstaller_8wekyb3d8bbwe.msixbundle";
    /// <summary>Expected WinGet App Installer dependencies archive.</summary>
    internal const string WingetDependencies = "DesktopAppInstaller_Dependencies.zip";
}
