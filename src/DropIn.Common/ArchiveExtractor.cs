using System.IO.Compression;

namespace DropIn.Common;

/// <summary>Extracts ZIP archives safely while reporting byte-based progress.</summary>
internal static class ArchiveExtractor
{
    /// <summary>Extracts every entry, rejecting paths that would escape the destination directory.</summary>
    /// <param name="zipPath">Archive to extract.</param>
    /// <param name="dir">Destination directory.</param>
    /// <param name="report">Receives the completed fraction (0 to 1) after each entry.</param>
    /// <param name="write">Optional wrapper that performs each file write (used for overwrite handling); receives the target path and the write action.</param>
    public static void Extract(string zipPath, string dir, Action<double>? report = null, Action<string, Action>? write = null)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var root = Path.GetFullPath(dir) + Path.DirectorySeparatorChar;
        var total = Math.Max(1, zip.Entries.Sum(e => e.Length));
        long done = 0;
        foreach (var entry in zip.Entries)
        {
            // Normalize each entry before writing to prevent archive paths from traversing outside the destination.
            var target = Path.GetFullPath(Path.Combine(dir, entry.FullName));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Invalid path in the archive: " + entry.FullName);
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) { Directory.CreateDirectory(target); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            void Write() => entry.ExtractToFile(target, true);
            if (write != null) write(target, Write); else Write();
            done += entry.Length;
            report?.Invoke((double)done / total);
        }
        report?.Invoke(1);
    }
}
