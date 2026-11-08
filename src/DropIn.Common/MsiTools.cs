using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace DropIn.Common;

/// <summary>Thin wrapper over the Windows Installer API (msi.dll) used to inspect MSI packages and registrations.</summary>
[SupportedOSPlatform("windows")]
static class MsiTools
{
    /// <summary>Identity properties stored in an MSI package.</summary>
    internal sealed record Package(string ProductCode, string UpgradeCode, string ProductName, string ProductVersion);

    /// <summary>INSTALLSTATE_DEFAULT: the product is installed.</summary>
    const int InstalledState = 5;
    /// <summary>ERROR_SUCCESS.</summary>
    const uint Success = 0;
    /// <summary>Length of a GUID string with braces plus the terminating null.</summary>
    const int GuidLength = 39;

    [DllImport("msi.dll", EntryPoint = "MsiOpenDatabaseW", CharSet = CharSet.Unicode)]
    static extern uint MsiOpenDatabase(string path, IntPtr persist, out IntPtr database);
    [DllImport("msi.dll", EntryPoint = "MsiDatabaseOpenViewW", CharSet = CharSet.Unicode)]
    static extern uint MsiDatabaseOpenView(IntPtr database, string query, out IntPtr view);
    [DllImport("msi.dll")]
    static extern uint MsiViewExecute(IntPtr view, IntPtr record);
    [DllImport("msi.dll")]
    static extern uint MsiViewFetch(IntPtr view, out IntPtr record);
    [DllImport("msi.dll", EntryPoint = "MsiRecordGetStringW", CharSet = CharSet.Unicode)]
    static extern uint MsiRecordGetString(IntPtr record, uint field, StringBuilder value, ref uint size);
    [DllImport("msi.dll")]
    static extern uint MsiCloseHandle(IntPtr handle);
    [DllImport("msi.dll", EntryPoint = "MsiEnumRelatedProductsW", CharSet = CharSet.Unicode)]
    static extern uint MsiEnumRelatedProducts(string upgradeCode, uint reserved, uint index, StringBuilder product);
    [DllImport("msi.dll", EntryPoint = "MsiGetProductInfoW", CharSet = CharSet.Unicode)]
    static extern uint MsiGetProductInfo(string product, string property, StringBuilder value, ref uint size);
    [DllImport("msi.dll", EntryPoint = "MsiQueryProductStateW", CharSet = CharSet.Unicode)]
    static extern int MsiQueryProductState(string product);
    [DllImport("msi.dll", EntryPoint = "MsiSourceListClearAllW", CharSet = CharSet.Unicode)]
    static extern uint MsiSourceListClearAll(string product, string? userName, uint reserved);

    /// <summary>Reads the identity properties of an MSI file without installing it; null when it cannot be read.</summary>
    /// <param name="file">Path to the .msi file.</param>
    internal static Package? ReadPackage(string file)
    {
        if (MsiOpenDatabase(file, IntPtr.Zero, out var db) != Success) return null;
        try
        {
            var code = Property(db, "ProductCode");
            if (code == null) return null;
            return new Package(code, Property(db, "UpgradeCode") ?? "", Property(db, "ProductName") ?? "", Property(db, "ProductVersion") ?? "");
        }
        finally { MsiCloseHandle(db); }
    }

    /// <summary>Reads one value of the MSI Property table.</summary>
    /// <param name="db">Open database handle.</param>
    /// <param name="name">Property name.</param>
    static string? Property(IntPtr db, string name)
    {
        if (MsiDatabaseOpenView(db, $"SELECT `Value` FROM `Property` WHERE `Property`='{name}'", out var view) != Success) return null;
        try
        {
            if (MsiViewExecute(view, IntPtr.Zero) != Success || MsiViewFetch(view, out var record) != Success) return null;
            try
            {
                uint size = 0;
                // The first call with an empty buffer reports the required length.
                MsiRecordGetString(record, 1, new StringBuilder(), ref size);
                var sb = new StringBuilder((int)size + 1);
                size++;
                return MsiRecordGetString(record, 1, sb, ref size) == Success ? sb.ToString() : null;
            }
            finally { MsiCloseHandle(record); }
        }
        finally { MsiCloseHandle(view); }
    }

    /// <summary>Lists installed products that share the upgrade code (every installed version of the same product family).</summary>
    /// <param name="upgradeCode">UpgradeCode of the package being installed.</param>
    internal static List<(string ProductCode, string Version)> InstalledRelated(string upgradeCode)
    {
        var list = new List<(string, string)>();
        if (string.IsNullOrWhiteSpace(upgradeCode)) return list;
        for (uint i = 0; ; i++)
        {
            var product = new StringBuilder(GuidLength);
            if (MsiEnumRelatedProducts(upgradeCode, 0, i, product) != Success) break;
            var version = new StringBuilder(64);
            uint size = 64;
            MsiGetProductInfo(product.ToString(), "VersionString", version, ref size);
            list.Add((product.ToString(), version.ToString()));
        }
        return list;
    }

    /// <summary>Whether the product is currently installed for this user or the machine.</summary>
    /// <param name="productCode">ProductCode GUID.</param>
    internal static bool IsInstalled(string productCode) => MsiQueryProductState(productCode) == InstalledState;

    /// <summary>Removes the registered installer sources (such as a deleted temp path) so Windows Installer never asks for the old installer again.</summary>
    /// <param name="productCode">ProductCode GUID.</param>
    /// <returns>The Windows error code; 0 on success.</returns>
    internal static uint ClearSources(string productCode) => MsiSourceListClearAll(productCode, null, 0);
}