namespace DropIn.Common;

/// <summary>A snapshot of installation progress.</summary>
/// <param name="Percent">Completion from 0 to 100.</param>
/// <param name="Stage">Short description of the current step.</param>
/// <param name="Estimated">True when the percentage cannot be measured (an external installer is running) and the UI should only estimate it.</param>
public readonly record struct InstallProgressInfo(int Percent, string Stage, bool Estimated = false);

/// <summary>Helpers and stage boundaries shared by the installation steps.</summary>
public static class InstallProgress
{
    /// <summary>A reporter that ignores every update.</summary>
    public static readonly IProgress<InstallProgressInfo> None = new Progress<InstallProgressInfo>();

    /// <summary>Percentage reached once the package archive has been extracted.</summary>
    public const int PackageExtracted = 20;

    /// <summary>Percentage at which the installation is complete.</summary>
    public const int Complete = 100;

    /// <summary>Maps a fraction (0 to 1) of a step onto the percentage range from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static int Scale(double fraction, int from, int to)
        => from + (int)Math.Round(Math.Clamp(fraction, 0, 1) * (to - from));
}
