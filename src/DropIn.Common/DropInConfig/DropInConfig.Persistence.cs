using System.Text.Json;
using System.Text.Json.Nodes;

namespace DropIn.Common;

/// <summary>Configuration loading, normalization, and persistence operations.</summary>
public sealed partial class DropInConfig
{
    /// <summary>Directory against which relative configuration paths are resolved.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string BaseDirectory { get; private set; } = DefaultDataDirectory;

    /// <summary>Default per-user data directory and location of the data-directory pointer.</summary>
    public static string DefaultDataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DropIn");

    static string DataDirectoryPointer => Path.Combine(DefaultDataDirectory, "data-directory.txt");

    /// <summary>Returns the selected data directory, or the default before selection.</summary>
    public static string DataDirectory => ReadDataDirectory(DataDirectoryPointer, DefaultDataDirectory);

    /// <summary>Loads the selected configuration without recreating a missing data folder or its settings.</summary>
    public static DropInConfig LoadSelected()
    {
        var pointerExists = File.Exists(DataDirectoryPointer);
        var directory = DataDirectory;
        return pointerExists ? LoadExisting(directory) : LoadOrCreate(directory);
    }

    /// <summary>Loads an existing data directory and configuration without replacing missing data.</summary>
    public static DropInConfig LoadExisting(string baseDir) => Load(baseDir, false);

    /// <summary>Checks that an already loaded configuration has not disappeared.</summary>
    public void EnsureDataAvailable()
    {
        if (!Directory.Exists(BaseDirectory)) throw new DataDirectoryUnavailableException(BaseDirectory);
        if (!File.Exists(Path.Combine(BaseDirectory, FileName)))
            throw new DataDirectoryUnavailableException(BaseDirectory, configurationMissing: true);
    }

    internal static string ReadDataDirectory(string pointer, string defaultDirectory)
    {
        if (!File.Exists(pointer)) return defaultDirectory;
        var path = File.ReadAllText(pointer).Trim();
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new InvalidDataException("The DropIn data-directory pointer does not contain an absolute path.");
        if (!Directory.Exists(path))
            throw new DataDirectoryUnavailableException(path);
        return Path.GetFullPath(path);
    }

    /// <summary>Uses an explicitly selected existing directory without overwriting its data-directory pointer until loading succeeds.</summary>
    public static DropInConfig RecoverDataDirectory(string directory) => RecoverDataDirectory(directory, DataDirectoryPointer);

    internal static DropInConfig RecoverDataDirectory(string directory, string pointer)
    {
        var target = Path.GetFullPath(directory);
        if (!Directory.Exists(target)) throw new DirectoryNotFoundException("The selected DropIn data directory is unavailable: " + target);
        var config = File.Exists(Path.Combine(target, FileName)) ? LoadExisting(target) : LoadOrCreate(target);
        WriteDataDirectoryPointer(pointer, target);
        return config;
    }

    /// <summary>Copies settings and existing data to a selected directory without moving installed software.</summary>
    public DropInConfig SelectDataDirectory(string directory) => SelectDataDirectory(directory, DataDirectoryPointer);

