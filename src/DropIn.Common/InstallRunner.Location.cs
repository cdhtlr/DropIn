namespace DropIn.Common;

/// <summary>Detection of installers that ignore the requested install directory.</summary>
public static partial class InstallRunner
{
    /// <summary>Takes a snapshot of the registered program locations before an installer runs.</summary>
    static Dictionary<string, string> SnapshotLocations()
        => OperatingSystem.IsWindows() ? UninstallRegistry.Locations() : new Dictionary<string, string>();

    /// <summary>
    /// Returns a warning when a finished installer left the requested directory empty because it installed to a location of its own,
    /// or null when the files are where DropIn asked for them.
    /// </summary>
    /// <param name="dir">Requested install directory.</param>
    /// <param name="before">Snapshot taken before the installer started.</param>
    static string? WrongLocationNote(string dir, Dictionary<string, string> before)
    {
        if (HasFiles(dir)) return null;
        var added = SnapshotLocations().Where(p => !before.ContainsKey(p.Key)).Select(p => p.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var where = added.Count > 0 ? $" It reports: {string.Join("; ", added)}." : " Check its entry in Windows Apps.";
        return $"Note: the installer ignored the requested folder '{dir}' and used a location chosen by the installer itself.{where}";
    }

    /// <summary>Appends the wrong-location warning, when there is one, to a successful result.</summary>
    static InstallResult WithLocationNote(InstallResult result, string dir, Dictionary<string, string> before)
        => result.Success && WrongLocationNote(dir, before) is { } note ? result with { Message = result.Message + "\n" + note } : result;
}
