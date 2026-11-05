namespace DropIn.Common;

/// <summary>Checks up front whether the folders a program writes to are writable, so administrator rights can be requested before work starts.</summary>
public static class WriteAccess
{
    /// <summary>Tests real write access by creating and deleting a temporary file in the folder, or in its nearest existing parent when the folder does not exist yet.</summary>
    /// <param name="directory">Folder that will be written to.</param>
    public static bool IsWritable(string directory)
    {
        try
        {
            var dir = Path.GetFullPath(directory);
            while (!Directory.Exists(dir))
                dir = Path.GetDirectoryName(dir) ?? throw new DirectoryNotFoundException(directory);
            var probe = Path.Combine(dir, ".dropin-write-" + Guid.NewGuid().ToString("N"));
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            return false;
        }
    }

    /// <summary>Returns the folders from <paramref name="directories"/> that cannot be written to.</summary>
    /// <param name="directories">Folders to test; duplicates are reported once.</param>
    public static List<string> Denied(IEnumerable<string> directories)
        => directories.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).Where(d => !IsWritable(d)).ToList();
}