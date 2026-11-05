namespace DropIn.Packager;

sealed partial class MainForm
{
    /// <summary>Sets the window title, icon, size limits, and initial size from the configuration.</summary>
    void ConfigureWindow()
    {
        Text = PackagerConstants.AppTitle;
        using (var iconStream = typeof(MainForm).Assembly.GetManifestResourceStream(PackagerConstants.IconResourceName))
            if (iconStream != null) Icon = new Icon(iconStream, SystemInformation.SmallIconSize);
        var workArea = Screen.PrimaryScreen?.WorkingArea;
        Width = Cfg.Packager.WindowWidth >= PackagerConstants.MinWindowWidth
            ? Math.Min(Cfg.Packager.WindowWidth, workArea?.Width ?? PackagerConstants.FallbackScreenSize) : PackagerConstants.DefaultWindowWidth;
        Height = Cfg.Packager.WindowHeight >= PackagerConstants.MinWindowHeight
            ? Math.Min(Cfg.Packager.WindowHeight, workArea?.Height ?? PackagerConstants.FallbackScreenSize) : PackagerConstants.DefaultWindowHeight;
        MinimumSize = new Size(PackagerConstants.MinWindowWidth, PackagerConstants.MinWindowHeight);
        StartPosition = FormStartPosition.CenterScreen; // keeps the whole window, including the bottom row, on screen
    }

    /// <summary>Populates the source and package inputs and applies glyph images to the buttons.</summary>
    void ConfigureControls()
    {
        _source.Items.AddRange(new object[] { PackagerConstants.WingetSource, PackagerConstants.ChocolateySource });
        _source.SelectedIndex = Cfg.Is(Cfg.Packager.LastSource, PackagerConstants.ChocolateySource) ? 1 : 0;
        // A plain drop-down of recent packages; the COM-based TextBox autocomplete throws "Interface not registered" on some systems.
        new ToolTip().SetToolTip(_package, "Package name, id, or install command");
        if (Cfg.Packager.LastInput.Length > 0) { _package.Items.Add(Cfg.Packager.LastInput); _package.Text = Cfg.Packager.LastInput; }

        _fetch.Image = GlyphImage(PackagerConstants.FetchGlyph);
        _search.Image = GlyphImage(PackagerConstants.SearchGlyph);
        _create.Image = GlyphImage(PackagerConstants.CreateGlyph);
        _cancelImage = GlyphImage(PackagerConstants.CancelGlyph);
        _updateDatabase.Image = GlyphImage(PackagerConstants.UpdateDatabaseGlyph);
        foreach (var b in new[] { _search, _fetch, _create })
        {
            b.ImageAlign = ContentAlignment.MiddleLeft;
            b.TextImageRelation = TextImageRelation.ImageBeforeText;
        }
        _browse.Text = "…";
    }

    /// <summary>Builds the menu strip with the Database menu.</summary>
    MenuStrip BuildMenu()
    {
        var menu = new MenuStrip();
        var databaseMenu = new ToolStripMenuItem("Database") { Image = GlyphImage(PackagerConstants.DatabaseMenuGlyph) };
        _updateDatabase.Click += async (_, _) => await UpdateDatabaseAsync();
        databaseMenu.DropDownItems.Add(_updateDatabase);
        menu.Items.Add(databaseMenu);
        MainMenuStrip = menu;
        return menu;
    }