    internal DropInConfig SelectDataDirectory(string directory, string pointer)
    {
        var target = Path.GetFullPath(directory);
        if (!Directory.Exists(target)) throw new DirectoryNotFoundException(target);
        if (!WriteAccess.IsWritable(target)) throw new UnauthorizedAccessException("Cannot write to " + target);
        EnsureNoLinkedParents(target);
        var created = new List<string>();
        if (!string.Equals(Path.TrimEndingDirectorySeparator(target), Path.TrimEndingDirectorySeparator(BaseDirectory), StringComparison.OrdinalIgnoreCase))
        {
            var configPath = Path.Combine(target, FileName);
            if (File.Exists(configPath))
                throw new IOException("The selected folder already contains a DropIn configuration. Choose another folder to avoid overwriting it.");
            var historyTarget = Path.Combine(target, InstallHistory.StateDirectoryName, "installed.json");
            var directories = new (string Source, string Destination)[]
            {
                (ResolveDataPath(Packager.DatabaseDirectory), Path.Combine(target, CommonDefaults.DatabaseDirectory)),
                (ResolveOutputDirectory(), Path.Combine(target, CommonDefaults.OutputDirectory)),
                (ResolveDataPath(Packager.DownloadsDirectory), Path.Combine(target, CommonDefaults.DownloadsDirectory)),
                (ResolveDataPath(Packager.PortableClientDirectory), Path.Combine(target, CommonDefaults.PortableClientDirectory))
            };
            foreach (var (source, destination) in directories)
            {
                if (!Directory.Exists(source)) continue;
                if (PathsOverlap(source, destination) || PathsOverlap(source, Installer.InstallDirectory)
                    || PathsOverlap(source, target))
                    throw new IOException("Cannot safely migrate overlapping data or application directories: " + source);
            }
            try
            {
                using (var stream = new FileStream(configPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream))
                {
                    created.Add(configPath);
                    var resolved = Installer.InstallDirectory;
                    Installer.InstallDirectory = Installer.RawInstallDirectory ?? resolved;
                    try { writer.Write(ToFileJson()); }
                    finally { Installer.InstallDirectory = resolved; }
                }
                var selected = LoadOrCreate(target);
                selected.Installer.InstallDirectory = CommonDefaults.InstallDirectory;
                selected.Installer.RawInstallDirectory = CommonDefaults.InstallDirectory;
                selected.Packager.DatabaseDirectory = CommonDefaults.DatabaseDirectory;
                selected.Packager.PackageOutputDirectory = CommonDefaults.OutputDirectory;
                selected.Packager.DownloadsDirectory = CommonDefaults.DownloadsDirectory;
                selected.Packager.PortableClientDirectory = CommonDefaults.PortableClientDirectory;
                if (!string.IsNullOrWhiteSpace(selected.Installer.LogFile)) selected.Installer.LogFile = CommonDefaults.InstallerLogFile;
                if (!string.IsNullOrWhiteSpace(selected.Packager.LogFile)) selected.Packager.LogFile = CommonDefaults.PackagerLogFile;
                selected.Save(target);
                var historySource = Path.Combine(BaseDirectory, InstallHistory.StateDirectoryName, "installed.json");
                if (!File.Exists(historySource))
                    historySource = Path.Combine(Installer.InstallDirectory, InstallHistory.StateDirectoryName, "installed.json");
                CopyIfPresent(historySource, historyTarget, created);
                CopyIfPresent(Path.Combine(BaseDirectory, InstallHistory.StateDirectoryName, "service.log"),
                    Path.Combine(target, InstallHistory.StateDirectoryName, "service.log"), created);
                foreach (var (source, destination) in directories)
                    CopyDataDirectory(source, destination, created);
                if (!string.IsNullOrWhiteSpace(Installer.LogFile))
                    CopyIfPresent(ResolveDataPath(Installer.LogFile), Path.Combine(target, CommonDefaults.InstallerLogFile), created);
                if (!string.IsNullOrWhiteSpace(Packager.LogFile))
                    CopyIfPresent(ResolveDataPath(Packager.LogFile), Path.Combine(target, CommonDefaults.PackagerLogFile), created);
            }
            catch
            {
                Apply(BaseDirectory);
                foreach (var file in created) File.Delete(file);
                throw;
            }
        }
        try { WriteDataDirectoryPointer(pointer, target); }
        catch
        {
            Apply(BaseDirectory);
            foreach (var file in created) File.Delete(file);
            throw;
        }
        return LoadOrCreate(target);
    }

