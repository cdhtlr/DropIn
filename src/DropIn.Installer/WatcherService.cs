using System.Collections.Concurrent;
using System.Runtime.Versioning;
using System.ServiceProcess;
using DropIn.Common;

namespace DropIn.Installer;

/// <summary>Windows service that installs every .app package or installer file (.exe, .msi, .msix, ...) dropped into the install directory (the only folder it watches).</summary>
[SupportedOSPlatform("windows")]
sealed partial class WatcherService : ServiceBase
{
    /// <summary>The Windows service name used during installation and service control.</summary>
    public const string ServiceId = InstallerConstants.WatcherServiceName;

    /// <summary>The configuration that controls installation and watched directories.</summary>
    readonly DropInConfig _cfg;
    /// <summary>The thread-safe queue of package paths waiting to be processed.</summary>
    readonly BlockingCollection<string> _queue = new();
    /// <summary>The paths already queued or processing, used to suppress duplicate file events.</summary>
    readonly HashSet<string> _pending = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>The active file-system watchers that must be disposed when the service stops.</summary>
    readonly List<FileSystemWatcher> _watchers = new();
    /// <summary>The cancellation source used to stop the background worker.</summary>
    CancellationTokenSource? _cts;
    /// <summary>The task that consumes queued package paths.</summary>
    Task? _worker;

    /// <summary>Creates the service with its installation configuration.</summary>
    public WatcherService(DropInConfig cfg)
    {
        _cfg = cfg;
        ServiceName = ServiceId;
        CanStop = true;
    }

    /// <summary>Builds a path under the service state directory.</summary>
    string StateDir(string sub) => Path.Combine(_cfg.Installer.InstallDirectory, InstallerConstants.StateDirectoryName, sub);

    /// <summary>Initializes package watching and starts the worker when the service starts.</summary>
    protected override void OnStart(string[] args)
    {
        InstallRunner.NonInteractive = true;
        InstallRunner.ConfirmRemoveOld = AskUser;
        Directory.CreateDirectory(_cfg.Installer.InstallDirectory);
        Setup.GrantInstallDirectoryAccess(_cfg.Installer.InstallDirectory);
        _cts = new CancellationTokenSource();
        _worker = Task.Run(() => Work(_cts.Token));

        var directory = _cfg.Installer.InstallDirectory;
        var watcher = new FileSystemWatcher(directory)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            IncludeSubdirectories = false,
            InternalBufferSize = 64 * 1024
        };
        watcher.Created += (_, e) => Enqueue(e.FullPath);
        watcher.Changed += (_, e) => Enqueue(e.FullPath);
        watcher.Renamed += (_, e) => Enqueue(e.FullPath);
        // A buffer overflow loses events; rescan the folder so no dropped file is missed.
        watcher.Error += (_, _) => Scan(directory);
        watcher.EnableRaisingEvents = true;
        _watchers.Add(watcher);
        Scan(directory);
    }
    /// <summary>Whether the watcher installs this file: an .app package or a raw installer, but never DropIn's own programs.</summary>
    static bool IsInstallable(string path)
    {
        var ext = Path.GetExtension(path);
        if (ext.Equals(AppManifest.Extension, StringComparison.OrdinalIgnoreCase)) return true;
        return Array.Exists(InstallerConstants.RawInstallerExtensions, e => e.Equals(ext, StringComparison.OrdinalIgnoreCase))
            && !Path.GetFileName(path).StartsWith(InstallerConstants.OwnProgramPrefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Queues any installable files already present in the install directory.</summary>
    void Scan(string directory)
    {
        try { foreach (var f in Directory.GetFiles(directory)) Enqueue(f); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Disposes watchers, stops polling, and signals the worker to stop.</summary>
    protected override void OnStop()
    {
        foreach (var w in _watchers) w.Dispose();
        _cts?.Cancel();
        _queue.CompleteAdding();
        try { _worker?.Wait(InstallerConstants.WorkerStopTimeout); } catch (AggregateException) { }
    }

    /// <summary>Adds a file path once, ignoring unsupported extensions and duplicate events.</summary>
    void Enqueue(string path)
    {
        if (!IsInstallable(path)) return;
        // FileSystemWatcher can report several events for a single copy; queue each path only once.
        lock (_pending) { if (!_pending.Add(path)) return; }
        try { _queue.Add(path); } catch (InvalidOperationException) { }
    }

    /// <summary>Consumes queued paths and logs failures without terminating the service worker.</summary>
    void Work(CancellationToken ct)
    {
        foreach (var path in _queue.GetConsumingEnumerable())
        {
            if (ct.IsCancellationRequested) break;
            try { Process(path, ct); }
            catch (Exception ex) { Log(path, "Unexpected error: " + ex.Message); }
            finally { lock (_pending) _pending.Remove(path); }
        }
    }

}