    /// <summary>Creates a four-column table whose second column stretches.</summary>
    static TableLayoutPanel FourColumnTable(DockStyle dock)
    {
        var table = new TableLayoutPanel { Dock = dock, AutoSize = true, ColumnCount = 4, Padding = new Padding(8) };
        table.ColumnStyles.Add(new ColumnStyle());
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle());
        table.ColumnStyles.Add(new ColumnStyle());
        return table;
    }

    /// <summary>Builds the top panel with the package, version, filter, installer, and media inputs.</summary>
    TableLayoutPanel BuildTopPanel()
    {
        var top = FourColumnTable(DockStyle.Top);
        top.Controls.Add(_source, 0, 0);
        top.Controls.Add(_package, 1, 0);
        top.Controls.Add(_search, 2, 0);
        top.Controls.Add(_fetch, 3, 0);
        top.Controls.Add(_resultsLabel, 0, 1);
        top.Controls.Add(_searchResults, 1, 1);
        top.SetColumnSpan(_searchResults, 3);
        _resultsLabel.Visible = _searchResults.Visible = false;
        top.Controls.Add(Label("Version"), 0, 2);
        var vp = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        vp.Controls.Add(_version);
        top.Controls.Add(vp, 1, 2);
        top.Controls.Add(Label("Options"), 0, 3);
        top.Controls.Add(_filters, 1, 3);
        top.SetColumnSpan(_filters, 3);
        top.Controls.Add(Label("Installer"), 0, 4);
        top.Controls.Add(_installer, 1, 4);
        top.SetColumnSpan(_installer, 3);
        top.Controls.Add(Label("Icon (path or URL)"), 0, 5);
        top.Controls.Add(_iconInput, 1, 5);
        top.SetColumnSpan(_iconInput, 3);
        top.Controls.Add(Label("Screenshots (paths or URLs, separated by ;)"), 0, 6);
        top.Controls.Add(_screenshotInput, 1, 6);
        top.SetColumnSpan(_screenshotInput, 3);
        return top;
    }

    /// <summary>Adds the key and value columns to the property grid.</summary>
    void ConfigureGrid()
    {
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Key", HeaderText = "Manifest property", FillWeight = 35 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Value", HeaderText = "Value (editable)", FillWeight = 65 });
        _grid.Columns["Key"]!.ReadOnly = true;
    }

    /// <summary>Builds the bottom panel with the output folder, Create button, and progress indicators.</summary>
    TableLayoutPanel BuildBottomPanel()
    {
        var bottom = FourColumnTable(DockStyle.Bottom);
        bottom.Controls.Add(Label("Output folder"), 0, 0);
        bottom.Controls.Add(_outDir, 1, 0);
        bottom.Controls.Add(_browse, 2, 0);
        bottom.Controls.Add(_create, 3, 0);
        // Fixed, equal sizes keep the row compact; Dock.Fill with AutoSize made both buttons grow tall.
        _outDir.Dock = DockStyle.None;
        _outDir.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _browse.AutoSize = _create.AutoSize = false;
        _browse.Size = new Size(PackagerConstants.BrowseButtonWidth, PackagerConstants.BottomButtonHeight);
        _create.Size = new Size(PackagerConstants.CreateButtonWidth, PackagerConstants.BottomButtonHeight);
        _browse.Anchor = _create.Anchor = AnchorStyles.None;
        _browse.Margin = _create.Margin = new Padding(3);
        bottom.Controls.Add(_progress, 0, 1);
        bottom.SetColumnSpan(_progress, 3);
        bottom.Controls.Add(_downloadStatus, 3, 1);
        return bottom;
    }

    /// <summary>Builds the panel hosting the log text box.</summary>
    Panel BuildLogHost()
    {
        _log.Height = PackagerConstants.LogHeight;
        var logHost = new Panel { Dock = DockStyle.Bottom, Height = PackagerConstants.LogHostHeight, Padding = new Padding(8, 0, 8, 8) };
        logHost.Controls.Add(_log);
        return logHost;
    }

    /// <summary>Creates a consistently styled left-aligned field label.</summary>
    static Label Label(string text) => new() { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 8, 3) };

    /// <summary>Renders a Segoe MDL2 Assets glyph as a small image for buttons and menus.</summary>
    static Image GlyphImage(string glyph)
    {
        var size = PackagerConstants.GlyphImageSize;
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        using var font = new Font(PackagerConstants.GlyphFontName, PackagerConstants.GlyphFontPixelSize, GraphicsUnit.Pixel);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(glyph, font, SystemBrushes.ControlText, new RectangleF(0, 0, size, size), format);
        return bmp;
    }
}
