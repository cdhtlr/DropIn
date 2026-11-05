using System.Text.Json;
using System.Text.Json.Nodes;

namespace DropIn.Common;

/// <summary>Configuration loading, normalization, and persistence operations.</summary>
public sealed partial class DropInConfig
{
    /// <summary>Directory against which relative configuration paths are resolved.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string BaseDirectory { get; private set; } = AppContext.BaseDirectory;

    /// <summary>The most recently loaded configuration, for code that has no config instance at hand.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public static DropInConfig Current { get; private set; } = new();

    /// <summary>JSON formatting options used when saving configuration.</summary>
    static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <summary>Returns the default package installation directory under the application directory.</summary>
    /// <param name="baseDir">Application directory used as the base.</param>
    public static string DefaultInstallDirectory(string baseDir) => Path.Combine(baseDir, CommonDefaults.InstallDirectory);

    /// <summary>Resolves the configured package output directory to an absolute path.</summary>
    public string ResolveOutputDirectory()
        => string.IsNullOrWhiteSpace(Packager.PackageOutputDirectory)
            ? Path.Combine(BaseDirectory, CommonDefaults.OutputDirectory)
            : Path.GetFullPath(Path.Combine(BaseDirectory, Packager.PackageOutputDirectory));

    /// <summary>Compares a configuration value to an expected token without case sensitivity.</summary>
    /// <param name="value">Configured value to check.</param>
    /// <param name="expected">Expected token.</param>
    public bool Is(string value, string expected) => value.Trim().Equals(expected, StringComparison.OrdinalIgnoreCase);

    /// <summary>Pushes the settings that library code reads from static state.</summary>
    void Apply(string baseDir)
    {
        BaseDirectory = baseDir;
        Current = this;
        // Each program logs to its own file; the entry assembly tells which one is running.
        var packager = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name?.Contains("Packager", StringComparison.OrdinalIgnoreCase) == true;
        AppLog.Configure(packager ? Packager.LogFile : Installer.LogFile, packager ? Packager.LogLevel : Installer.LogLevel, baseDir);
        Net.Configure(Packager.ProxyUrl, Packager.UserAgent, Packager.MaxParallelDownloads, Packager.DownloadRetries, Packager.AllowInvalidCertificates);
        Silent.DefaultType = string.IsNullOrWhiteSpace(Packager.UnknownInstallerType) ? CommonDefaults.InstallerType : Packager.UnknownInstallerType.Trim().ToLowerInvariant();
        PackageSearchClient.ResultLimit = Math.Clamp(Packager.SearchResultLimit, 1, 1000);
        // Apply shared service settings after assigning Current so downstream code sees this instance.
        PackageManagerResolver.Configure(Packager.PortableClientDirectory, Packager.PreferPortableClient, baseDir);
        PackageBuilder.Configure(Packager.OutputNamePattern, Packager.OverwriteOutput, Packager.VerifyChecksum, Packager.KeepDownloadedInstallers ? Path.GetFullPath(Path.Combine(baseDir, Packager.DownloadsDirectory)) : null);
    }

    /// <summary>Folders the packager writes to: the program folder (config), the database, the output folder, kept downloads and the portable client when used.</summary>
    /// <param name="outputDirectory">Output folder for the current run.</param>
    public List<string> PackagerWriteDirectories(string outputDirectory)
    {
        var list = new List<string>
        {
            BaseDirectory,
            Path.GetFullPath(Path.Combine(BaseDirectory, Packager.DatabaseDirectory)),
            outputDirectory
        };
        if (Packager.KeepDownloadedInstallers) list.Add(Path.GetFullPath(Path.Combine(BaseDirectory, Packager.DownloadsDirectory)));
        if (Packager.PreferPortableClient) list.Add(Path.GetFullPath(Path.Combine(BaseDirectory, Packager.PortableClientDirectory)));
        return list;
    }