    static void WriteDataDirectoryPointer(string pointer, string target)
    {
        var temp = pointer + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(pointer)!);
            File.WriteAllText(temp, target);
            File.Move(temp, pointer, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    static void CopyIfPresent(string source, string destination, List<string> created)
    {
        if (!File.Exists(source)) return;
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Cannot migrate a linked file: " + source);
        EnsureNoLinkedParents(Path.GetDirectoryName(destination)!);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        created.Add(destination);
        input.CopyTo(output);
    }

    string ResolveDataPath(string path) => Path.GetFullPath(Path.Combine(BaseDirectory, path));

    static bool PathsOverlap(string first, string second)
    {
        var a = Path.TrimEndingDirectorySeparator(Path.GetFullPath(first));
        var b = Path.TrimEndingDirectorySeparator(Path.GetFullPath(second));
        return a.Equals(b, StringComparison.OrdinalIgnoreCase)
            || a.StartsWith(Path.EndsInDirectorySeparator(b) ? b : b + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || b.StartsWith(Path.EndsInDirectorySeparator(a) ? a : a + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    static void EnsureNoLinkedParents(string directory)
    {
        for (var current = new DirectoryInfo(directory); current != null; current = current.Parent)
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Cannot migrate through a linked directory: " + current.FullName);
    }

    static void CopyDataDirectory(string source, string destination, List<string> created)
    {
        if (!Directory.Exists(source)) return;
        EnsureNoLinkedParents(source);
        EnsureNoLinkedParents(destination);
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
            CopyIfPresent(file, Path.Combine(destination, Path.GetFileName(file)), created);
        foreach (var child in Directory.EnumerateDirectories(source))
            CopyDataDirectory(child, Path.Combine(destination, Path.GetFileName(child)), created);
    }
    /// <summary>The most recently loaded configuration, for code that has no config instance at hand.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public static DropInConfig Current { get; private set; } = new();

    /// <summary>JSON formatting options used when saving configuration.</summary>
    static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <summary>Returns the default package installation directory under the data directory.</summary>
    /// <param name="baseDir">Data directory used as the base.</param>
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

    /// <summary>Folders the packager writes to, including the config, database, output, downloads and portable client.</summary>
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

    /// <summary>Adds or replaces the history entry for a package folder in the data directory.</summary>
    public static void RecordInstall(InstalledPackage entry)
    {
        try { InstallHistory.Record(Current.Installer.InstallDirectory, entry); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            AppLog.Error("Could not record the installation: " + ex.Message);
        }
    }
    /// <summary>Loads the config from <paramref name="baseDir"/>, creating a default one if it is missing or invalid.</summary>
    public static DropInConfig LoadOrCreate(string baseDir) => Load(baseDir, true);

    static DropInConfig Load(string baseDir, bool createIfMissing)
    {
        if (createIfMissing) Directory.CreateDirectory(baseDir);
        else if (!Directory.Exists(baseDir)) throw new DataDirectoryUnavailableException(baseDir);
        var path = Path.Combine(baseDir, FileName);
        if (!createIfMissing && !File.Exists(path))
            throw new DataDirectoryUnavailableException(baseDir, configurationMissing: true);
        var oldPath = Path.Combine(AppContext.BaseDirectory, FileName);
        var migrating = !File.Exists(path) && !string.Equals(Path.GetFullPath(path), Path.GetFullPath(oldPath), StringComparison.OrdinalIgnoreCase) && File.Exists(oldPath);
        DropInConfig? cfg = null;
        JsonNode? legacyHistory = null;
        JsonNode? original = null;
        var sourcePath = migrating ? oldPath : path;
        if (File.Exists(sourcePath))
        {
            try
            {
                var text = File.ReadAllText(sourcePath);
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
            catch (JsonException ex) when (!createIfMissing)
            {
                throw new InvalidDataException("The DropIn configuration is invalid: " + sourcePath, ex);
            }
            catch (JsonException) { cfg = null; original = null; }
        }
        if (!createIfMissing && cfg == null)
            throw new InvalidDataException("The DropIn configuration is invalid or missing: " + sourcePath);
        cfg ??= new DropInConfig();
        cfg.Normalize();
        if (migrating)
        {
            // Existing applications must remain at their original paths; moving installed software can break it.
            if (!Path.IsPathRooted(cfg.Installer.InstallDirectory))
            {
                var previousInstallDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, cfg.Installer.InstallDirectory));
                if (Directory.Exists(previousInstallDirectory))
                    cfg.Installer.InstallDirectory = previousInstallDirectory;
            }
            if (!Path.IsPathRooted(cfg.Packager.DatabaseDirectory))
            {
                var previousDatabase = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, cfg.Packager.DatabaseDirectory));
                var newDatabase = Path.GetFullPath(Path.Combine(baseDir, cfg.Packager.DatabaseDirectory));
                if (Directory.Exists(previousDatabase) &&
                    !string.Equals(previousDatabase, newDatabase, StringComparison.OrdinalIgnoreCase))
                    CopyDirectory(previousDatabase, newDatabase);
            }
        }
        // Write the file when it differs from the grouped layout: old layouts, options added in a newer version, or blanks filled with defaults.
        var desired = cfg.ToFileJson();
        if (migrating || original == null || original.ToJsonString() != JsonNode.Parse(desired)!.ToJsonString())
        {
            File.WriteAllText(path, desired);
        }
        cfg.Installer.RawInstallDirectory = cfg.Installer.InstallDirectory;
        if (!Path.IsPathRooted(cfg.Installer.InstallDirectory))
            cfg.Installer.InstallDirectory = Path.GetFullPath(Path.Combine(baseDir, cfg.Installer.InstallDirectory));
        cfg.Apply(baseDir);
        if (legacyHistory != null) InstallHistory.Import(cfg.Installer.InstallDirectory, legacyHistory);
        return cfg;
    }

    static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            var target = Path.Combine(destination, Path.GetFileName(file));
            if (!File.Exists(target)) File.Copy(file, target, false);
        }
        foreach (var directory in Directory.EnumerateDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
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
        Directory.CreateDirectory(baseDir);
        // Write the install directory as the user configured it (possibly relative), not its resolved absolute form.
        var resolved = Installer.InstallDirectory;
        Installer.InstallDirectory = Installer.RawInstallDirectory ?? resolved;
        try { File.WriteAllText(Path.Combine(baseDir, FileName), ToFileJson()); }
        finally { Installer.InstallDirectory = resolved; }
    }
}
