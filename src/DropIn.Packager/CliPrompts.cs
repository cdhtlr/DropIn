using DropIn.Common;

namespace DropIn.Packager;

/// <summary>Reads interactive version and installer choices from the console.</summary>
static class CliPrompts
{
    /// <summary>Displays available versions and reads the requested or default selection.</summary>
    public static string? PromptVersion(IReadOnlyList<string> versions, string? requested)
    {
        Console.WriteLine("Versions:");
        for (var i = 0; i < versions.Count; i++)
            Console.WriteLine($"  {i + 1}. {versions[i]}{(versions[i].Equals(requested, StringComparison.OrdinalIgnoreCase) || requested == null && i == 0 ? " (default)" : "")}");
        Console.Write($"Version [1-{versions.Count}; Enter=default, q=skip]: ");
        var answer = Console.ReadLine()?.Trim();
        if (answer?.Equals("q", StringComparison.OrdinalIgnoreCase) == true) return null;
        if (int.TryParse(answer, out var index) && index >= 1 && index <= versions.Count) return versions[index - 1];
        return requested ?? versions[0];
    }

    /// <summary>Displays installer choices and returns the selected or preferred option.</summary>
    public static InstallerOption? PromptOption(IList<InstallerOption> options, InstallerOption preferred)
    {
        Console.WriteLine("Installer options:");
        for (var i = 0; i < options.Count; i++)
        {
            var option = options[i];
            Console.WriteLine($"  {i + 1}. {option.Label}{(ReferenceEquals(option, preferred) ? " (default)" : "")}");
            foreach (var property in option.Properties.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
                Console.WriteLine($"       {property.Key}: {property.Value}");
        }
        Console.Write($"Option [1-{options.Count}; Enter=default, q=skip]: ");
        var answer = Console.ReadLine()?.Trim();
        if (answer?.Equals("q", StringComparison.OrdinalIgnoreCase) == true) return null;
        if (int.TryParse(answer, out var index) && index >= 1 && index <= options.Count) return options[index - 1];
        return preferred;
    }
}
