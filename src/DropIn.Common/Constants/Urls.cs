namespace DropIn.Common;

/// <summary>URLs used to retrieve package metadata and official package-manager clients.</summary>
internal static class CommonUrls
{
    /// <summary>Raw-content base URL for the UniGetUI metadata files.</summary>
    internal const string UniGetUiRawBase = "https://raw.githubusercontent.com/Devolutions/UniGetUI/main/WebBasedData/";
    /// <summary>GitHub API endpoint for the UniGetUI icon mirror.</summary>
    internal const string UniGetUiIconsApi = "https://api.github.com/repos/Devolutions/UniGetUI/contents/WebBasedData/icons?ref=main";
    /// <summary>Path marker identifying icons in the UniGetUI mirror.</summary>
    internal const string UniGetUiIconsPathMarker = "/WebBasedData/icons/";
    /// <summary>Official Chocolatey package endpoint.</summary>
    internal const string ChocolateyPackage = "https://community.chocolatey.org/api/v2/package/chocolatey";
    /// <summary>GitHub endpoint for the latest WinGet release metadata.</summary>
    internal const string WingetReleaseApi = "https://api.github.com/repos/microsoft/winget-cli/releases/latest";
}