    /// <summary>Adds or replaces the history entry for a package folder in the history file below the install directory.</summary>
    public static void RecordInstall(InstalledPackage entry)
    {
        try { InstallHistory.Record(Current.Installer.InstallDirectory, entry); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            AppLog.Error("Could not record the installation: " + ex.Message);
        }
    }
    /// <summary>Loads the config from <paramref name="baseDir"/>, creating a default one if it is missing or invalid.</summary>
    public static DropInConfig LoadOrCreate(string baseDir)
    {
        var path = Path.Combine(baseDir, FileName);
        DropInConfig? cfg = null;
        JsonNode? legacyHistory = null;
        JsonNode? original = null;
        if (File.Exists(path))
        {
            try
            {
                var text = File.ReadAllText(path);
                original = JsonNode.Parse(text);
                var node = JsonNode.Parse(text) as JsonObject;
                if (node != null)
                {
                    MigrateFlatLayout(node);
                    MigrateMovedKeys(node, out legacyHistory);
                    Flatten(node);
                }
                cfg = node?.Deserialize<DropInConfig>();
            }
            catch (JsonException) { cfg = null; original = null; }
        }
        cfg ??= new DropInConfig();
        cfg.Normalize();
        // Write the file when it differs from the grouped layout: old layouts, options added in a newer version, or blanks filled with defaults.
        var desired = cfg.ToFileJson();
        if (original == null || original.ToJsonString() != JsonNode.Parse(desired)!.ToJsonString())
        {
            try { File.WriteAllText(path, desired); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        cfg.Installer.RawInstallDirectory = cfg.Installer.InstallDirectory;
        if (!Path.IsPathRooted(cfg.Installer.InstallDirectory))
            cfg.Installer.InstallDirectory = Path.GetFullPath(Path.Combine(baseDir, cfg.Installer.InstallDirectory));
        if (legacyHistory != null) InstallHistory.Import(cfg.Installer.InstallDirectory, legacyHistory);
        cfg.Apply(baseDir);
        return cfg;
    }

    /// <summary>Replaces null sections, collections or strings that a hand-edited file may contain.</summary>
    void Normalize()
    {
        Installer ??= new();
        Packager ??= new();
        Installer.Normalize();
        Packager.Normalize();
    }

    /// <summary>
    /// Moves the options of the old flat layout (all keys at the top level) into the Installer and Packager
    /// sections. Returns whether the file used the old layout.
    /// </summary>
    static bool MigrateFlatLayout(JsonObject root)
    {
        var sections = new (string Name, Type Type)[]
        {
            (nameof(Installer), typeof(InstallerSettings)),
            (nameof(Packager), typeof(PackagerSettings)),
        };
        var sectionNames = sections.Select(s => s.Name).ToHashSet();
        // The old Global section is read and removed by MigrateMovedKeys.
        var legacyKeys = root.Select(p => p.Key).Where(k => !sectionNames.Contains(k) && k != "Global").ToList();
        if (legacyKeys.Count == 0) return false;
        foreach (var (name, type) in sections)
        {
            var section = root[name] as JsonObject ?? new JsonObject();
            foreach (var property in type.GetProperties())
            {
                if (!legacyKeys.Contains(property.Name)) continue;
                section[property.Name] = root[property.Name]?.DeepClone();
            }
            root[name] = section;
        }
        // Keys that no section knows (such as the removed SetupInitialized flag) are dropped.
        foreach (var key in legacyKeys) root.Remove(key);
        return true;
    }

    /// <summary>Applies renames and moves between versions: the old Global section (network, database and default-type options go to Packager, the log level to both programs), LastPackages (a list) to LastInput, and Installer.InstalledPackages to the history file.</summary>
    static bool MigrateMovedKeys(JsonObject root, out JsonNode? legacyHistory)
    {
        var changed = false;
        legacyHistory = null;
        if (root["Installer"] is JsonObject installer && installer.Remove("InstalledPackages", out var history))
        {
            legacyHistory = history;
            changed = true;
        }
        if (root["Packager"] is JsonObject packager)
        {
            if (packager.Remove("ExtractIconFromExe")) changed = true;
            if (root["Global"] is JsonObject level && level["LogLevel"] is { } logLevel)
                foreach (var section in new[] { root["Installer"] as JsonObject, packager })
                    if (section != null && !section.ContainsKey("LogLevel")) section["LogLevel"] = logLevel.DeepClone();
            foreach (var key in new[] { "DatabaseDirectory", "DefaultInstallerType", "ProxyUrl", "UserAgent", "MaxParallelDownloads", "DownloadRetries" })
            {
                if (root["Global"] is not JsonObject global || !global.Remove(key, out var moved)) continue;
                if (moved != null && !packager.ContainsKey(key)) packager[key] = moved;
                changed = true;
            }
            if (root.Remove("Global")) changed = true;
            if (packager.Remove("LastPackages", out var last))
            {
                if (!packager.ContainsKey("LastInput") && last is JsonArray { Count: > 0 } list) packager["LastInput"] = list[0]?.DeepClone();
                changed = true;
            }
        }
        return changed;
    }

    /// <summary>Serializes this configuration to the standard file in the specified directory.</summary>
    /// <param name="baseDir">Directory in which to write the configuration.</param>
    public void Save(string baseDir)
    {
        // Write the install directory as the user configured it (possibly relative), not its resolved absolute form.
        var resolved = Installer.InstallDirectory;
        Installer.InstallDirectory = Installer.RawInstallDirectory ?? resolved;
        try { File.WriteAllText(Path.Combine(baseDir, FileName), ToFileJson()); }
        finally { Installer.InstallDirectory = resolved; }
    }
}
