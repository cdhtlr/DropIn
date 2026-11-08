using System.Text.Json;
using System.Text.Json.Nodes;

namespace DropIn.Common;

public sealed partial class DropInConfig
{
    /// <summary>
    /// Groups of the file layout: section, group, key inside the group, and the property it maps to.
    /// Properties that are not listed stay directly in their section.
    /// </summary>
    static readonly (string Section, string Group, string Key, string Property)[] Layout =
    {
        ("Installer", "Log", "File", nameof(InstallerSettings.LogFile)),
        ("Installer", "Log", "Level", nameof(InstallerSettings.LogLevel)),
        ("Installer", "Progress", "AutoClose", nameof(InstallerSettings.AutoCloseProgressWindow)),
        ("Installer", "Progress", "Show", nameof(InstallerSettings.ShowProgressWindow)),
        ("Installer", "Screenshots", "Show", nameof(InstallerSettings.ShowScreenshots)),
        ("Installer", "Screenshots", "Slideshow", nameof(InstallerSettings.ScreenshotSlideshow)),
        ("Installer", "Screenshots", "SlideshowSeconds", nameof(InstallerSettings.ScreenshotSlideshowSeconds)),
        ("Installer", "SilentInstall", "ExtraSwitches", nameof(InstallerSettings.ExtraSilentSwitches)),
        ("Installer", "SilentInstall", "Fallback", nameof(InstallerSettings.SilentFallback)),
        ("Installer", "SilentInstall", "TimeoutMinutes", nameof(InstallerSettings.InstallerTimeoutMinutes)),
        ("Installer", "Upgrade", "MsiCleanUpgrade", nameof(InstallerSettings.MsiCleanUpgrade)),
        ("Installer", "Upgrade", "RemoveOldVersions", nameof(InstallerSettings.RemoveOldVersions)),

        ("Packager", "Database", "AutoUpdate", nameof(PackagerSettings.AutoUpdateDatabase)),
        ("Packager", "Database", "Directory", nameof(PackagerSettings.DatabaseDirectory)),
        ("Packager", "Database", "LastUpdate", nameof(PackagerSettings.LastDatabaseUpdate)),
        ("Packager", "Database", "UpdateIntervalDays", nameof(PackagerSettings.DatabaseUpdateIntervalDays)),
        ("Packager", "Downloads", "Directory", nameof(PackagerSettings.DownloadsDirectory)),
        ("Packager", "Downloads", "KeepInstallers", nameof(PackagerSettings.KeepDownloadedInstallers)),
        ("Packager", "Downloads", "VerifyChecksum", nameof(PackagerSettings.VerifyChecksum)),
        ("Packager", "Log", "File", nameof(PackagerSettings.LogFile)),
        ("Packager", "Log", "Level", nameof(PackagerSettings.LogLevel)),
        ("Packager", "Network", "AllowInvalidCertificates", nameof(PackagerSettings.AllowInvalidCertificates)),
        ("Packager", "Network", "DownloadRetries", nameof(PackagerSettings.DownloadRetries)),
        ("Packager", "Network", "MaxParallelDownloads", nameof(PackagerSettings.MaxParallelDownloads)),
        ("Packager", "Network", "ProxyUrl", nameof(PackagerSettings.ProxyUrl)),
        ("Packager", "Network", "UserAgent", nameof(PackagerSettings.UserAgent)),
        ("Packager", "PortableClient", "Directory", nameof(PackagerSettings.PortableClientDirectory)),
        ("Packager", "PortableClient", "Prefer", nameof(PackagerSettings.PreferPortableClient)),
        ("Packager", "State", "LastInput", nameof(PackagerSettings.LastInput)),
        ("Packager", "State", "LastSource", nameof(PackagerSettings.LastSource)),
        ("Packager", "State", "WindowHeight", nameof(PackagerSettings.WindowHeight)),
        ("Packager", "State", "WindowWidth", nameof(PackagerSettings.WindowWidth)),
    };

    /// <summary>Groups written by earlier versions; they are still read, and dropped options map to properties that no longer exist.</summary>
    static readonly (string Section, string Group, string Key, string Property)[] LegacyLayout =
    {
        ("Installer", "Silent", "ExtraSwitches", nameof(InstallerSettings.ExtraSilentSwitches)),
        ("Installer", "Silent", "Fallback", nameof(InstallerSettings.SilentFallback)),
        ("Installer", "Silent", "TimeoutMinutes", nameof(InstallerSettings.InstallerTimeoutMinutes)),
        ("Installer", "Watcher", "Enabled", "WatcherEnabled"),
        ("Installer", "Watcher", "PollSeconds", "WatcherPollSeconds"),
        ("Packager", "Search", "ResultLimit", nameof(PackagerSettings.SearchResultLimit)),
    };

