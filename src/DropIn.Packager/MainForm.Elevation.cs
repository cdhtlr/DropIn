using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

using DropIn.Common;

namespace DropIn.Packager;

static class Elevation
{
    /// <summary>Returns whether the current process runs with administrator rights.</summary>
    public static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>Win32 error code returned when the user declines the UAC prompt.</summary>
    const int UacDeclined = 1223;

    /// <summary>Starts a new elevated instance of this program with the given arguments.</summary>
    /// <param name="args">Command-line arguments to pass on.</param>
    /// <param name="failure">The reason it could not start (a declined UAC prompt is named as such), or null on success.</param>
    public static Process? RestartElevated(IEnumerable<string>? args, out string? failure)
    {
        failure = null;
        var psi = new ProcessStartInfo(Environment.ProcessPath!) { Verb = "runas", UseShellExecute = true };
        foreach (var a in args ?? Array.Empty<string>()) psi.ArgumentList.Add(a);
        try { return Process.Start(psi); }
        catch (Win32Exception ex)
        {
            failure = ex.NativeErrorCode == UacDeclined
                ? "you declined the administrator (UAC) prompt."
                : "administrator rights could not be obtained: " + ex.Message;
            return null;
        }
    }

    /// <summary>Lists the folders that are not writable, or an empty list when everything is fine or the process is already elevated.</summary>
    /// <param name="directories">Folders the program is about to write to.</param>
    public static List<string> DeniedFolders(IEnumerable<string> directories)
        => IsElevated() ? new List<string>() : WriteAccess.Denied(directories);

    /// <summary>Text that tells the user which folders need administrator rights.</summary>
    /// <param name="denied">Folders that are not writable.</param>
    public static string Explain(IEnumerable<string> denied)
        => "DropIn needs administrator rights to write to:\n\n" + string.Join("\n", denied.Select(d => "  " + d));
}
