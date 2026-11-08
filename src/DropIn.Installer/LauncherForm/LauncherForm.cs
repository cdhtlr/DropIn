using DropIn.Common;

namespace DropIn.Installer;

/// <summary>Main window shown when the installer is started manually; it stays open until the user closes it.</summary>
sealed partial class LauncherForm : Form
{
    /// <summary>The configuration used by all actions in the launcher.</summary>
    DropInConfig _cfg;
    readonly LinkLabel _installDirectoryLink = new() { AutoEllipsis = true, Dock = DockStyle.Fill };
    /// <summary>The menu that groups handler and service setup commands.</summary>
    readonly ToolStripMenuItem _setupMenu = new("Setup");
    /// <summary>The command that registers package handling and installs the watcher service.</summary>
    readonly ToolStripMenuItem _registerItem = new("Register handler and install service");
    /// <summary>The command that removes package handling and the watcher service.</summary>
    readonly ToolStripMenuItem _unregisterItem = new("Unregister handler and uninstall service");
    /// <summary>The button that opens the package picker.</summary>
    readonly Button _installButton = new() { Text = "Install package or installer", Width = 352, Height = 48, Anchor = AnchorStyles.Top };
    /// <summary>The tool-tip provider for launcher controls.</summary>
    readonly ToolTip _tips = new();
    /// <summary>The controls that accept dragged package files.</summary>
    readonly Control[] _dropTargets;
    /// <summary>The hint shown when drag-and-drop installation is available.</summary>
    readonly Label _dropHint = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = SystemColors.GrayText };
    /// <summary>The number of installations currently in progress.</summary>
    int _running;

    /// <summary>Creates the launcher and wires its setup, browse, and drag-and-drop actions.</summary>
    public LauncherForm(DropInConfig cfg, IEnumerable<string> initialFiles, bool browseOnStart)
    {
        _cfg = cfg;
        Program.EnableOverwritePrompt();
        Text = "DropIn Installer";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(440, 190);
        using (var s = typeof(LauncherForm).Assembly.GetManifestResourceStream("app_icon.ico"))
            if (s != null) Icon = new Icon(s, SystemInformation.SmallIconSize);

        var menu = new MenuStrip { Dock = DockStyle.Top, ShowItemToolTips = true };
        _setupMenu.Image = GlyphImage("\uE713");
        menu.Items.Add(_setupMenu);
        _registerItem.Image = GlyphImage("\uE72E");
        _unregisterItem.Image = GlyphImage("\uE711");
        _setupMenu.ToolTipText = "Register or unregister DropIn's .app handler and install service.";
        _registerItem.ToolTipText = "Associate .app files with DropIn and install the folder watcher service.";
        _unregisterItem.ToolTipText = "Remove DropIn's .app association and uninstall the folder watcher service.";
        _setupMenu.DropDownItems.AddRange(new ToolStripItem[] { _registerItem, _unregisterItem });
        MainMenuStrip = menu;

        _installButton.Image = GlyphImage("\uE896", 24);
        _installButton.ImageAlign = ContentAlignment.MiddleLeft;
        _installButton.TextImageRelation = TextImageRelation.ImageBeforeText;
        _tips.SetToolTip(_installButton, "Browse for .app packages or installers (.exe, .msi, .msix, .msixbundle, .appx, .appxbundle, .zip) and start their installations.");

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(16) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.Controls.Add(_installButton, 0, 0);
        layout.Controls.Add(_dropHint, 0, 1);
        layout.Controls.Add(_installDirectoryLink, 0, 2);
        _installDirectoryLink.LinkClicked += (_, _) => OpenInstallDirectory();
        UpdateInstallDirectoryLink();
        Controls.Add(layout);
        Controls.Add(menu);
        _dropTargets = AllChildControls(this).ToArray();
        foreach (var c in _dropTargets)
        {
            c.DragEnter += (_, e) => OnDragEnter(e);
            c.DragOver += (_, e) => OnDragOver(e);
            c.DragDrop += (_, e) => OnDragDrop(e);
        }
        RefreshActions();

        _registerItem.Click += (_, _) => Setup(InstallerConstants.RegisterSwitch);
        _unregisterItem.Click += (_, _) => Setup(InstallerConstants.UnregisterSwitch);
        _installButton.Click += (_, _) => Browse();

        var files = initialFiles.ToList();
        // Perform first-run registration only after the form has been shown, then handle launch-time files.
        Shown += (_, _) =>
        {
            // Every start registers the handler and installs the service when either is missing (usually shows a UAC prompt).
            // The same setup also grants write access to the install folder when the current user lacks it.
            if ((!DropIn.Installer.Setup.IsHandlerRegistered() || !DropIn.Installer.Setup.IsServiceInstalled()
                || !DropIn.Installer.Setup.IsServiceConfigured(_cfg.BaseDirectory) || Program.NeedsInstallAccess(_cfg))
                && !Setup(InstallerConstants.RegisterSwitch))
                return;

            RefreshActions();
            foreach (var f in files) Install(f);
            if (browseOnStart) Browse();
        };
    }

}
