using System.Diagnostics;

namespace DropIn.Common;

/// <summary>Reads the product name and version of a raw installer file.</summary>
static class InstallerMetadata
{
    /// <summary>Returns the product name and version stored in an .msi or .exe, or nulls when unavailable.</summary>
    /// <param name="file">Installer file path.</param>
    internal static (string? Name, string? Version) Read(string file)
    {
        try
        {
            if (file.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) && OperatingSystem.IsWindows() && MsiTools.ReadPackage(file) is { } msi)
                return (msi.ProductName, msi.ProductVersion);
            if (file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                var info = FileVersionInfo.GetVersionInfo(file);
                var version = FirstNonBlank(info.ProductVersion, info.FileVersion);
                // Version text such as "0.81 (build 5)" is cut at the first space to keep folder names short.
                return (FirstNonBlank(info.ProductName), version?.Split(' ')[0]);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { }
        return (null, null);
    }

    /// <summary>Gets the first value that is not null or whitespace.</summary>
    static string? FirstNonBlank(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();
}