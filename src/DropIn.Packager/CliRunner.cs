using DropIn.Common;

namespace DropIn.Packager;

/// <summary>Runs the batch command-line packager.</summary>
static class CliRunner
{
    /// <summary>
    /// Batch CLI:
    /// DropIn.Packager [--output dir] [--yes] --package "winget install --id A" --package "choco install B"
    /// Legacy form remains supported: DropIn.Packager <winget|choco> <id> [version] [-o dir].
    /// </summary>
    public static async Task<int> RunAsync(string[] args)
    {
        NativeMethods.AttachConsole(PackagerConstants.AttachParentProcess);
        Console.InputEncoding = System.Text.Encoding.UTF8;
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        if (args[0] is "-h" or "--help")
        {
            Console.WriteLine("Usage: DropIn.Packager [-o dir] [--source winget|chocolatey] [-y] [--interactive] [--no-unigetui] -p <command-or-id> [--package ...]");
            Console.WriteLine("Example: DropIn.Packager --package \"winget install --id Git.Git\" --package \"choco install 7zip\"");
            Console.WriteLine("Legacy: DropIn.Packager <winget|choco> <package-id> [version] [-o dir]");
            return PackagerConstants.ExitSuccess;
        }

        var defaults = PackagerDefaults.LoadOrCreate();
        var options = CliOptions.Parse(args, out var error);
        if (options == null)
        {
            Console.Error.WriteLine(error);
            return PackagerConstants.ExitUsageError;
        }
        if (options.Requests.Count == 0)
        {
            Console.Error.WriteLine("Add one or more --package inputs.");
            return PackagerConstants.ExitUsageError;
        }

        try
        {
            if (await UniGetUiDatabase.EnsureAvailableAsync())
                Console.WriteLine("Downloaded the UniGetUI icon and screenshot database.");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or TaskCanceledException)
        {
            Console.Error.WriteLine("Could not load the UniGetUI database; packages will use the default icon: " + ex.Message);
        }

        var denied = Elevation.DeniedFolders(DropInConfig.Current.PackagerWriteDirectories(Path.GetFullPath(options.OutputDir)));
        if (denied.Count > 0)
        {
            Console.WriteLine(Elevation.Explain(denied));
            Console.WriteLine("Restarting as administrator in a new window...");
            using var elevated = Elevation.RestartElevated(args, out var failure);
            if (elevated == null)
            {
                Console.Error.WriteLine("Canceled: cannot write to " + string.Join(", ", denied) + ": " + failure);
                return PackagerConstants.ExitPackageFailed;
            }
            elevated.WaitForExit();
            return elevated.ExitCode;
        }
        PackageBuilder.ConfirmInstallerTraces = !DropInConfig.Current.Packager.WarnInstallerTraces ? null : (_, warning) =>
        {
            Console.WriteLine();
            Console.WriteLine("Warning: " + warning);
            // Without prompting (batch use) the warning is only shown.
            if (!options.Prompt) return true;
            Console.Write("Create the package anyway? [y/N]: ");
            return Console.ReadLine()?.Trim().StartsWith("y", StringComparison.OrdinalIgnoreCase) == true;
        };
        PackageBuilder.IconExtractor = options.Prompt ? path =>
        {
            Console.Write("No package icon was found. Choose [D]efault DropIn icon (default) or [E]xtract from installer EXE: ");
            return Console.ReadLine()?.Trim().StartsWith("e", StringComparison.OrdinalIgnoreCase) == true
                ? ExeIconExtractor.ExtractPng(path)
                : null;
        } : null;
        var failed = false;
        foreach (var request in options.Requests)
        {
            try
            {
                await BuildRequestAsync(request, options, defaults);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine($"Skipped '{request.Input}': canceled because of the installer warning.");
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or IOException
                                       or System.Text.Json.JsonException or InvalidDataException
                                       or YamlDotNet.Core.YamlException or UnauthorizedAccessException)
            {
                failed = true;
                Console.Error.WriteLine($"Error for '{request.Input}': {ex.Message}");
            }
        }
        return failed ? PackagerConstants.ExitPackageFailed : PackagerConstants.ExitSuccess;
    }

