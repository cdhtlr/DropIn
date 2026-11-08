namespace DropIn.Common;

/// <summary>Knowledge about installer types and common silent switches.</summary>
public static class Silent
{
    /// <summary>Installer type assumed when none is declared (UnknownInstallerType in the configuration).</summary>
    public static string DefaultType { get; set; } = "zip";

    /// <summary>Maps winget/chocolatey installer type names (or file extensions) to a canonical type.</summary>
    public static string Normalize(string? type, string? fileNameOrUrl = null)
    {
        var t = (type ?? "").Trim().ToLowerInvariant().TrimStart('.');
        if (t is "msi" or "wix") return "msi";
        if (t is "msix" or "appx" or "msixbundle" or "appxbundle") return "msix";
        if (t is "inno" or "nullsoft" or "nsis" or "burn" or "exe" or "zip" or "portable") return t == "nullsoft" ? "nsis" : t;
        var ext = Path.GetExtension((fileNameOrUrl ?? "").Split('?')[0]).TrimStart('.').ToLowerInvariant();
        return ext switch
        {
            "msi" => "msi",
            "msix" or "appx" or "msixbundle" or "appxbundle" => "msix",
            "zip" => "zip",
            "exe" => "exe",
            "" => DefaultType, // default when the type cannot be determined
            _ => ext
        };
    }

    /// <summary>Default silent switch for a known installer type (null for unknown).</summary>
    public static string? DefaultSwitch(string type) => type switch
    {
        "inno" => "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-",
        "nsis" => "/S",
        "burn" => "/quiet /norestart",
        "msi" => "/qn /norestart",
        _ => null
    };

    /// <summary>Candidate silent switches to try, in order, for a generic executable.</summary>
    public static readonly string[] ExeCandidates =
    {
        "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-",
        "/S",
        "/quiet /norestart",
        "/silent",
        "/s /v\"/qn\"",
        "-silent",
    };

    /// <summary>Switch that selects the target directory, or null if the type has no standard one.</summary>
    public static string? DirSwitch(string type, string dir) => type switch
    {
        "inno" => $"/DIR=\"{dir}\"",
        "nsis" => $"/D={dir}",   // must be last argument, unquoted
        "msi" => $"INSTALLDIR=\"{dir}\"",
        _ => null
    };
}
