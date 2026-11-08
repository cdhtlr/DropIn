namespace DropIn.Packager;

/// <summary>Holds the options parsed from the command line.</summary>
sealed class CliOptions
{
    /// <summary>Gets the packages requested for building.</summary>
    public List<PackageRequest> Requests { get; } = new();
    /// <summary>Gets or sets the directory that receives the generated packages.</summary>
    public string OutputDir { get; set; } = MainForm.DefaultOutputDirectory;
    /// <summary>Gets or sets whether the user is prompted to choose versions and installers.</summary>
    public bool Prompt { get; set; } = !Console.IsInputRedirected;
    /// <summary>Gets or sets the source used when a request does not name one.</summary>
    public string FallbackSource { get; set; } = PackagerConstants.WingetSource;
    /// <summary>Gets or sets whether icons and screenshots are looked up in the UniGetUI database.</summary>
    public bool UseUniGetUi { get; set; } = true;

    /// <summary>Returns whether a value names a supported package source or alias.</summary>
    static bool IsSource(string value)
        => value is PackagerConstants.WingetSource or PackagerConstants.ChocoAlias or PackagerConstants.ChocolateySource;

    /// <summary>Maps the Chocolatey alias to its full source name.</summary>
    static string NormalizeSource(string source)
        => source == PackagerConstants.ChocoAlias ? PackagerConstants.ChocolateySource : source;

    /// <summary>Parses the arguments; returns null and sets the error message when they are invalid.</summary>
    public static CliOptions? Parse(string[] args, out string? error)
    {
        var options = new CliOptions();
        error = IsSource(args[0]) ? options.ParseLegacy(args) : options.ParseOptions(args);
        return error == null ? options : null;
    }

    /// <summary>Parses the original positional syntax: source, package id, optional version, and flags.</summary>
    string? ParseLegacy(string[] args)
    {
        if (args.Length < 2) return "A package id is required.";
        FallbackSource = args[0] == PackagerConstants.WingetSource ? PackagerConstants.WingetSource : PackagerConstants.ChocolateySource;
        string? version = null;
        for (var i = 2; i < args.Length; i++)
        {
            if (args[i] is "-o" or "--output" && i + 1 < args.Length) OutputDir = args[++i];
            else if (args[i] is "-y" or "--yes") Prompt = false;
            else if (args[i] == "--interactive") Prompt = true;
            else if (args[i] == "--no-unigetui") UseUniGetUi = false;
            else if (args[i] is "-v" or "--version" && i + 1 < args.Length) version = args[++i];
            else if (args[i].StartsWith('-')) return $"Unknown option: {args[i]}";
            else version = args[i];
        }
        Requests.Add(new PackageRequest(args[1], FallbackSource, version));
        return null;
    }

    /// <summary>Parses the multi-package option syntax.</summary>
    string? ParseOptions(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--package":
                case "-p":
                    if (++i >= args.Length) return "--package requires a value.";
                    if (IsSource(args[i]))
                    {
                        var source = args[i++];
                        if (i >= args.Length) return $"--package {source} requires a package id.";
                        Requests.Add(new PackageRequest(args[i], NormalizeSource(source)));
                    }
                    else Requests.Add(new PackageRequest(args[i]));
                    break;
                case "--output":
                case "-o":
                    if (++i >= args.Length) return $"{args[i - 1]} requires a directory.";
                    OutputDir = args[i];
                    break;
                case "--source":
                    if (++i >= args.Length || !IsSource(args[i])) return "--source expects winget or chocolatey.";
                    FallbackSource = NormalizeSource(args[i]);
                    break;
                case "--yes":
                case "-y":
                    Prompt = false;
                    break;
                case "--no-unigetui":
                    UseUniGetUi = false;
                    break;
                case "--interactive":
                    Prompt = true;
                    break;
                default:
                    return $"Unknown option or missing --package: {args[i]}";
            }
        }
        return null;
    }
}