    /// <summary>Option names used by earlier versions and their current names.</summary>
    static readonly (string Old, string New)[] Renames =
    {
        ("CreateStartMenuItem", nameof(InstallerSettings.PortableAppStartMenuItem)),
        ("DefaultOutputDirectory", nameof(PackagerSettings.PackageOutputDirectory)),
        ("DefaultInstallerType", nameof(PackagerSettings.UnknownInstallerType)),
    };

    /// <summary>File key of the preferred-installer group, which maps to <see cref="PackagerSettings.PreferredOptions"/>.</summary>
    const string PreferredInstallerKey = "PreferredInstaller";

    /// <summary>Friendly keys of the preferred-installer group and the manifest properties they stand for.</summary>
    static readonly (string FileKey, string Property)[] PreferredKeys =
    {
        ("Kind", InstallerOption.KindKey),
        ("Type", "InstallerType"),
    };

    /// <summary>Moves the grouped keys of the file layout up into their sections; flat sections are left unchanged.</summary>
    static void Flatten(JsonObject root)
    {
        var all = Layout.Concat(LegacyLayout).ToArray();
        foreach (var section in new[] { nameof(Installer), nameof(Packager) })
        {
            if (root[section] is not JsonObject sectionNode) continue;
            foreach (var (_, group, key, property) in all.Where(l => l.Section == section))
            {
                if (sectionNode[group] is not JsonObject groupNode || !groupNode.Remove(key, out var value)) continue;
                if (!sectionNode.ContainsKey(property)) sectionNode[property] = value;
            }
            foreach (var group in all.Where(l => l.Section == section).Select(l => l.Group).Distinct())
                sectionNode.Remove(group);
            foreach (var (oldName, newName) in Renames)
                if (sectionNode.Remove(oldName, out var renamed) && !sectionNode.ContainsKey(newName)) sectionNode[newName] = renamed;
            if (section == nameof(Packager) && sectionNode.Remove(PreferredInstallerKey, out var preferred) && preferred is JsonObject preferredObject)
            {
                var options = new JsonObject();
                foreach (var (key, value) in preferredObject)
                {
                    var property = PreferredKeys.FirstOrDefault(k => k.FileKey == key).Property ?? key;
                    options[property] = value?.DeepClone();
                }
                if (!sectionNode.ContainsKey(nameof(PackagerSettings.PreferredOptions))) sectionNode[nameof(PackagerSettings.PreferredOptions)] = options;
            }
        }
    }

    /// <summary>Returns the serialized flat configuration arranged into the grouped file layout.</summary>
    static JsonObject Nest(JsonObject flat)
    {
        var result = new JsonObject();
        foreach (var (name, value) in flat)
        {
            if (value is not JsonObject section || name is not (nameof(Installer) or nameof(Packager)))
            {
                result[name] = value?.DeepClone();
                continue;
            }
            var nested = new JsonObject();
            foreach (var (property, propertyValue) in section)
            {
                if (name == nameof(Packager) && property == nameof(PackagerSettings.PreferredOptions) && propertyValue is JsonObject options)
                {
                    var preferred = new JsonObject();
                    foreach (var (key, optionValue) in options)
                        preferred[PreferredKeys.FirstOrDefault(k => k.Property == key).FileKey ?? key] = optionValue?.DeepClone();
                    nested[PreferredInstallerKey] = preferred;
                    continue;
                }
                var map = Layout.FirstOrDefault(l => l.Section == name && l.Property == property);
                if (map.Group == null) { nested[property] = propertyValue?.DeepClone(); continue; }
                if (nested[map.Group] is not JsonObject group) nested[map.Group] = group = new JsonObject();
                group[map.Key] = propertyValue?.DeepClone();
            }
            result[name] = nested;
        }
        return Sorted(result);
    }

    /// <summary>Returns a copy of <paramref name="node"/> with the keys of every object sorted alphabetically.</summary>
    static JsonObject Sorted(JsonObject node)
    {
        var sorted = new JsonObject();
        foreach (var (key, value) in node.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
            sorted[key] = value is JsonObject child ? Sorted(child) : value?.DeepClone();
        return sorted;
    }

    /// <summary>Serializes this configuration in the grouped, alphabetically sorted file layout.</summary>
    string ToFileJson() => Nest((JsonObject)JsonSerializer.SerializeToNode(this)!).ToJsonString(WriteOptions);
}
