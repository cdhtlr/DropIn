# DropIn usage guide

For an overview and demos, see the [README](../README.md).

## How it works

1. The Packager searches for a package, downloads its installer (checksum verified) and writes `<id>.app` (a ZIP with `manifest.json`, icon and screenshots).
2. The Installer installs the `.app` or a raw installer silently, then opens the result folder or offers to run its `.exe`.

Supported files: `.app`, `.exe`, `.msi`, `.msix`, `.msixbundle`, `.appx`, `.appxbundle`, `.zip`.

## Guides

- [Programs](PROGRAMS.md): Packager and Installer usage.
- [Building and installation](BUILDING.md): requirements, setup, data recovery, and tests.
- [Command-line reference](CLI.md): Packager and Installer commands.
- [Configuration](CONFIGURATION.md): data folders and all configuration options.
