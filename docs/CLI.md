# Command-line reference

## Packager

```text
DropIn.Packager -p "winget install --id Git.Git" -p "choco install 7zip"
DropIn.Packager -p winget Git.Git -p choco 7zip
DropIn.Packager -p 7zip -p putty --source chocolatey
DropIn.Packager <winget|choco> <package-id> [version] [-o dir]

# Unattended, custom output folder
DropIn.Packager -p Git.Git -y -o D:\Apps
```

| Option | Description |
|---|---|
| `-p`, `--package <command-or-id>` | Package to build. Also `--package <winget\|choco\|chocolatey> <id>` |
| `--source <winget\|choco\|chocolatey>` | Source for inputs without one (default `winget`) |
| `-o`, `--output <dir>` | Output folder |
| `-y`, `--yes` | Don't prompt: use the latest (or requested) version and the preferred installer option |
| `--interactive` | Prompt for version and installer option (default, unless input is redirected) |
| `--no-unigetui` | Skip the UniGetUI icon and screenshot database |
| `-h`, `--help` | Show usage (must be the first argument) |

Default preferences live in `Packager.PreferredInstaller`. See [Configuration](CONFIGURATION.md).

## Installer

| Command | Description |
|---|---|
| `DropIn.Installer <file> ...` | Install the given files |
| `--install [file ...]` | Browse for files, or install the given files |
| `--register` | Register the `.app` handler and the watcher service (needs administrator) |
| `--unregister` | Remove both (needs administrator) |
| `-h`, `--help` | Show usage (printed to the console, or in a message box without one) |
