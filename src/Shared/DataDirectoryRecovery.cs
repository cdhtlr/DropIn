using DropIn.Common;

namespace DropIn;

internal static class DataDirectoryRecovery
{
    internal static DropInConfig? Prompt(DataDirectoryUnavailableException error)
    {
        var choice = MessageBox.Show(
            error.Message +
            "\n\nReconnect the drive if necessary. Choose Yes to select an existing data folder, " +
            "or No to use the default data folder. Cancel leaves the saved location unchanged." +
            "\n\nUsing another folder does not recover the missing folder's data.",
            "DropIn", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
        if (choice == DialogResult.Cancel) return null;

        string directory;
        if (choice == DialogResult.Yes)
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Select an existing DropIn data folder, or an empty folder to start with new settings.",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true
            };
            if (dialog.ShowDialog() != DialogResult.OK) return null;
            directory = dialog.SelectedPath;
        }
        else
        {
            directory = DropInConfig.DefaultDataDirectory;
            Directory.CreateDirectory(directory);
        }
        return DropInConfig.RecoverDataDirectory(directory);
    }
}
