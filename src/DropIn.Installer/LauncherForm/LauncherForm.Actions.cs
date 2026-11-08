using DropIn.Common;

namespace DropIn.Installer;

sealed partial class LauncherForm
{
    /// <summary>Renders a Segoe MDL2 glyph into an image for a menu or button.</summary>
    static Image GlyphImage(string glyph, int size = 16)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        using var font = new Font("Segoe MDL2 Assets", size * 0.72f, GraphicsUnit.Pixel);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(glyph, font, SystemBrushes.ControlText, new RectangleF(0, 0, size, size), format);
        return bmp;
    }

    /// <summary>Runs a setup command and refreshes the available launcher actions afterward.</summary>
    bool Setup(string command)
    {
        if (command == InstallerConstants.RegisterSwitch)
        {
            if (_running > 0)
            {
                MessageBox.Show(this, "Wait for the current installations to finish before changing registration.", "DropIn");
                return false;
            }
            if (!Program.SelectDataDirectory(ref _cfg, this)) return false;
            UpdateInstallDirectoryLink();
        }
        Enabled = false;
        try { return Program.Command(new[] { command }, _cfg) == 0; }
        finally
        {
            RefreshActions();
            Enabled = true;
        }
    }

    void UpdateInstallDirectoryLink()
    {
        _installDirectoryLink.Text = "Install directory: " + _cfg.Installer.InstallDirectory;
        _tips.SetToolTip(_installDirectoryLink, _cfg.Installer.InstallDirectory);
    }

    void OpenInstallDirectory()
    {
        try
        {
            _cfg.EnsureDataAvailable();
            Directory.CreateDirectory(_cfg.Installer.InstallDirectory);
            Program.OpenInstallDirectory(_cfg.Installer.InstallDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            MessageBox.Show(this, "Could not open the install directory: " + ex.Message, "DropIn",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>Updates setup commands, install controls, and drag-and-drop availability.</summary>
    void RefreshActions()
    {
        var registered = DropIn.Installer.Setup.IsHandlerRegistered();
        var serviceReady = DropIn.Installer.Setup.IsServiceInstalled() && DropIn.Installer.Setup.IsServiceConfigured(_cfg.BaseDirectory);
        _registerItem.Visible = !registered || !serviceReady;
        _unregisterItem.Visible = registered;
        _installButton.Visible = registered;
        _dropEnabled = registered && serviceReady;
        AllowDrop = _dropEnabled;
        // Keep drag support synchronized across the form and every nested control that receives mouse events.
        foreach (var c in _dropTargets) c.AllowDrop = _dropEnabled;
        _dropHint.Text = DropIn.Installer.Setup.IsAdmin()
            // Windows blocks drags from a normal Explorer into an elevated window, which shows the "no drop" cursor.
            ? "Drag and drop is blocked while DropIn runs as administrator. Restart it normally."
            : _dropEnabled ? "Or drag .app packages or installers (.exe, .msi, .msix, .msixbundle, .appx, .appxbundle, .zip) onto this window." : "";
    }

    /// <summary>Whether drag-and-drop installation is currently available; cached so drag events stay fast.</summary>
    bool _dropEnabled;

    /// <summary>Gets whether package dragging is enabled by both handler registration and service installation.</summary>
    bool DropEnabled => _dropEnabled;

    /// <summary>Enumerates every descendant control, so each one (buttons, labels, panels) can accept drops instead of showing the "no drop" cursor.</summary>
    static IEnumerable<Control> AllChildControls(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var nested in AllChildControls(child)) yield return nested;
        }
    }

    /// <summary>Whether the file is an .app package or a raw installer/archive that DropIn can install.</summary>
    static bool IsInstallable(string file) =>
        file.EndsWith(AppManifest.Extension, StringComparison.OrdinalIgnoreCase)
        || InstallerConstants.RawInstallerExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns only installable file paths (.app packages and raw installers) from the current drag-and-drop payload.</summary>
    static string[] DroppedPackages(DragEventArgs e) =>
        e.Data?.GetData(DataFormats.FileDrop) is string[] files
            ? files.Where(f => File.Exists(f) && IsInstallable(f)).ToArray()
            : Array.Empty<string>();

    /// <summary>Sets the drag effect according to whether the payload contains supported packages.</summary>
    protected override void OnDragEnter(DragEventArgs drgevent)
    {
        drgevent.Effect = DropEnabled && DroppedPackages(drgevent).Length > 0 ? DragDropEffects.Copy : DragDropEffects.None;
        base.OnDragEnter(drgevent);
    }

    /// <summary>Keeps the drag effect up to date while the cursor moves over any control.</summary>
    protected override void OnDragOver(DragEventArgs drgevent)
    {
        drgevent.Effect = DropEnabled && DroppedPackages(drgevent).Length > 0 ? DragDropEffects.Copy : DragDropEffects.None;
        base.OnDragOver(drgevent);
    }

    /// <summary>Queues each supported package delivered by a drag-and-drop operation.</summary>
    protected override void OnDragDrop(DragEventArgs drgevent)
    {
        base.OnDragDrop(drgevent);
        if (!DropEnabled) return;
        foreach (var f in DroppedPackages(drgevent)) Install(f);
    }

    /// <summary>Opens a multi-select dialog and queues the chosen package files.</summary>
    void Browse()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Install package or installer",
            Filter = "Packages and installers|*.app;*.exe;*.msi;*.msix;*.msixbundle;*.appx;*.appxbundle;*.zip|DropIn packages (*.app)|*.app|All files (*.*)|*.*",
            Multiselect = true,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        foreach (var file in dialog.FileNames) Install(file);
    }

    /// <summary>Starts an installation in its own window and returns immediately so more packages can be queued.</summary>
    void Install(string file)
    {
        if (!File.Exists(file)) { MessageBox.Show(this, "File not found: " + file, "DropIn"); return; }
        var isPackage = file.EndsWith(AppManifest.Extension, StringComparison.OrdinalIgnoreCase);
        try
        {
            _cfg.EnsureDataAvailable();
            Directory.CreateDirectory(_cfg.Installer.InstallDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            MessageBox.Show(this, "Could not access the installation directory " + _cfg.Installer.InstallDirectory + ": " + ex.Message,
                "DropIn", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        PackageMedia? media = null;
        var title = Path.GetFileName(file);
        if (isPackage)
        {
            try
            {
                media = AppManifest.ReadMedia(file);
                var m = AppManifest.ReadFrom(file);
                if (!string.IsNullOrWhiteSpace(m.Name)) title = m.Name;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                MessageBox.Show(this, "Cannot read package " + title + ": " + ex.Message, "DropIn");
                return;
            }
        }
        ProgressForm.Start(title, media,
            (hide, progress) => isPackage ? InstallRunner.InstallPackage(file, _cfg, hide, progress) : InstallRunner.InstallFile(file, _cfg, null, progress),
            () => _running++, () => _running--,
            result =>
            {
                if (result.Success) Program.ShowInstalled(title, result.InstallDirectory, result.Portable);
            });
    }

    /// <summary>Asks for confirmation before closing while installations are still running.</summary>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_running > 0 && e.CloseReason == CloseReason.UserClosing &&
            MessageBox.Show(this, "Installations are still running. Close anyway?", "DropIn",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            e.Cancel = true;
        base.OnFormClosing(e);
    }
}
