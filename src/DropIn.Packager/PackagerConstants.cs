namespace DropIn.Packager;

/// <summary>Holds the literal values shared by the packager window and command-line interface.</summary>
static class PackagerConstants
{
    /// <summary>The window title and message-box caption.</summary>
    public const string AppTitle = "DropIn Packager";
    /// <summary>The manifest resource name of the embedded window icon.</summary>
    public const string IconResourceName = "app_icon.ico";

    /// <summary>The source identifier for winget.</summary>
    public const string WingetSource = "winget";
    /// <summary>The source identifier for Chocolatey.</summary>
    public const string ChocolateySource = "chocolatey";
    /// <summary>The short command-line alias for Chocolatey.</summary>
    public const string ChocoAlias = "choco";

    /// <summary>The window width used when the configured width is too small.</summary>
    public const int DefaultWindowWidth = 1000;
    /// <summary>The window height used when the configured height is too small.</summary>
    public const int DefaultWindowHeight = 760;
    /// <summary>The smallest window width allowed.</summary>
    public const int MinWindowWidth = 760;
    /// <summary>The smallest window height allowed.</summary>
    public const int MinWindowHeight = 560;
    /// <summary>The screen size assumed when no primary screen is available.</summary>
    public const int FallbackScreenSize = 4000;
    /// <summary>The height of the log text box.</summary>
    public const int LogHeight = 110;
    /// <summary>The height of the panel hosting the log text box.</summary>
    public const int LogHostHeight = 120;
    /// <summary>The label representing an unrestricted value in an installer filter.</summary>
    public const string AnyFilterLabel = "(any)";
    /// <summary>The text shown on an action button while its action can be canceled.</summary>
    public const string CancelText = "Cancel";
    /// <summary>The separator between screenshot paths or URLs.</summary>
    public const char ScreenshotSeparator = ';';
    /// <summary>The prefix marking read-only manifest metadata rows in the property grid.</summary>
    public const string ManifestRowPrefix = "[manifest] ";

    /// <summary>The manifest property holding the installer architecture.</summary>
    public const string ArchitectureKey = "Architecture";
    /// <summary>The manifest property holding the installer type.</summary>
    public const string InstallerTypeKey = "InstallerType";
    /// <summary>The manifest property holding the installer scope.</summary>
    public const string ScopeKey = "Scope";
    /// <summary>The manifest property holding the installer locale.</summary>
    public const string LocaleKey = "InstallerLocale";

    /// <summary>The font used to render button and menu glyphs.</summary>
    public const string GlyphFontName = "Segoe MDL2 Assets";
    /// <summary>The pixel size of rendered glyph text.</summary>
    public const float GlyphFontPixelSize = 11f;
    /// <summary>The width and height of rendered glyph images.</summary>
    public const int GlyphImageSize = 16;

    /// <summary>Width of the output-folder browse button, in pixels.</summary>
    public const int BrowseButtonWidth = 36;

    /// <summary>Width of the Create .app button, in pixels.</summary>
    public const int CreateButtonWidth = 120;

    /// <summary>Shared height of the browse and Create buttons, in pixels.</summary>
    public const int BottomButtonHeight = 32;
    /// <summary>The glyph for the Fetch button.</summary>
    public const string FetchGlyph = "\uE72C";
    /// <summary>The glyph for the Search button.</summary>
    public const string SearchGlyph = "\uE721";
    /// <summary>The glyph for the Create button.</summary>
    public const string CreateGlyph = "\uE710";
    /// <summary>The glyph for a button acting as Cancel.</summary>
    public const string CancelGlyph = "\uE711";
    /// <summary>The glyph for the database-update command.</summary>
    public const string UpdateDatabaseGlyph = "\uE895";
    /// <summary>The glyph for the Database menu.</summary>
    public const string DatabaseMenuGlyph = "\uE8F1";

    /// <summary>The command-line exit code for success.</summary>
    public const int ExitSuccess = 0;
    /// <summary>The command-line exit code for invalid usage.</summary>
    public const int ExitUsageError = 1;
    /// <summary>The command-line exit code when at least one package failed.</summary>
    public const int ExitPackageFailed = 2;
    /// <summary>The parent-process value accepted by AttachConsole.</summary>
    public const int AttachParentProcess = -1;
}
