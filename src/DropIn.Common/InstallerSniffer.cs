using System.Text;

namespace DropIn.Common;

/// <summary>Recognizes the setup framework of an executable so the right silent and target-directory switches can be used.</summary>
public static class InstallerSniffer
{
    /// <summary>Only the loader stub at the start of the file is scanned; the markers live there.</summary>
    const int ScanBytes = 8 * 1024 * 1024;

    static readonly (string Type, byte[] Marker)[] Markers =
    {
        ("inno", Encoding.ASCII.GetBytes("InnoSetupLdrWindow")),
        ("inno", Encoding.ASCII.GetBytes("JR.Inno.Setup")),
        ("inno", Encoding.ASCII.GetBytes("Inno Setup Setup Data")),
        ("nsis", Encoding.ASCII.GetBytes("Nullsoft Install System")),
        ("nsis", Encoding.ASCII.GetBytes("NullsoftInst")),
        ("burn", Encoding.ASCII.GetBytes(".wixburn")),
    };

    /// <summary>Returns "inno", "nsis" or "burn" when the executable is recognized, otherwise null.</summary>
    public static string? Detect(string file)
    {
        try
        {
            using var stream = File.OpenRead(file);
            var buffer = new byte[(int)Math.Min(stream.Length, ScanBytes)];
            var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            var span = buffer.AsSpan(0, read);
            foreach (var (type, marker) in Markers)
                if (span.IndexOf(marker) >= 0) return type;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return null;
    }
}
