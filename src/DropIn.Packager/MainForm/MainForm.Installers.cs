using DropIn.Common;

namespace DropIn.Packager;

sealed partial class MainForm
{
    /// <summary>Filters shown only when the manifest offers at least one value for them.</summary>
    static readonly (string Key, string Label)[] FilterDefs =
    {
        (PackagerConstants.ArchitectureKey, "Architecture"),
        (InstallerOption.KindKey, "Package kind"),
        (PackagerConstants.InstallerTypeKey, "Installer type"),
        (PackagerConstants.ScopeKey, "Scope"),
        (PackagerConstants.LocaleKey, "Locale"),
    };

    /// <summary>Builds one filter combo per manifest field that actually varies between installers.</summary>
    void BuildFilters()
    {
        _filters.Controls.Clear();
        _filterBoxes.Clear();
        var info = _info!;
        var hinted = InstallerHints.Matching(info.Installers, _hint);
        // Honor parsed command hints when they match any option; otherwise fall back to all available installers.
        var preferred = PickDefault(hinted.Count > 0 ? hinted : info.Installers);
        var wanted = new Dictionary<string, string?>
        {
            [PackagerConstants.ArchitectureKey] = _hint?.Architecture ?? preferred.Get(PackagerConstants.ArchitectureKey),
            [PackagerConstants.InstallerTypeKey] = _hint?.InstallerType ?? preferred.Get(PackagerConstants.InstallerTypeKey),
            [PackagerConstants.ScopeKey] = _hint?.Scope ?? preferred.Get(PackagerConstants.ScopeKey),
            [PackagerConstants.LocaleKey] = _hint?.Locale ?? preferred.Get(PackagerConstants.LocaleKey),
            [InstallerOption.KindKey] = preferred.Get(InstallerOption.KindKey),
        };
        foreach (var (key, label) in FilterDefs)
        {
            var values = info.Installers.Select(i => i.Get(key)).Where(v => v.Length > 0).Distinct().ToList();
            if (values.Count == 0) continue;
            var cb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
            cb.Items.Add(PackagerConstants.AnyFilterLabel);
            cb.Items.AddRange(values.Cast<object>().ToArray());
            var w = wanted.GetValueOrDefault(key);
            cb.SelectedItem = w != null && values.Contains(w, StringComparer.OrdinalIgnoreCase)
                ? values.First(v => v.Equals(w, StringComparison.OrdinalIgnoreCase)) : PackagerConstants.AnyFilterLabel;
            cb.SelectedIndexChanged += (_, _) => ApplyFilters();
            _filterBoxes[key] = cb;
            _filters.Controls.Add(Label(label));
            _filters.Controls.Add(cb);
        }
        ApplyFilters();
    }

    /// <summary>Applies selected filter values and refreshes the installer-option list.</summary>
    void ApplyFilters()
    {
        if (_info == null) return;
        IEnumerable<InstallerOption> q = _info.Installers;
        foreach (var (key, cb) in _filterBoxes)
            if (cb.SelectedItem is string v && v != PackagerConstants.AnyFilterLabel) q = q.Where(i => i.Get(key) == v);
        _visible = q.ToList();
        _installer.Items.Clear();
        _installer.Items.AddRange(_visible.Select(o => (object)o.Label).ToArray());
        if (_visible.Count > 0) _installer.SelectedIndex = _visible.IndexOf(PickDefault(_visible));
        else { _current = null; _grid.Rows.Clear(); _create.Enabled = false; }
    }

    /// <summary>Prefers x64, then neutral, then anything; installers over portable packages.</summary>
    public static InstallerOption PickDefault(IList<InstallerOption> options) => Defaults.Pick(options);

    /// <summary>Displays the properties of the currently selected installer option.</summary>
    void SelectInstaller()
    {
        if (_installer.SelectedIndex < 0 || _installer.SelectedIndex >= _visible.Count) return;
        _current = _visible[_installer.SelectedIndex];
        _grid.Rows.Clear();
        if (_info != null)
            foreach (var kv in _info.Metadata) AddRow(PackagerConstants.ManifestRowPrefix + kv.Key, kv.Value, true);
        foreach (var kv in _current.Properties) AddRow(kv.Key, kv.Value, false);
        _create.Enabled = true;
        if (!_fetching) CheckTraces();
    }

    /// <summary>Warns once per installer type when the selected installer leaves registry entries or caches behind.</summary>
    void CheckTraces()
    {
        if (!Cfg.Packager.WarnInstallerTraces || _current == null) return;
        if (InstallTraces.Predict(_current, out var type) is not { } warning || !_askedTraces.Add(type)) return;
        if (MessageBox.Show(this, warning + "\n\nUse this installer anyway?", PackagerConstants.AppTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            _acceptedTraces.Add(type);
        else
            Log("Choose another installer type to avoid these registry and cache traces.");
    }

    /// <summary>Adds a property row and optionally marks it as read-only.</summary>
    void AddRow(string key, string value, bool readOnly)
    {
        var i = _grid.Rows.Add(key, value);
        if (!readOnly) return;
        _grid.Rows[i].ReadOnly = true;
        _grid.Rows[i].DefaultCellStyle.ForeColor = SystemColors.GrayText;
    }

    /// <summary>Copies edited values back into the selected installer option.</summary>
    void CommitGrid()
    {
        if (_current == null) return;
        var props = new Dictionary<string, string>();
        foreach (DataGridViewRow r in _grid.Rows)
        {
            if (r.IsNewRow || r.ReadOnly) continue;
            var k = r.Cells[0].Value?.ToString()?.Trim();
            if (string.IsNullOrEmpty(k)) continue;
            props[k] = r.Cells[1].Value?.ToString() ?? "";
        }
        _current.Properties.Clear();
        foreach (var kv in props) _current.Properties[kv.Key] = kv.Value;
    }
}
