namespace DropIn.Common;

/// <summary>Normalized package selection and installer options parsed from a command line.</summary>
/// <param name="Source">Package source inferred from the command, when specified.</param>
/// <param name="Id">Package identifier, query, or positional name.</param>
/// <param name="Version">Requested package version, if present.</param>
/// <param name="Architecture">Normalized architecture option, if present.</param>
/// <param name="InstallerType">Requested installer type, if present.</param>
/// <param name="Scope">Requested installation scope, if present.</param>
/// <param name="Locale">Requested locale, if present.</param>
public sealed record ParsedCommand(
    string? Source, string Id, string? Version, string? Architecture, string? InstallerType, string? Scope, string? Locale);

/// <summary>Extracts the package name and options from a "winget install ..." / "choco install ..." command (or a bare name).</summary>
public static class CommandParser
{
    /// <summary>winget options whose following values are irrelevant to package selection.</summary>
    static readonly HashSet<string> WingetSkipValue = new(StringComparer.OrdinalIgnoreCase)
    {
        "-s", "--source", "-l", "--location", "-o", "--log", "--override", "-h", "--header", "--custom",
        "--dependency-source", "--authentication-mode", "--authentication-account", "--rename", "--ignore-security-hash-value",
    };

    /// <summary>Chocolatey options whose following values are irrelevant to package selection.</summary>
    static readonly HashSet<string> ChocoSkipValue = new(StringComparer.OrdinalIgnoreCase)
    {
        "-s", "--source", "--params", "--parameters", "--package-parameters", "--ia", "--installargs", "--installarguments",
        "--install-arguments", "-u", "--user", "-p", "--password", "--cert", "--certpassword", "--timeout",
        "--execution-timeout", "--cache-location", "--cachelocation", "--proxy", "--proxy-user", "--proxy-password",
        "--download-checksum", "--download-checksum-type", "--download-checksum-x64", "--download-checksum-type-x64",
        "--log-file", "--sourcename",
    };

    /// <summary>Parses a package-manager command or bare package name into normalized fields.</summary>
    /// <param name="input">Command-line text supplied by a user or drag-and-drop action.</param>
    /// <returns>The parsed package selection, or <see langword="null"/> when no package name is found.</returns>
    public static ParsedCommand? Parse(string input)
    {
        var t = Tokenize(input);
        if (t.Count == 0) return null;
        string? source = null;
        var i = 0;
        var head = Path.GetFileNameWithoutExtension(t[0]).ToLowerInvariant();
        if (head is "winget") { source = "winget"; i = 1; }
        else if (head is "choco" or "chocolatey" or "cinst") { source = "chocolatey"; i = 1; }
        if (source != null && i < t.Count && !t[i].StartsWith('-')
            && t[i].ToLowerInvariant() is "install" or "add" or "i" or "upgrade" or "download")
            i++;

        string? id = null, query = null, version = null, arch = null, type = null, scope = null, locale = null, positional = null;
        // Package-manager-specific skip lists prevent option values from being mistaken for package names.
        var skip = source == "chocolatey" ? ChocoSkipValue : WingetSkipValue;
        for (; i < t.Count; i++)
        {
            var tok = t[i];
            if (!tok.StartsWith('-')) { positional ??= tok; continue; }
            string key = tok, val = "";
            var hasVal = false;
            var eq = tok.IndexOf('=');
            if (eq > 0) { key = tok[..eq]; val = tok[(eq + 1)..]; hasVal = true; }
            key = key.ToLowerInvariant();

            string? Value()
            {
                if (hasVal) return val;
                return i + 1 < t.Count ? t[++i] : null;
            }

            switch (key)
            {
                case "--id": id = Value(); break;
                case "-q" or "--query" or "--name" or "--moniker": query = Value(); break;
                case "-v" or "--version": version = Value(); break;
                case "-a" or "--architecture": arch = Value(); break;
                case "--installer-type": type = Value(); break;
                case "--scope": scope = Value(); break;
                case "--locale": locale = Value(); break;
                case "--x86" or "-x86" or "--forcex86" or "--force-x86": arch = "x86"; break;
                case "--x64" or "-x64" or "--forcex64": arch = "x64"; break;
                default:
                    if (skip.Contains(key) && !hasVal) i++;
                    break;
            }
        }
        var name = id ?? query ?? positional;
        if (string.IsNullOrWhiteSpace(name)) return null;
        return new ParsedCommand(source, name.Trim(), Clean(version), Normalize(arch), Clean(type), Clean(scope), Clean(locale));
    }

    /// <summary>Trims an optional value and converts blank text to <see langword="null"/>.</summary>
    static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>Converts common architecture aliases to the x86 or x64 canonical value.</summary>
    static string? Normalize(string? arch) => Clean(arch)?.ToLowerInvariant() switch
    {
        "x86" or "32" or "i386" or "win32" => "x86",
        "x64" or "64" or "amd64" => "x64",
        var a => a
    };

    /// <summary>Splits command-line text on whitespace while preserving quoted tokens as one value.</summary>
    /// <param name="s">Input command line.</param>
    static List<string> Tokenize(string s)
    {
        var res = new List<string>();
        var sb = new System.Text.StringBuilder();
        char quote = '\0';
        // Track whether an empty quoted argument was present so it remains a token.
        var has = false;
        foreach (var c in s)
        {
            if (quote != '\0') { if (c == quote) quote = '\0'; else sb.Append(c); }
            else if (c is '"' or '\'') { quote = c; has = true; }
            else if (char.IsWhiteSpace(c)) { if (sb.Length > 0 || has) res.Add(sb.ToString()); sb.Clear(); has = false; }
            else sb.Append(c);
        }
        if (sb.Length > 0 || has) res.Add(sb.ToString());
        return res;
    }
}