    /// <summary>Resolves, optionally prompts for, and builds a single requested package.</summary>
    static async Task BuildRequestAsync(PackageRequest request, CliOptions options, PackagerDefaults defaults)
    {
        var (source, parsed, requestedVersion) = ParseRequest(request, options.FallbackSource);
        var id = parsed.Id;
        Console.WriteLine($"\n=== {source}: {id} ===");
        var versions = source == PackagerConstants.WingetSource
            ? await WingetClient.GetVersionsAsync(id)
            : await ChocolateyClient.GetVersionsAsync(id);
        if (requestedVersion != null && !versions.Contains(requestedVersion, StringComparer.OrdinalIgnoreCase))
            versions.Insert(0, requestedVersion);
        // Non-interactive runs select an explicit version or the source-preferred first entry.
        var version = options.Prompt ? CliPrompts.PromptVersion(versions, requestedVersion) : requestedVersion ?? versions[0];
        if (version == null) { Console.WriteLine("Skipped."); return; }

        using var info = source == PackagerConstants.WingetSource
            ? await WingetClient.LoadAsync(id, version)
            : await ChocolateyClient.LoadAsync(id, version);
        // Parsed command attributes narrow the options before configured preferences break ties.
        var matches = InstallerHints.Matching(info.Installers, parsed);
        var preferred = defaults.Pick(matches.Count > 0 ? matches : info.Installers);
        var selected = options.Prompt ? CliPrompts.PromptOption(info.Installers, preferred) : preferred;
        if (selected == null) { Console.WriteLine("Skipped."); return; }
        Console.WriteLine($"Selected: {selected.Label}");

        var lastPercentage = -1;
        var progressLock = new object();
        var downloadProgress = new InlineProgress<DownloadProgress>(p =>
        {
            // Console output is shared across asynchronous download callbacks.
            lock (progressLock)
            {
                var percentage = p.Percentage;
                if (percentage is { } value && value > lastPercentage)
                {
                    lastPercentage = value;
                    Console.Write($"\rDownload: {value}%");
                }
            }
        });
        PackageMedia? media = null;
        if (options.UseUniGetUi)
        {
            media = await UniGetUiDatabase.FetchAsync(source, id, log: new Progress<string>(Console.WriteLine));
            Console.WriteLine(media == null
                ? "No icon/screenshots found in the UniGetUI database; using the default icon."
                : $"UniGetUI database: {(media.Icon == null ? "no icon" : "icon found")}, {media.Screenshots.Count} screenshot(s).");
        }
        await PackageBuilder.BuildAsync(info, selected, options.OutputDir, media,
            new Progress<string>(Console.WriteLine), default, downloadProgress);
        if (lastPercentage >= 0) Console.WriteLine();
    }

    /// <summary>Parses package input and resolves its source and version from explicit values or command hints.</summary>
    static (string Source, ParsedCommand Parsed, string? Version) ParseRequest(PackageRequest request, string fallbackSource)
    {
        var input = request.Input.Trim();
        string? explicitSource = request.Source;
        var colon = input.IndexOf(':');
        if (explicitSource == null && colon > 0 && input[..colon] is PackagerConstants.WingetSource or PackagerConstants.ChocoAlias or PackagerConstants.ChocolateySource)
        {
            explicitSource = input[..colon] == PackagerConstants.WingetSource ? PackagerConstants.WingetSource : PackagerConstants.ChocolateySource;
            input = input[(colon + 1)..].Trim();
        }
        var parsed = CommandParser.Parse(input)
                     ?? throw new InvalidOperationException($"Could not find a package id in '{request.Input}'.");
        return (explicitSource ?? parsed.Source ?? fallbackSource, parsed, request.Version ?? parsed.Version);
    }
}
