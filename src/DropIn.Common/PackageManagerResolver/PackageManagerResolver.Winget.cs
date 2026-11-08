using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace DropIn.Common;

/// <summary>WinGet client discovery and provisioning.</summary>
public static partial class PackageManagerResolver
{
static async Task<PackageManagerCommand> ResolveWingetAsync(IProgress<string>? log, CancellationToken ct)
    {
        if (await FindRegisteredWingetAsync(ct) is { } registered)
        {
            log?.Report($"Using the installed WinGet App Installer client: {registered}");
            return new PackageManagerCommand(registered);
        }

        var root = Path.Combine(PortableDirectory, CommonFileNames.WingetDirectory);
        var bundlePath = Path.Combine(root, CommonFileNames.WingetBundle);
        var dependenciesZip = Path.Combine(root, CommonFileNames.WingetDependencies);
        if (!File.Exists(bundlePath) || !File.Exists(dependenciesZip))
        {
            Directory.CreateDirectory(root);
            log?.Report("Checking the latest official WinGet release...");
            using var release = JsonDocument.Parse(await Net.GetStringAsync(CommonUrls.WingetReleaseApi, ct));
            if (!release.RootElement.TryGetProperty("assets", out var assetList) || assetList.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("GitHub returned an invalid WinGet release response.");
            var assets = assetList.EnumerateArray().ToList();
            var bundleUrl = AssetUrl(assets, CommonFileNames.WingetBundle);
            var dependenciesUrl = AssetUrl(assets, CommonFileNames.WingetDependencies);
            if (!File.Exists(dependenciesZip))
            {
                log?.Report("Downloading WinGet dependencies to PortableClient...");
                await DownloadCachedAsync(dependenciesUrl, dependenciesZip, log, ct);
            }
            if (!File.Exists(bundlePath))
            {
                log?.Report("Downloading the official WinGet App Installer package to PortableClient...");
                await DownloadCachedAsync(bundleUrl, bundlePath, log, ct);
            }
        }

        // Re-extract dependencies when the ready marker is absent or the prior extraction was incomplete.
        var dependencyRoot = Path.Combine(root, "Dependencies");
        var readyMarker = Path.Combine(dependencyRoot, ".ready");
        if (!File.Exists(readyMarker))
        {
            if (Directory.Exists(dependencyRoot)) Directory.Delete(dependencyRoot, true);
            Directory.CreateDirectory(dependencyRoot);
            ZipFile.ExtractToDirectory(dependenciesZip, dependencyRoot);
            await File.WriteAllTextAsync(readyMarker, "ok", ct);
        }

        var architecture = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.X86 => "x86",
            Architecture.Arm64 => "arm64",
            _ => throw new PlatformNotSupportedException($"WinGet is not supported on {RuntimeInformation.OSArchitecture}."),
        };
        var architectureDirectory = Path.Combine(dependencyRoot, architecture);
        var dependencies = Directory.Exists(architectureDirectory)
            ? Directory.GetFiles(architectureDirectory, "*.appx")
            : Array.Empty<string>();
        if (dependencies.Length == 0)
            throw new InvalidDataException($"The WinGet release did not contain dependencies for {architecture}.");

        // AppX registration is required because WinGet is not a standalone portable executable.
        log?.Report("Registering the official WinGet package for the current Windows user. WinGet is distributed as an AppX package and cannot run as a standalone portable executable.");
        var dependencyArguments = string.Join(",", dependencies.Select(path => "'" + EscapePowerShell(path) + "'"));
        var script = $"$ErrorActionPreference='Stop'; Add-AppxPackage -Path '{EscapePowerShell(bundlePath)}' -DependencyPath @({dependencyArguments})";
        await RunPowerShellAsync(script, ct);

        var executable = Sources.FindExecutable("winget");
        if (executable == null) executable = await FindRegisteredWingetAsync(ct);
        if (executable == null)
            throw new InvalidOperationException("WinGet was registered, but its executable could not be located. Enable the App Installer winget.exe execution alias in Windows Settings.");
        log?.Report($"Using the WinGet client: {executable}");
        return new PackageManagerCommand(executable);
    }
}
