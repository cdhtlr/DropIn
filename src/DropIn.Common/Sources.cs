namespace DropIn.Common;

/// <summary>Detects package-manager executables and searches the process PATH.</summary>
public static class Sources
{
    /// <summary>"winget" or "chocolatey" if the command is on PATH (winget wins), otherwise null.</summary>
    public static string? DetectInstalled()
    {
        if (FindExecutable("winget") != null) return "winget";
        if (FindExecutable("choco") != null) return "chocolatey";
        return null;
    }

    /// <summary>Returns the first matching executable found on PATH, or null.</summary>
    public static string? FindExecutable(string command)
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var path = Path.Combine(dir.Trim('"'), command + (OperatingSystem.IsWindows() ? ".exe" : ""));
                if (File.Exists(path)) return path;
            }
            catch (ArgumentException) { }
        }
        return null;
    }
}
