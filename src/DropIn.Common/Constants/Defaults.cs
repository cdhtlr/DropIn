namespace DropIn.Common;

/// <summary>Default values and limits shared by DropIn components.</summary>
internal static class CommonDefaults
{
    /// <summary>Starter JSON written when the user database does not exist.</summary>
    internal const string CustomDatabaseTemplate = """
{
  "icons_and_screenshots": {
    "example/package-id": {
      "icon": "https://example.com/icon.png",
      "images": [
        "https://example.com/screenshot-1.png"
      ]
    }
  },
  "winget": {
    "Example.Package": {
      "icon": "https://example.com/icon.png",
      "images": []
    }
  },
  "chocolatey": {
    "examplepackage": {
      "icon": "https://example.com/icon.png",
      "images": []
    }
  }
}
""";
    /// <summary>Default installer user agent.</summary>
    internal const string UserAgent = "DropIn-Packager/1.0";
    /// <summary>Default installer type for packages without a declared type.</summary>
    internal const string InstallerType = "zip";
    /// <summary>Default package install directory pattern.</summary>
    internal const string InstallSubfolderPattern = @"{Source}\{Name}";
    /// <summary>Source name used in folder patterns when the package source is not winget or chocolatey.</summary>
    internal const string OtherSource = "other";
    /// <summary>Default package output file-name pattern.</summary>
    internal const string OutputNamePattern = "{Id}";
    /// <summary>Default install folder name when no usable name is supplied.</summary>
    internal const string InstallFolderName = "app";
    /// <summary>Default post-install action.</summary>
    internal const string AfterInstall = "AskRunSingleExe";
    /// <summary>Default install directory, relative to the application directory.</summary>
    internal const string InstallDirectory = "Applications";
    /// <summary>Default package output directory, relative to the application directory.</summary>
    internal const string OutputDirectory = "Packages";
    /// <summary>Default directory for kept installer downloads, relative to the application directory.</summary>
    internal const string DownloadsDirectory = "Downloads";
    /// <summary>Default directory of the portable package-manager clients, relative to the application directory.</summary>
    internal const string PortableClientDirectory = "PortableClient";
    /// <summary>Default directory of the media database, relative to the application directory.</summary>
    internal const string DatabaseDirectory = "Database";
    /// <summary>Default screenshot slideshow behavior: rotate until the window is closed.</summary>
    internal const string ScreenshotSlideshow = "Loop";
    /// <summary>Default seconds each screenshot stays visible.</summary>
    internal const int ScreenshotSlideshowSeconds = 4;
    /// <summary>Default package source.</summary>
    internal const string LastSource = "winget";
    /// <summary>Default log severity.</summary>
    internal const string LogLevel = "Info";
    internal const string InstallerLogFile = @"Logs\Installer.log";
    internal const string PackagerLogFile = @"Logs\Packager.log";
    /// <summary>Maximum accepted icon or screenshot size in bytes.</summary>
    internal const int MaxImageBytes = 8 * 1024 * 1024;
    /// <summary>Default network stream buffer size in bytes.</summary>
    internal const int DownloadBufferSize = 128 * 1024;
    /// <summary>Threshold below which downloads use one request.</summary>
    internal const long ParallelDownloadThreshold = 8 * 1024 * 1024;
    /// <summary>Default number of parallel range requests.</summary>
    internal const int ParallelConnections = 4;
    /// <summary>Default transient download retry count.</summary>
    internal const int DownloadRetries = 4;
}
