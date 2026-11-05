using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace DropIn.IconHandler;

/// <summary>COM persistence interface implemented by a Windows shell icon handler.</summary>
[ComVisible(true)]
[Guid("0000010B-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IPersistFile
{
    /// <summary>Returns the COM class identifier for the handler.</summary>
    void GetClassID(out Guid classId);
    /// <summary>Reports whether the loaded file has been modified.</summary>
    [PreserveSig] int IsDirty();
    /// <summary>Loads the file whose embedded icon should be extracted.</summary>
    void Load([MarshalAs(UnmanagedType.LPWStr)] string fileName, uint mode);
    /// <summary>Rejects saving because this handler is read-only.</summary>
    void Save([MarshalAs(UnmanagedType.LPWStr)] string fileName, [MarshalAs(UnmanagedType.Bool)] bool remember);
    /// <summary>Rejects save completion because this handler is read-only.</summary>
    void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string fileName);
    /// <summary>Returns the currently loaded file path.</summary>
    void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string fileName);
}

/// <summary>COM shell interface for locating and extracting Unicode icons.</summary>
[ComVisible(true)]
[Guid("000214FA-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IExtractIconW
{
    /// <summary>Provides the package path and flags that describe how the shell should cache its icon.</summary>
    [PreserveSig]
    int GetIconLocation(uint flags, [MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconFile,
        uint iconFileLength, out int iconIndex, out uint resultFlags);

    /// <summary>Extracts large and small icon handles from the package.</summary>
    [PreserveSig]
    int Extract([MarshalAs(UnmanagedType.LPWStr)] string iconFile, uint iconIndex,
        out IntPtr largeIcon, out IntPtr smallIcon, uint iconSize);
}

/// <summary>Extracts each .app package's embedded icon for Windows Explorer without creating cache files.</summary>
[ComVisible(true)]
[Guid("6A282523-BF7F-4F91-A7E5-8C945C57A801")]
[ClassInterface(ClassInterfaceType.None)]
public sealed class PackageIconHandler : IExtractIconW, IPersistFile
{
    /// <summary>HRESULT indicating that the COM operation succeeded.</summary>
    const int S_OK = 0;
    /// <summary>HRESULT indicating that the file is unchanged.</summary>
    const int S_FALSE = 1;
    /// <summary>HRESULT returned when the icon cannot be extracted.</summary>
    const int E_FAIL = unchecked((int)0x80004005);
    /// <summary>Shell flag requesting an icon specific to each file instance.</summary>
    const uint GIL_PERINSTANCE = 0x00000002;
    /// <summary>Shell flag indicating that the icon location is not an executable file.</summary>
    const uint GIL_NOTFILENAME = 0x00000008;
    /// <summary>Shell flag requesting that the extracted icon not be cached.</summary>
    const uint GIL_DONTCACHE = 0x00000010;
    /// <summary>Manifest entry inside the app package.</summary>
    const string ManifestName = "manifest.json";

    /// <summary>Absolute path of the package currently loaded by the shell.</summary>
    string? _fileName;

    /// <summary>Returns this handler class identifier to COM.</summary>
    public void GetClassID(out Guid classId) => classId = new Guid("6A282523-BF7F-4F91-A7E5-8C945C57A801");
    /// <summary>Reports that the read-only package has no pending changes.</summary>
    public int IsDirty() => S_FALSE;
    /// <summary>Stores the normalized full path of the package to inspect.</summary>
    public void Load(string fileName, uint mode) => _fileName = Path.GetFullPath(fileName);
    /// <summary>Rejects saving because package files are not modified by the handler.</summary>
    public void Save(string fileName, bool remember) => Marshal.ThrowExceptionForHR(unchecked((int)0x80030001));
    /// <summary>Rejects save completion because package files are not modified by the handler.</summary>
    public void SaveCompleted(string fileName) => Marshal.ThrowExceptionForHR(unchecked((int)0x80030001));
    /// <summary>Returns the loaded path, or an empty string before a file is loaded.</summary>
    public void GetCurFile(out string fileName) => fileName = _fileName ?? "";

    /// <summary>Supplies the package path and the no-cache flags used by the shell.</summary>
    public int GetIconLocation(uint flags, StringBuilder iconFile, uint iconFileLength, out int iconIndex, out uint resultFlags)
    {
        iconIndex = 0;
        resultFlags = GIL_PERINSTANCE | GIL_DONTCACHE | GIL_NOTFILENAME;
        if (_fileName == null || iconFileLength <= _fileName.Length) return E_FAIL;
        iconFile.Clear().Append(_fileName);
        return S_OK;
    }

    /// <summary>Reads the embedded icon image and creates appropriately sized native icon handles.</summary>
    public int Extract(string iconFile, uint iconIndex, out IntPtr largeIcon, out IntPtr smallIcon, uint iconSize)
    {
        largeIcon = IntPtr.Zero;
        smallIcon = IntPtr.Zero;
        try
        {
            var file = _fileName ?? iconFile;
            using var zip = ZipFile.OpenRead(file);
            // Read the icon entry named by the package manifest without extracting files to disk.
            var manifestEntry = zip.GetEntry(ManifestName);
            if (manifestEntry == null) return E_FAIL;
            using var reader = new StreamReader(manifestEntry.Open());
            var manifest = JsonSerializer.Deserialize<IconManifest>(reader.ReadToEnd());
            if (manifest == null || string.IsNullOrEmpty(manifest.IconFile)) return E_FAIL;
            var iconEntry = zip.GetEntry(manifest.IconFile);
            if (iconEntry == null || iconEntry.Length is 0 or > 16 * 1024 * 1024) return E_FAIL;

            using var source = iconEntry.Open();
            using var image = Image.FromStream(source);
            var largeSize = Math.Clamp((int)(iconSize & 0xFFFF), 16, 256);
            var smallSize = Math.Clamp((int)(iconSize >> 16), 16, 256);
            // The shell owns the returned handles and releases them after using the icons.
            largeIcon = CreateIcon(image, largeSize);
            smallIcon = CreateIcon(image, smallSize);
            return S_OK;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException
                                   or JsonException or ArgumentException or ExternalException or OutOfMemoryException)
        {
            if (largeIcon != IntPtr.Zero) { DestroyIcon(largeIcon); largeIcon = IntPtr.Zero; }
            if (smallIcon != IntPtr.Zero) { DestroyIcon(smallIcon); smallIcon = IntPtr.Zero; }
            return E_FAIL;
        }
    }

    /// <summary>Resizes an image to a transparent square bitmap and returns a native icon handle.</summary>
    static IntPtr CreateIcon(Image image, int size)
    {
        using var bitmap = new Bitmap(size, size);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(image, 0, 0, size, size);
        }
        return bitmap.GetHicon();
    }

    /// <summary>Releases a native icon handle created by the Windows graphics API.</summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool DestroyIcon(IntPtr icon);

    /// <summary>Minimal manifest model containing the embedded icon path.</summary>
    sealed class IconManifest
    {
        /// <summary>Relative package entry name of the embedded icon image.</summary>
        public string IconFile { get; set; } = "";
    }
}
