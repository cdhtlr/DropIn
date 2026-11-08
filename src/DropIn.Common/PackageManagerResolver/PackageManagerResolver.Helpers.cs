using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace DropIn.Common;

/// <summary>Shared package-manager provisioning helpers.</summary>
public static partial class PackageManagerResolver
{
    /// <summary>Finds the download URL for a named asset in a release response.</summary>

    static string AssetUrl(IEnumerable<JsonElement> assets, string name)
    {
        var asset = assets.FirstOrDefault(item =>
            item.TryGetProperty("name", out var assetName) && assetName.GetString() == name);
        if (asset.ValueKind == JsonValueKind.Undefined ||
            !asset.TryGetProperty("browser_download_url", out var url) ||
            string.IsNullOrWhiteSpace(url.GetString()))
            throw new InvalidDataException($"The latest WinGet release does not contain {name}.");
        return url.GetString()!;
    }

    /// <summary>Locates winget.exe in the registered App Installer package.</summary>
    static async Task<string?> FindRegisteredWingetAsync(CancellationToken ct)
    {
        var script = "$ErrorActionPreference='Stop'; $p=Get-AppxPackage -Name Microsoft.DesktopAppInstaller | Select-Object -First 1 -ExpandProperty InstallLocation; if ($p) { Join-Path $p 'winget.exe' }";
        var output = await RunPowerShellAsync(script, ct, allowFailure: true);
        var path = output.Trim();
        return path.Length > 0 && File.Exists(path) ? path : null;
    }

    /// <summary>Runs a non-interactive PowerShell command and returns its standard output.</summary>
    static async Task<string> RunPowerShellAsync(string script, CancellationToken ct, bool allowFailure = false)
    {
        var systemPowerShell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        var startInfo = new ProcessStartInfo
        {
            FileName = File.Exists(systemPowerShell) ? systemPowerShell : "powershell.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(script);
        using var process = new Process { StartInfo = startInfo };
        process.Start();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException ex)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            if (!ct.IsCancellationRequested)
                throw new TimeoutException("WinGet App Installer registration timed out.", ex);
            throw;
        }
        var output = await stdout;
        var error = await stderr;
        if (!allowFailure && process.ExitCode != 0)
            throw new InvalidOperationException("Could not register the WinGet App Installer package." +
                (string.IsNullOrWhiteSpace(error) ? "" : Environment.NewLine + error.Trim()));
        return output;
    }

    /// <summary>Escapes single quotes for safe insertion into a PowerShell single-quoted string.</summary>
    static string EscapePowerShell(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    /// <summary>Downloads an asset to a temporary path and then atomically moves it into the cache.</summary>
    static async Task DownloadCachedAsync(string url, string path, IProgress<string>? log, CancellationToken ct)
    {
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".download";
        try
        {
            await Net.DownloadAsync(url, temporaryPath, log, ct);
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    /// <summary>Copies a directory tree while preserving relative paths.</summary>
    static void CopyDirectory(string source, string destination)
    {
        foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
    }
}
