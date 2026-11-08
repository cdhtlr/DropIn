using System.IO.Compression;

namespace DropIn.Common;

/// <summary>Finds installed package managers or provisions a local client for the current search.</summary>
public static partial class PackageManagerResolver
{
    /// <summary>Optional configured location for provisioned package-manager files.</summary>
    static string _portableOverride = "";
    /// <summary>Whether to prefer the locally provisioned client over an installed client.</summary>
    static bool _preferPortable;
    /// <summary>Base directory used to resolve relative portable-client locations.</summary>
    static string _baseDir = AppContext.BaseDirectory;

    /// <summary>Updates the portable-client location and client preference.</summary>
    public static void Configure(string? portableDirectory, bool preferPortable, string baseDir)
    {
        _portableOverride = portableDirectory ?? "";
        _preferPortable = preferPortable;
        _baseDir = baseDir;
    }

    /// <summary>Gets the absolute directory used to cache provisioned clients.</summary>
    static string PortableDirectory => !string.IsNullOrWhiteSpace(_portableOverride)
        ? Path.GetFullPath(Path.Combine(_baseDir, _portableOverride))
        : Path.Combine(_baseDir, CommonFileNames.PortableClientDirectory);

    /// <summary>Finds an installed client or provisions the requested package-manager client.</summary>
    public static async Task<PackageManagerCommand> ResolveAsync(string source, IProgress<string>? log = null,
        CancellationToken ct = default)
    {
        if (source is not ("winget" or "chocolatey"))
            throw new ArgumentException("Unknown package source.", nameof(source));

        if (!_preferPortable && Sources.FindExecutable(source == "winget" ? "winget" : "choco") is { } installed)
        {
            log?.Report($"Using installed {source} client: {installed}");
            return new PackageManagerCommand(installed);
        }

        return source == "winget"
            ? await ResolveWingetAsync(log, ct)
            : await ResolveChocolateyAsync(log, ct);
    }

    /// <summary>Uses an installed Chocolatey client or extracts the official package into the local cache.</summary>
    static async Task<PackageManagerCommand> ResolveChocolateyAsync(IProgress<string>? log, CancellationToken ct)
    {
        var root = Path.Combine(PortableDirectory, CommonFileNames.ChocolateyDirectory);
        var executable = Path.Combine(root, "bin", "choco.exe");
        if (!File.Exists(executable))
        {
            Directory.CreateDirectory(PortableDirectory);
            var staging = Path.Combine(PortableDirectory, "choco-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            var packagePath = Path.Combine(staging, "chocolatey.nupkg");
            var extracted = Path.Combine(staging, "extracted");
            try
            {
                log?.Report("Downloading the official Chocolatey CLI package...");
                await Net.DownloadAsync(CommonUrls.ChocolateyPackage, packagePath, log, ct);
                ZipFile.ExtractToDirectory(packagePath, extracted);
                var payload = Path.Combine(extracted, "tools", "chocolateyInstall");
                if (!File.Exists(Path.Combine(payload, "choco.exe")))
                    throw new InvalidDataException("The downloaded Chocolatey package did not contain choco.exe.");

                Directory.CreateDirectory(Path.Combine(root, "bin"));
                foreach (var directory in new[] { "lib", "config", "cache" })
                    Directory.CreateDirectory(Path.Combine(root, directory));
                // Extract the package payload into a reusable per-application client directory.
                CopyDirectory(payload, root);
                File.Copy(Path.Combine(payload, "choco.exe"), executable, true);
                File.Move(packagePath, Path.Combine(root, "chocolatey.nupkg"), true);
            }
            finally
            {
                try { Directory.Delete(staging, true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        log?.Report("Using the Chocolatey client in PortableClient.");
        return new PackageManagerCommand(executable, root, root);
    }
}
