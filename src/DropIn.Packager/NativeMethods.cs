using System.Runtime.InteropServices;

namespace DropIn.Packager;

/// <summary>Declares the Win32 functions used by the packager.</summary>
static class NativeMethods
{
    /// <summary>Attaches the command-line process to the parent console.</summary>
    [DllImport("kernel32.dll")]
    public static extern bool AttachConsole(int pid);
}
