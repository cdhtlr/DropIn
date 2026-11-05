using DropIn.Common;

namespace DropIn.Packager;

/// <summary>Narrows installer options using hints parsed from an install command.</summary>
static class InstallerHints
{
    /// <summary>Returns the options matching every hint present in the command; a null command matches all options.</summary>
    public static List<InstallerOption> Matching(IEnumerable<InstallerOption> options, ParsedCommand? hint)
        => options.Where(option =>
            Matches(option, PackagerConstants.ArchitectureKey, hint?.Architecture)
            && Matches(option, PackagerConstants.InstallerTypeKey, hint?.InstallerType)
            && Matches(option, PackagerConstants.ScopeKey, hint?.Scope)
            && Matches(option, PackagerConstants.LocaleKey, hint?.Locale)).ToList();

    /// <summary>Returns whether the option has the given property value, or no value was requested.</summary>
    static bool Matches(InstallerOption option, string key, string? wanted)
        => wanted == null || option.Get(key).Equals(wanted, StringComparison.OrdinalIgnoreCase);
}
