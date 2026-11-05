using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace DropIn.Common;

/// <summary>One package returned by a package-manager search.</summary>
/// <param name="Name">Display name of the package.</param>
/// <param name="Id">Package identifier used by its source.</param>
/// <param name="Version">Package version reported by the search.</param>
public sealed record PackageSearchResult(string Name, string Id, string Version)
{
    /// <summary>Formats the package name, identifier, and version for display.</summary>
    public override string ToString() => $"{Name} | {Id} | {Version}";
}

/// <summary>Searches package catalogs with installed or locally provisioned package manager clients.</summary>
public static class PackageSearchClient
{
    /// <summary>Maximum number of results requested from package managers.</summary>
    public static int ResultLimit { get; set; } = 100;
    /// <summary>WinGet exit code indicating that the search produced no matches.</summary>
    const int WingetNoMatches = unchecked((int)0x8A150014);
    /// <summary>Community Chocolatey feed used for searches.</summary>
    const string ChocolateyFeed = "https://community.chocolatey.org/api/v2/";

    /// <summary>Runs a package search and parses the selected client output.</summary>
    public static async Task<List<PackageSearchResult>> SearchAsync(string source, string query,
        IProgress<string>? log = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) throw new ArgumentException("Enter a package name or search term.", nameof(query));

        var client = await PackageManagerResolver.ResolveAsync(source, log, ct);
        var startInfo = new ProcessStartInfo
        {
            FileName = client.FileName,
            WorkingDirectory = client.WorkingDirectory ?? AppContext.BaseDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (client.ChocolateyInstall is { } chocolateyInstall)
            startInfo.Environment["ChocolateyInstall"] = chocolateyInstall;

        if (source == "winget")
        {
            startInfo.ArgumentList.Add("search");
            startInfo.ArgumentList.Add(query.Trim());
            startInfo.ArgumentList.Add("--source");
            startInfo.ArgumentList.Add("winget");
            startInfo.ArgumentList.Add("--count");
            startInfo.ArgumentList.Add(ResultLimit.ToString());
            startInfo.ArgumentList.Add("--accept-source-agreements");
            startInfo.ArgumentList.Add("--disable-interactivity");
        }
        else
        {
            startInfo.ArgumentList.Add("search");
            startInfo.ArgumentList.Add(query.Trim());
            startInfo.ArgumentList.Add("--source");
            startInfo.ArgumentList.Add(ChocolateyFeed);
            startInfo.ArgumentList.Add("--limit-output");
            startInfo.ArgumentList.Add("--no-color");
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start()) throw new InvalidOperationException($"Could not start {client.FileName}.");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new InvalidOperationException($"Could not start the {source} client at '{client.FileName}'.", ex);
        }

        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }

        var output = await stdout;
        var error = await stderr;
        if (process.ExitCode != 0 && !(source == "winget" && process.ExitCode == WingetNoMatches))
        {
            var detail = error.Trim();
            if (detail.Length > 500) detail = detail[..500];
            throw new InvalidOperationException($"{Path.GetFileName(client.FileName)} search failed with exit code {process.ExitCode}." +
                (detail.Length == 0 ? "" : Environment.NewLine + detail));
        }

        return source == "winget" ? ParseWinget(output) : ParseChocolatey(output);
    }

    /// <summary>Parses the column-aligned text output produced by WinGet search.</summary>
    static List<PackageSearchResult> ParseWinget(string output)
    {
        var results = new List<PackageSearchResult>();
        foreach (var line in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            // Split aligned columns only on repeated spaces so package names may contain single spaces.
            var match = Regex.Match(line, @"^\s*(?<name>.+?)\s{2,}(?<id>[A-Za-z0-9][A-Za-z0-9._+-]*)\s{2,}(?<version>\S+)");
            if (!match.Success || match.Groups["name"].Value.Trim().Equals("Name", StringComparison.OrdinalIgnoreCase))
                continue;
            results.Add(new PackageSearchResult(
                match.Groups["name"].Value.Trim(),
                match.Groups["id"].Value,
                match.Groups["version"].Value));
        }
        return results;
    }

    /// <summary>Parses Chocolatey limit-output records separated by vertical bars.</summary>
    static List<PackageSearchResult> ParseChocolatey(string output)
    {
        var results = new List<PackageSearchResult>();
        foreach (var line in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Trim().Split('|');
            if (fields.Length < 2 || fields[0].Length == 0 || fields[1].Length == 0 ||
                fields[0].StartsWith("Chocolatey", StringComparison.OrdinalIgnoreCase))
                continue;
            results.Add(new PackageSearchResult(fields[0], fields[0], fields[1]));
            if (results.Count == ResultLimit) break;
        }
        return results;
    }
}
