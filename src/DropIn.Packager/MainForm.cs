using DropIn.Common;

namespace DropIn.Packager;

/// <summary>Provides the graphical interface for searching, configuring, and packaging applications.</summary>
sealed partial class MainForm : Form
{
    /// <summary>The persisted configuration shared by the packager window.</summary>
    static readonly DropInConfig Cfg = DropInConfig.LoadOrCreate(AppContext.BaseDirectory);
    /// <summary>Gets the configured output directory used for newly created packages.</summary>
    public static string DefaultOutputDirectory => Cfg.ResolveOutputDirectory();
    /// <summary>The preferred installer-option settings loaded from the shared configuration.</summary>
    static readonly PackagerDefaults Defaults = PackagerDefaults.LoadOrCreate();

    /// <summary>The package-manager selector.</summary>
    readonly ComboBox _source = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    /// <summary>The input for a package name, identifier, or install command.</summary>
    readonly ComboBox _package = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown, AutoCompleteMode = AutoCompleteMode.None };
    /// <summary>The button that searches for matching packages.</summary>
    readonly Button _search = new() { Text = "Search", AutoSize = true };
    /// <summary>The button that retrieves the selected package manifest.</summary>
    readonly Button _fetch = new() { Text = "Fetch", AutoSize = true };
    /// <summary>The label displayed beside available search results.</summary>
    readonly Label _resultsLabel = new() { Text = "Search results", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 8, 3) };
    /// <summary>The list of packages returned by search.</summary>
    readonly ComboBox _searchResults = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    /// <summary>The package-version selector.</summary>
    readonly ComboBox _version = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    /// <summary>The container for installer-option filters.</summary>
    readonly FlowLayoutPanel _filters = new() { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
    /// <summary>The filter controls keyed by their manifest property names.</summary>
    readonly Dictionary<string, ComboBox> _filterBoxes = new();
    /// <summary>The selector for the installer option that will be packaged.</summary>
    readonly ComboBox _installer = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    /// <summary>The editable grid displaying manifest and selected-installer properties.</summary>
    readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill, AllowUserToAddRows = true, AllowUserToDeleteRows = true, RowHeadersVisible = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = SystemColors.Window,
    };
    /// <summary>The icon path or URL selected for package media.</summary>
    readonly TextBox _iconInput = new() { Dock = DockStyle.Fill };
    /// <summary>The semicolon-separated screenshot paths or URLs selected for package media.</summary>
    readonly TextBox _screenshotInput = new() { Dock = DockStyle.Fill, ScrollBars = ScrollBars.Horizontal };
    /// <summary>The destination directory for the generated package.</summary>
    readonly TextBox _outDir = new() { Dock = DockStyle.Fill, Text = DefaultOutputDirectory };
    /// <summary>The button that selects an output directory.</summary>
    readonly Button _browse = new() { Text = "...", Width = 32 };
    /// <summary>The button that builds the package from the selected manifest and installer.</summary>
    readonly Button _create = new() { Text = "Create .app", AutoSize = true, Enabled = false };
    /// <summary>The read-only activity and error log.</summary>
    readonly TextBox _log = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    /// <summary>The activity and download progress indicator.</summary>
    readonly ProgressBar _progress = new() { Dock = DockStyle.Fill, Style = ProgressBarStyle.Marquee, Visible = false, MarqueeAnimationSpeed = 30 };
    /// <summary>The textual download progress shown beside the progress bar.</summary>
    readonly Label _downloadStatus = new() { AutoSize = true, Anchor = AnchorStyles.Right, TextAlign = ContentAlignment.MiddleRight };
    /// <summary>The command that refreshes the UniGetUI media database.</summary>
    readonly ToolStripMenuItem _updateDatabase = new("Update icon and screenshot database");

    /// <summary>The currently loaded package manifest.</summary>
    ManifestInfo? _info;
    /// <summary>The media currently loaded or found for the package.</summary>
    PackageMedia? _media;
    /// <summary>The installer options matching the active filters.</summary>
    List<InstallerOption> _visible = new();
    /// <summary>The installer option currently selected for package creation.</summary>
    InstallerOption? _current;
    /// <summary>Whether manifest selections are being populated programmatically.</summary>
    bool _loading;
    /// <summary>Installer types whose registry/cache warning was already shown or accepted for the fetched package.</summary>
    readonly HashSet<string> _askedTraces = new(), _acceptedTraces = new();
    bool _fetching;
    /// <summary>The parsed command hints used to select package source, version, and installer properties.</summary>
    ParsedCommand? _hint;

    /// <summary>Initializes the packager window, its controls, and its event handlers.</summary>
    public MainForm()
    {
        ConfigureWindow();
        ConfigureControls();
        var menu = BuildMenu();
        var top = BuildTopPanel();
        ConfigureGrid();
        var bottom = BuildBottomPanel();
        var logHost = BuildLogHost();

        Controls.Add(_grid);
        Controls.Add(logHost);
        Controls.Add(bottom);
        Controls.Add(top);
        Controls.Add(menu);

        WireEvents();
    }

    /// <summary>Gets the source identifier represented by the package-manager selector.</summary>
    string SourceName => _source.SelectedIndex == 0 ? PackagerConstants.WingetSource : PackagerConstants.ChocolateySource;

    /// <summary>Selects the package-manager entry matching a source identifier.</summary>
    void SelectSource(string source)
        => _source.SelectedIndex = source == PackagerConstants.WingetSource ? 0 : 1;

    /// <summary>Appends a message to the window log and application log.</summary>
    void Log(string s)
    {
        _log.AppendText(s + Environment.NewLine);
        AppLog.Info(s);
    }

    /// <summary>Persists the window size, last source and recent inputs without touching the other settings.</summary>
    void SaveState()
    {
        try
        {
            var fresh = DropInConfig.LoadOrCreate(AppContext.BaseDirectory);
            fresh.Packager.LastSource = SourceName;
            fresh.Packager.LastInput = Cfg.Packager.LastInput;
            if (WindowState == FormWindowState.Normal) { fresh.Packager.WindowWidth = Width; fresh.Packager.WindowHeight = Height; }
            fresh.Save(AppContext.BaseDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Remembers the last input in the config and lists every input of this session in the dropdown (memory only).</summary>
    void Remember(string input)
    {
        input = input.Trim();
        if (input.Length == 0) return;
        Cfg.Packager.LastInput = input;
        var text = _package.Text;
        _package.Items.Remove(input);
        _package.Items.Insert(0, input);
        _package.Text = text;
    }
}
