using System.Text.RegularExpressions;
using DropIn.Common;

namespace DropIn.Packager;

sealed partial class MainForm
{
    /// <summary>Replaces the search-result list and updates its visibility.</summary>
    void SetResults(List<PackageSearchResult>? results)
    {
        _searchResults.Items.Clear();
        if (results != null)
        {
            _searchResults.Items.AddRange(results.Cast<object>().ToArray());
            _searchResults.SelectedIndex = 0;
        }
        _resultsLabel.Visible = _searchResults.Visible = results is { Count: > 0 };
    }

    /// <summary>Searches for the entered package and fetches the first matching result.</summary>
    Task SearchAsync() => RunAsync(_search, async ct =>
    {
        _log.Clear();
        var query = _package.Text.Trim();
        if (Regex.IsMatch(query, @"^(winget|choco|chocolatey|cinst)\s+", RegexOptions.IgnoreCase))
        {
            var parsed = CommandParser.Parse(query);
            if (parsed != null)
            {
                if (parsed.Source != null) SelectSource(parsed.Source);
                query = parsed.Id;
            }
        }
        if (query.Length == 0) { Log("Enter a package name or search term."); return; }
        SetResults(null);
        List<PackageSearchResult>? results = null;
        string? failure = null;
        try
        {
            Log($"Searching {SourceName} for \"{query}\"...");
            results = await PackageSearchClient.SearchAsync(SourceName, query, new Progress<string>(Log), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            failure = ex.Message;
        }
        if (results is { Count: > 0 })
        {
            SetResults(results);
            Log($"{results.Count} package result(s); showing the first one. Pick another from the list to switch.");
            _package.Text = results[0].Id;
            await FetchCoreAsync(ct);
            return;
        }
        // Nothing usable found: skip the results list and try to fetch the input as a package id.
        Log(failure == null ? "No search results; trying to fetch it directly..." : "Search failed (" + failure + "); trying to fetch it directly...");
        await FetchCoreAsync(ct);
    });

    /// <summary>Fetches the manifest for the current package input.</summary>
    Task FetchAsync() => RunAsync(_fetch, async ct =>
    {
        _log.Clear();
        await FetchCoreAsync(ct);
    });

    /// <summary>Parses the package input, loads its versions and manifest, and resolves media.</summary>
    async Task FetchCoreAsync(CancellationToken ct)
    {
        var parsed = CommandParser.Parse(_package.Text);
        if (parsed == null) { Log("Enter a package id or an install command."); return; }
        _hint = parsed;
        _askedTraces.Clear();
        _acceptedTraces.Clear();
        _media = null;
        _iconInput.Clear();
        _screenshotInput.Clear();
        if (parsed.Source != null) SelectSource(parsed.Source);
        Log($"Looking up {parsed.Id} on {SourceName}...");
        var versions = SourceName == PackagerConstants.WingetSource
            ? await WingetClient.GetVersionsAsync(parsed.Id, ct)
            : await ChocolateyClient.GetVersionsAsync(parsed.Id, ct);
        // Preserve an explicitly requested version even when the source does not list it.
        if (parsed.Version != null && !versions.Contains(parsed.Version)) versions.Insert(0, parsed.Version);
        _loading = true;
        _version.Items.Clear();
        _version.Items.AddRange(versions.Cast<object>().ToArray());
        _version.SelectedItem = parsed.Version ?? versions[0];
        _loading = false;
        await LoadVersionCoreAsync((string)_version.SelectedItem!, ct);
        await LookupMediaAsync(_info?.Id ?? parsed.Id, ct);
        Remember(_package.Text);
        CheckTraces();
    }

    /// <summary>Loads a selected package version through the cancelable fetch action.</summary>
    Task LoadVersionAsync(string? version) => RunAsync(_fetch, async ct =>
    {
        await LoadVersionCoreAsync(version, ct);
        CheckTraces();
    });

    /// <summary>Retrieves the manifest for a version and rebuilds the installer filters.</summary>
    async Task LoadVersionCoreAsync(string? version, CancellationToken ct)
    {
        if (_loading || version == null || _hint == null) return;
        Log($"Loading manifest {_hint.Id} {version}...");
        var info = SourceName == PackagerConstants.WingetSource
            ? await WingetClient.LoadAsync(_hint.Id, version, ct)
            : await ChocolateyClient.LoadAsync(_hint.Id, version, ct);
        _info?.Dispose();
        _info = info;
        _fetching = true;
        try { BuildFilters(); }
        finally { _fetching = false; }
        Log($"{info.Installers.Count} installer option(s) found.");
    }
}
