using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace DropIn.Common;

/// <summary>Checksum, URL filename, and version utilities.</summary>
public static partial class Net
{
    /// <summary>Throws if the file hash does not match. Unknown algorithms or empty hashes are skipped.</summary>

    public static void VerifyHash(string path, string? expected, string algorithm, IProgress<string>? log = null)
    {
        if (string.IsNullOrWhiteSpace(expected)) return;
        HashAlgorithm? alg = algorithm.Trim().ToLowerInvariant() switch
        {
            "" or "sha256" => SHA256.Create(),
            "sha1" => SHA1.Create(),
            "sha512" => SHA512.Create(),
            "md5" => MD5.Create(),
            _ => null
        };
        if (alg == null) return;
        using (alg)
        using (var fs = File.OpenRead(path))
        {
            var actual = Convert.ToHexString(alg.ComputeHash(fs));
            if (!actual.Equals(expected.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"{algorithm} mismatch for {Path.GetFileName(path)}: expected {expected}, got {actual}");
        }
        log?.Report("Checksum verified.");
    }

    /// <summary>Returns a sanitized file name from a download URL.</summary>
    public static string FileNameFromUrl(string url)
    {
        var name = Uri.UnescapeDataString(Path.GetFileName(new Uri(url).AbsolutePath));
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }

    /// <summary>Extracts a numeric version from a version string for ordering.</summary>
    public static Version ParseVersion(string v)
    {
        var m = Regex.Match(v, @"\d+(\.\d+){0,3}");
        return m.Success && Version.TryParse(m.Value.Contains('.') ? m.Value : m.Value + ".0", out var r) ? r : new Version(0, 0);
    }
}
