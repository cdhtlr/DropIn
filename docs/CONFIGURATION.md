# Configuration

See the [usage guide](USAGE.md) for an overview and the [programs guide](PROGRAMS.md) for program instructions.

Both programs share `dropin.config.json` in the folder selected during Installer registration. Before registration the default is `%LOCALAPPDATA%\DropIn`. A small `data-directory.txt` in that default location records the selected folder for both programs.

- Created with defaults before a data folder is selected. If the selected folder or its configuration is missing, the programs offer recovery instead of silently recreating it. New options are added automatically.
- Relative paths resolve against the data folder. Explicitly configured absolute paths remain unchanged. If a folder is not writable, the programs offer to restart as administrator.
- Selecting a different data folder copies settings, install history, database, packages, kept downloads, portable clients and logs into subfolders of the selected folder. Existing files are never overwritten and the original copies are retained as a backup. Previously installed applications remain at their original location to avoid breaking them, while future installations use the new folder. Conflicting files, overlapping application/data folders and linked source files or folders stop migration. Close Packager and finish any installations before changing folders.
- When upgrading from a program-folder configuration, it and the relative database are copied to the data folder. Existing installed applications remain at their original location to avoid breaking them, and install history is copied on first use. Other existing output and download folders are not moved automatically.
- An empty `Log.File` disables that log. `Log.Level` is `Error`, `Warning`, `Info` or `Debug`.

## `Installer`

| Key | Default | Description |
|---|---|---|
| `AfterInstall` | `AskRunSingleExe` | `AskRunSingleExe`, `OpenFolder`, `RunSingleExe` or `None` |
| `InstallDirectory` | `Applications` | Install and watch folder |
| `InstallSubfolderPattern` | `{Source}\{Name}` | Subfolder. Tokens `{Name}`, `{Version}`, `{Source}` (`winget`, `chocolatey`, `other`) |
| `OverwriteExisting` | `true` | Overwrite an existing install folder: `true`, `false`, `"Ask"` |
| `PortableAppStartMenuItem` | `true` | Start Menu item for portable/zip apps only |

## `Installer.Log`

| Key | Default | Description |
|---|---|---|
| `File` | `Logs\Installer.log` | Log file |
| `Level` | `Info` | Log level |

## `Installer.Progress`

| Key | Default | Description |
|---|---|---|
| `AutoClose` | `true` | Close the window after a successful install |
| `Show` | `true` | Show the progress window |

## `Installer.Screenshots`

| Key | Default | Description |
|---|---|---|
| `Show` | `true` | Show package screenshots |
| `Slideshow` | `Loop` | `Loop`, `UntilFinished` or `Off` |
| `SlideshowSeconds` | `4` | Seconds per screenshot (1-60) |

## `Installer.SilentInstall`

| Key | Default | Description |
|---|---|---|
| `ExtraSwitches` | `{}` | Extra silent switches per package name, e.g. `{ "Git": "/COMPONENTS=icons" }` |
| `Fallback` | `true` | Open the installer interactively if silent install fails |
| `TimeoutMinutes` | `0` | Stop a silent installer after N minutes (0 = no limit) |

## `Installer.Upgrade`

| Key | Default | Description |
|---|---|---|
| `MsiCleanUpgrade` | `true` | Uninstall the old MSI product before installing |
| `RemoveOldVersions` | `true` | Remove older versions of the same package: `true`, `false`, `"Ask"` |

## `Packager`

| Key | Default | Description |
|---|---|---|
| `OutputNamePattern` | `{Id}` | Output file name. Tokens `{Id}`, `{Version}`, `{Source}` |
| `OverwriteOutput` | `true` | Overwrite an existing `.app` |
| `PackageOutputDirectory` | `Packages` | Output folder |
| `SearchResultLimit` | `100` | Maximum search results (1-1000) |
| `UnknownInstallerType` | `zip` | Type assumed when it cannot be detected |
| `WarnInstallerTraces` | `true` | Warn when the installer looks like it contains personal traces |

## `Packager.Database`

| Key | Default | Description |
|---|---|---|
| `AutoUpdate` | `true` | Update the database automatically when `UpdateIntervalDays` has passed |
| `Directory` | `Database` | Database folder |
| `LastUpdate` | `null` | Time of the last update |
| `UpdateIntervalDays` | `30` | Days between automatic updates |

## `Packager.Downloads`

| Key | Default | Description |
|---|---|---|
| `Directory` | `Downloads` | Folder for kept installers |
| `KeepInstallers` | `false` | Keep downloaded installers |
| `VerifyChecksum` | `true` | Verify download checksums |

## `Packager.Log`

| Key | Default | Description |
|---|---|---|
| `File` | `Logs\Packager.log` | Log file |
| `Level` | `Info` | Log level |

## `Packager.Network`

| Key | Default | Description |
|---|---|---|
| `AllowInvalidCertificates` | `false` | UNSAFE: accept invalid TLS certificates for all DropIn downloads (installers, images, database). Enable only on a trusted network |
| `DownloadRetries` | `4` | Retries per download (1-10) |
| `MaxParallelDownloads` | `4` | Parallel connections per download (1-16) |
| `ProxyUrl` | `""` | Proxy URL |
| `UserAgent` | `DropIn-Packager/1.0` | HTTP user agent |

## `Packager.PortableClient`

| Key | Default | Description |
|---|---|---|
| `Directory` | `PortableClient` | Portable winget/choco folder |
| `Prefer` | `false` | Prefer the portable client over an installed one |

## `Packager.PreferredInstaller`

| Key | Default | Description |
|---|---|---|
| `Architecture` | `x64` | Preferred architecture |
| `Kind` | `portable` | `portable` or `installer` |
| `Type` | `exe` | Preferred installer type |

## `Packager.State`

| Key | Default | Description |
|---|---|---|
| `LastInput` | `""` | Last input |
| `LastSource` | `winget` | Last selected source |
| `WindowHeight` | `0` | Saved window height |
| `WindowWidth` | `0` | Saved window width |
