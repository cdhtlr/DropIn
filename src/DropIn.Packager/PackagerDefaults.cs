using System.Text.Json;
using DropIn.Common;

namespace DropIn.Packager;

/// <summary>Stores preferred installer-option values and selects a suitable option.</summary>
sealed class PackagerDefaults
{
    /// <summary>Gets or sets the preferred manifest-property values used during option selection.</summary>
    public Dictionary<string, string> PreferredOptions { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Reads the preferences from the shared dropin.config.json next to the programs.</summary>
    public static PackagerDefaults LoadOrCreate()
        => new() { PreferredOptions = DropInConfig.LoadOrCreate(AppContext.BaseDirectory).Packager.PreferredOptions };

    /// <summary>Selects the best matching installer option using preferences and architecture priority.</summary>
    public InstallerOption Pick(IList<InstallerOption> options)
    {
        if (options.Count == 0) throw new InvalidOperationException("The manifest has no installer options.");
        // Apply each preference only when it leaves at least one candidate.
        var candidates = options.ToList();
        foreach (var preference in PreferredOptions)
        {
            var matching = candidates.Where(option =>
                option.Get(preference.Key).Equals(preference.Value, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matching.Count > 0) candidates = matching;
        }
        return candidates.OrderBy(option => option.Get(PackagerConstants.ArchitectureKey) switch
            { "x64" => 0, "neutral" or "" => 1, "x86" => 2, _ => 3 }).First();
    }
}
