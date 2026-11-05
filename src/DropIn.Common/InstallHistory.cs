using System.Text.Json;
using System.Text.Json.Nodes;

namespace DropIn.Common;

/// <summary>The install history kept as state (not a setting) in <c>.dropin\installed.json</c> below the install directory.</summary>
public static class InstallHistory
{
    /// <summary>The state directory name below the install directory.</summary>
    public const string StateDirectoryName = ".dropin";
    const string FileName = "installed.json";
    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    static string PathFor(string installDirectory) => Path.Combine(installDirectory, StateDirectoryName, FileName);

    /// <summary>Reads the history; a missing or invalid file yields an empty list.</summary>
    public static List<InstalledPackage> Load(string installDirectory)
    {
        try { return JsonSerializer.Deserialize<List<InstalledPackage>>(File.ReadAllText(PathFor(installDirectory))) ?? new(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }

    /// <summary>Adds or replaces the entry for a package folder; entries for the same package whose folder is gone are dropped.</summary>
    internal static void Record(string installDirectory, InstalledPackage entry)
    {
        var list = Load(installDirectory);
        list.RemoveAll(p => p.Name.Equals(entry.Name, StringComparison.OrdinalIgnoreCase)
            && (p.Directory.Equals(entry.Directory, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(p.Directory)));
        list.Add(entry);
        Write(installDirectory, list);
    }

    /// <summary>Copies a history that older versions stored in the config file, unless a history file already exists.</summary>
    internal static void Import(string installDirectory, JsonNode legacy)
    {
        try
        {
            if (File.Exists(PathFor(installDirectory))) return;
            var list = legacy.Deserialize<List<InstalledPackage>>() ?? new();
            if (list.Count > 0) Write(installDirectory, list);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            AppLog.Error("Could not import the install history: " + ex.Message);
        }
    }

    static void Write(string installDirectory, List<InstalledPackage> list)
    {
        var path = PathFor(installDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(list, Options));
        File.Move(temp, path, true);
    }
}