# Programs

## Packager

1. Select winget or Chocolatey and search for a package or paste an install command.
2. Choose an installer option and edit its properties if needed.
3. Review the icon and screenshots, then create the `.app` package.

The media database downloads on first launch and can be refreshed from the **Database** menu. Custom entries in `Database\custom-database.json` take priority and are not overwritten.
Media can also be supplied as local paths or URLs. Separate screenshot entries with `;`. Manifest filters and installer properties are editable in the window.

## Installer

Open an `.app`, drag a supported file onto the Installer, use its install button, or drop the file into the install directory. On first registration, select a data folder and approve the administrator prompt.

The Installer can run installers silently or fall back to interactive setup. Portable applications are extracted to their own folder and may receive a Start Menu shortcut.
The default package folder is `<InstallDirectory>\<Source>\<Name>`. Raw installers use source `other`. The watcher runs installers as the signed-in user and requests elevation when needed.

The **Setup** menu registers or unregisters the file handler and watcher. Registration chooses a data folder for applications, packages, configuration, database and logs. Cancelling the picker cancels registration. The install-directory link opens the application folder.

## More information

- [Building and installation](BUILDING.md): requirements, setup, data recovery, and tests.
- [Command-line reference](CLI.md): Packager and Installer commands.
- [Configuration](CONFIGURATION.md): data folders and settings.
- [Usage guide](USAGE.md): overview and supported file types.
