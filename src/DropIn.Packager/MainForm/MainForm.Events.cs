using DropIn.Common;

namespace DropIn.Packager;

sealed partial class MainForm
{
    /// <summary>Subscribes the control, form, and lifetime event handlers.</summary>
    void WireEvents()
    {
        _fetch.Click += async (_, _) => await FetchAsync();
        _search.Click += async (_, _) => await SearchAsync();
        _source.SelectedIndexChanged += (_, _) => SetResults(null);
        _package.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await FetchAsync(); } };
        _version.SelectionChangeCommitted += async (_, _) => await LoadVersionAsync(_version.SelectedItem as string);
        _searchResults.SelectionChangeCommitted += async (_, _) =>
        {
            if (_searchResults.SelectedItem is not PackageSearchResult result) return;
            _package.Text = result.Id;
            await FetchAsync();
        };
        _installer.SelectedIndexChanged += (_, _) => SelectInstaller();
        _grid.CellEndEdit += (_, _) => CommitGrid();
        _grid.UserDeletedRow += (_, _) => CommitGrid();
        _browse.Click += (_, _) => BrowseOutputFolder();
        _create.Click += async (_, _) => await CreateAsync();
        FormClosed += (_, _) => { _info?.Dispose(); SaveState(); };
        Shown += async (_, _) => await OnShownAsync();
    }

    /// <summary>Lets the user pick an output folder, starting from the nearest existing ancestor of the current one.</summary>
    void BrowseOutputFolder()
    {
        var start = _outDir.Text.Trim();
        while (start.Length > 0 && !Directory.Exists(start)) start = Path.GetDirectoryName(start) ?? "";
        using var d = new FolderBrowserDialog { Description = "Output folder", UseDescriptionForTitle = true };
        if (start.Length > 0) d.InitialDirectory = start;
        if (d.ShowDialog(this) == DialogResult.OK) _outDir.Text = d.SelectedPath;
    }

    /// <summary>Ensures the UniGetUI database exists and refreshes it when the configured interval has elapsed.</summary>
    async Task OnShownAsync()
    {
        try
        {
            Cfg.EnsureDataAvailable();
            if (await UniGetUiDatabase.EnsureAvailableAsync())
                Log("Downloaded the UniGetUI icon and screenshot database.");
            else if (Cfg.Packager.AutoUpdateDatabase && Cfg.Packager.DatabaseUpdateIntervalDays > 0 &&
                     (Cfg.Packager.LastDatabaseUpdate == null || DateTime.Now - Cfg.Packager.LastDatabaseUpdate >= TimeSpan.FromDays(Cfg.Packager.DatabaseUpdateIntervalDays)))
                await UpdateDatabaseAsync();
        }
        catch (UnauthorizedAccessException) when (!Elevation.IsElevated())
        {
            // The application folder (for example C:\DropIn) is write-protected; ask Windows for administrator rights.
            if (MessageBox.Show(this, "DropIn needs administrator rights to save its database in the application folder.\nRestart as administrator?",
                    "DropIn Packager", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                MessageBox.Show(this, "The packager cannot run without administrator rights.", "DropIn Packager", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Close();
            }
            else if (Elevation.RestartElevated(null, out var failure) != null)
                Close();
            else
            {
                MessageBox.Show(this, "The packager cannot run: it cannot save its database: " + failure, "DropIn Packager", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Close();
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or InvalidDataException or TaskCanceledException)
        {
            Log("Could not load the UniGetUI database: " + ex.Message);
        }
    }
}
