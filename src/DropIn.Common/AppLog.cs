namespace DropIn.Common;

/// <summary>Optional file log controlled by LogFile and LogLevel in dropin.config.json.</summary>
public static class AppLog
{
    /// <summary>Serializes writes so concurrent callers do not interleave log entries.</summary>
    static readonly object Gate = new();
    /// <summary>Absolute configured log path, or <see langword="null"/> when logging is disabled.</summary>
    static string? _path;
    /// <summary>Maximum numeric severity rank written to the log.</summary>
    static int _level = 2;

    /// <summary>Maps a textual log level to its severity rank.</summary>
    static int Rank(string level) => level.Trim().ToLowerInvariant() switch
    {
        "error" => 0,
        "warning" or "warn" => 1,
        "debug" or "trace" => 3,
        _ => 2
    };

    /// <summary>Configures the optional log destination and minimum verbosity.</summary>
    /// <param name="file">Path relative to <paramref name="baseDir"/>, or blank to disable logging.</param>
    /// <param name="level">Maximum level to write; unrecognized values use the informational level.</param>
    /// <param name="baseDir">Directory used to resolve a relative log path.</param>
    public static void Configure(string? file, string? level, string baseDir)
    {
        _level = Rank(level ?? "Info");
        _path = string.IsNullOrWhiteSpace(file) ? null : Path.GetFullPath(Path.Combine(baseDir, file));
    }

    /// <summary>Appends a timestamped message when logging is enabled and its level is permitted.</summary>
    /// <param name="level">Severity label to include in the entry.</param>
    /// <param name="message">Text to append to the log.</param>
    public static void Write(string level, string message)
    {
        var path = _path;
        if (path == null || Rank(level) > _level) return;
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(path, $"{DateTime.Now:s} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Writes an informational message.</summary>
    /// <param name="message">Text to append to the log.</param>
    public static void Info(string message) => Write("Info", message);
    /// <summary>Writes an error message.</summary>
    /// <param name="message">Text to append to the log.</param>
    public static void Error(string message) => Write("Error", message);
}
