namespace DropIn.Common;

/// <summary>Explains which registry entries and caches an installer type leaves on the machine.</summary>
public static class InstallTraces
{
    /// <summary>Predicts the installer type of an option from its manifest fields and returns the matching warning, or null when it leaves no traces.</summary>
    public static string? Predict(InstallerOption option, out string type)
    {
        var hint = option.FileHint;
        type = Silent.Normalize(option.Get("InstallerType"), hint);
        var portable = option.Get(InstallerOption.KindKey) == "portable";
        return Describe(type, portable);
    }

    /// <summary>Returns a warning that names the registry entries and caches left behind and the risk they pose , or null for portable payloads.</summary>
    /// <param name="type">Normalized installer type (msi, msix, burn, inno, nsis, exe, zip, portable).</param>
    /// <param name="portable">Whether the payload is installed as a portable application.</param>
    public static string? Describe(string type, bool portable)
    {
        if (portable || type is "zip" or "portable") return null;
        return type switch
        {
            "msi" => "This is an MSI installer. Installing it leaves traces outside the DropIn install folder:\n\n" +
                     "- Registry: a product registration and an uninstall entry (HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall and Installer\\Products), including the path of the installer file used as its source.\n" +
                     "- Cache: a copy of the MSI in C:\\Windows\\Installer (can use tens to hundreds of MB).\n\n" +
                     "Risk: if the registered source is missing or differs, later updates, repairs or uninstalls can fail with errors such as \"the feature you are trying to use is on a network resource that is unavailable\", and a leftover registration may need Microsoft's Program Install and Uninstall troubleshooter or the Windows Installer CleanUp Utility.\n\n" +
                     "DropIn clears the source path after installing and can remove older versions, but the registration and cache stay until the program is uninstalled.",
            "msix" => "This is an MSIX/APPX package. Installing it leaves traces outside the DropIn install folder:\n\n" +
                      "- Registry/package store: it is registered for the user and installed under C:\\Program Files\\WindowsApps, not in the DropIn install folder.\n" +
                      "- Data: per-user data stays in %LocalAppData%\\Packages until the app is removed.\n\n" +
                      "Risk: deleting the DropIn install folder does not remove it; it stays registered until removed from Settings > Apps.",
            "burn" => "This is a WiX Burn bootstrapper. Installing it leaves traces outside the DropIn install folder:\n\n" +
                      "- Registry: an uninstall entry and the bundle registration, often with chained MSI products.\n" +
                      "- Cache: C:\\ProgramData\\Package Cache holds the bundle and its packages.\n\n" +
                      "Risk: updating or repairing can ask for the original installer, and shared components stay on the machine after the program folder is deleted.",
            _ => "This is an installer program (" + type + "). Installing it usually leaves traces outside the DropIn install folder:\n\n" +
                 "- Registry: an uninstall entry and application settings (HKLM/HKCU\\SOFTWARE, Uninstall).\n" +
                 "- Files: shared components, drivers, services or shortcuts outside the install folder.\n\n" +
                 "Risk: deleting the install folder leaves these behind; only the program's own uninstaller removes them, and a broken registration can block reinstalling or updating."
        };
    }
}