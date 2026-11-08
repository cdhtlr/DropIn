# Building and installation

The Installer, Packager and Explorer icon handler require the matching-architecture [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0). Setup checks for a registered runtime. Setup and published application resources use English only.

Setup creates a **DropIn** Start Menu folder with Installer and Packager shortcuts, and launches the Installer after an interactive installation. Running setup again offers to reinstall or uninstall DropIn. When upgrading from a self-contained release, uninstall the old release first to remove its bundled runtime files.

## Build

With the .NET SDK and Inno Setup 6 installed, run:

```powershell
.\publish-all.ps1
```

This publishes framework-dependent x64, x86 and ARM64 binaries to `publish\<runtime>` and installers to `publish\installers`. Each selected runtime's publish folder is deleted and rebuilt. Do not store personal files there. Set `INNO_SETUP_COMPILER` if `ISCC.exe` is not on PATH or in a standard location.

For a specific architecture, use `.\publish-all.ps1 -RuntimeIdentifiers win-x64`. To build without an installer, run `dotnet build src\DropIn.slnx --configuration Release`.

The Inno Setup packages are traditional desktop installers, not MSIX packages. Microsoft Store submission requires separate packaging, signing and certification. The privileged watcher service and machine-wide file association require a Store policy/packaging review.

## Data recovery

Uninstalling DropIn preserves the per-user data-directory selection and data. If the selected folder or its configuration is missing, the Installer and Packager offer a choice of another folder or the default. This does not restore missing data. Reconnect its drive first to retain access. The watcher service fails to start rather than recreate missing data. After changing folders, use the Installer's registration action to update the service.

## Tests

Run `dotnet run --project tests\DropIn.ConfigChecks\DropIn.ConfigChecks.csproj --configuration Release` to check missing and invalid data-directory recovery without modifying the user's settings.
