using DropIn.Common;

namespace DropIn.Packager;

sealed partial class MainForm
{
    /// <summary>Refreshes the UniGetUI media database and records the update time.</summary>
    async Task UpdateDatabaseAsync()
    {
        _updateDatabase.Enabled = false;
        try
        {
            Cfg.EnsureDataAvailable();
            await UniGetUiDatabase.UpdateAsync();
            Log("Updated the UniGetUI icon and screenshot database.");
            try
            {
                var fresh = DropInConfig.LoadSelected();
                fresh.Packager.LastDatabaseUpdate = DateTime.Now;
                fresh.Save(fresh.BaseDirectory);
                Cfg.Packager.LastDatabaseUpdate = fresh.Packager.LastDatabaseUpdate;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                Log("Could not save the database update time: " + ex.Message);
                MessageBox.Show(this, "The database was updated, but its update time could not be saved: " + ex.Message,
                    PackagerConstants.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or TaskCanceledException)
        {
            Log("Could not update the UniGetUI database: " + ex.Message);
            MessageBox.Show(this, "Could not update the UniGetUI database.\n\n" + ex.Message, PackagerConstants.AppTitle,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { _updateDatabase.Enabled = true; }
    }

    /// <summary>Builds the .app package from the selected manifest, installer option, and media.</summary>
    Task CreateAsync() => RunAsync(_create, async ct =>
    {
        if (_info == null || _current == null) return;
        var denied = Elevation.DeniedFolders(Cfg.PackagerWriteDirectories(_outDir.Text.Trim()));
        if (denied.Count > 0)
        {
            var folders = string.Join(", ", denied);
            if (MessageBox.Show(this, Elevation.Explain(denied) + "\n\nRestart as administrator?", PackagerConstants.AppTitle,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                Log("Canceled: administrator rights are required to write to " + folders + ".");
            else if (Elevation.RestartElevated(null, out var failure) != null)
                Close();
            else
            {
                Log("Canceled: cannot write to " + folders + ": " + failure);
                MessageBox.Show(this, "Cannot write to " + folders + ": " + failure, PackagerConstants.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            return;
        }
        _grid.EndEdit();
        CommitGrid();
        var selectedMedia = await SelectedMediaAsync(ct);
        PackageBuilder.IconExtractor = path => ChoosePackageIcon(path);
        PackageBuilder.ConfirmInstallerTraces = Cfg.Packager.WarnInstallerTraces
            ? (type, warning) => _acceptedTraces.Contains(type) || MessageBox.Show(this, warning + "\n\nCreate the package anyway?", PackagerConstants.AppTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes
            : null;
        var app = await PackageBuilder.BuildAsync(_info, _current, _outDir.Text, selectedMedia,
            new Progress<string>(Log), ct, new Progress<DownloadProgress>(ReportDownload));
        Log("Done: " + app);
    });

    /// <summary>Offers the user the default DropIn icon or the installer executable's icon.</summary>
    byte[]? ChoosePackageIcon(string path)
    {
        using var dialog = new Form
        {
            Text = "Package icon",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(440, 112)
        };
        dialog.Controls.Add(new Label
        {
            Text = "No package icon was found. Choose which icon to use:",
            AutoSize = true,
            Location = new Point(16, 16)
        });
        var useDefault = new Button
        {
            Text = "Use default DropIn icon",
            DialogResult = DialogResult.No,
            Size = new Size(170, 30),
            Location = new Point(82, 64)
        };
        var extract = new Button
        {
            Text = "Extract from installer EXE",
            DialogResult = DialogResult.Yes,
            Size = new Size(180, 30),
            Location = new Point(258, 64)
        };
        dialog.Controls.Add(useDefault);
        dialog.Controls.Add(extract);
        dialog.AcceptButton = useDefault;
        dialog.CancelButton = useDefault;
        return dialog.ShowDialog(this) == DialogResult.Yes ? ExeIconExtractor.ExtractPng(path) : null;
    }

    /// <summary>Shows download progress as a percentage, or as received kilobytes when the total is unknown.</summary>
    void ReportDownload(DownloadProgress p)
    {
        if (p.Percentage is { } percentage)
        {
            _progress.Style = ProgressBarStyle.Continuous;
            _progress.Value = Math.Max(_progress.Value, percentage);
            _downloadStatus.Text = $"{_progress.Value}%";
        }
        else
        {
            _progress.Style = ProgressBarStyle.Marquee;
            _downloadStatus.Text = $"{p.BytesReceived / 1024:N0} KB";
        }
    }
}
