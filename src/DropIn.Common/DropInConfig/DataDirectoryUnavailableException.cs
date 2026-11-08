namespace DropIn.Common;

/// <summary>Indicates that the user's selected data directory cannot be found.</summary>
public sealed class DataDirectoryUnavailableException : IOException
{
    public string DirectoryPath { get; }

    public DataDirectoryUnavailableException(string directoryPath, bool configurationMissing = false)
        : base((configurationMissing ? "The DropIn configuration is missing from: " : "The selected DropIn data directory is unavailable: ") + directoryPath)
    {
        DirectoryPath = directoryPath;
    }
}
