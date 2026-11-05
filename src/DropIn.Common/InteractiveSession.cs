using System.Runtime.InteropServices;
using System.Text;

namespace DropIn.Common;

/// <summary>Starts an interactive installer on the active desktop from a Windows service.</summary>
public static class InteractiveSession
{
    /// <summary>Wait duration value that asks the Windows API to wait without a timeout.</summary>
    const uint WaitInfinite = 0xFFFFFFFF;
    /// <summary>Session identifier returned when no active console session exists.</summary>
    const uint SessionNone = 0xFFFFFFFF;
    /// <summary>Process creation flag saying the environment block is Unicode.</summary>
    const uint CreateUnicodeEnvironment = 0x400;
    /// <summary>Windows error returned when a program needs administrator rights to start.</summary>
    const int ErrorElevationRequired = 740;

    /// <summary>Windows process startup parameters, including the target interactive desktop.</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct StartupInfo
    {
        /// <summary>Size of this structure in bytes, required by the native API.</summary>
        public int Size;
        /// <summary>Reserved native field; must remain unset.</summary>
        public string? Reserved;
        /// <summary>Desktop on which the process window is created.</summary>
        public string? Desktop;
        /// <summary>Optional console window title.</summary>
        public string? Title;
        /// <summary>Native window position, size, console buffer, fill, and startup flag values.</summary>
        public uint X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
        /// <summary>Window display mode and reserved native value.</summary>
        public short ShowWindow, Reserved2;
        /// <summary>Reserved and standard stream handles required by the native structure layout.</summary>
        public IntPtr Reserved2Pointer, StdInput, StdOutput, StdError;
    }

    /// <summary>Handles and identifiers returned for a newly created process and its primary thread.</summary>
    [StructLayout(LayoutKind.Sequential)]
    struct ProcessInformation
    {
        /// <summary>Handle to the created process.</summary>
        public IntPtr Process;
        /// <summary>Handle to the created process primary thread.</summary>
        public IntPtr Thread;
        /// <summary>Identifier of the created process.</summary>
        public uint ProcessId;
        /// <summary>Identifier of the created primary thread.</summary>
        public uint ThreadId;
    }

    /// <summary>Gets the session identifier of the currently attached physical console.</summary>
    [DllImport("kernel32.dll")]
    static extern uint WTSGetActiveConsoleSessionId();

    /// <summary>Obtains the primary token for the specified logged-on user session.</summary>
    [DllImport("wtsapi32.dll", SetLastError = true)]
    static extern bool WTSQueryUserToken(uint sessionId, out IntPtr token);

    /// <summary>Creates a process using a user token rather than the service identity.</summary>
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CreateProcessAsUser(
        IntPtr token, string applicationName, StringBuilder commandLine,
        IntPtr processAttributes, IntPtr threadAttributes, bool inheritHandles,
        uint creationFlags, IntPtr environment, string currentDirectory,
        ref StartupInfo startupInfo, out ProcessInformation processInformation);

    /// <summary>Waits for a process handle to become signaled or for the requested interval.</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    /// <summary>Retrieves the termination status of a process.</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);

    /// <summary>Builds the environment block (profile, temp and app-data paths) of the token user.</summary>
    [DllImport("userenv.dll", SetLastError = true)]
    static extern bool CreateEnvironmentBlock(out IntPtr environment, IntPtr token, bool inherit);

    /// <summary>Frees an environment block created by <see cref="CreateEnvironmentBlock"/>.</summary>
    [DllImport("userenv.dll")]
    static extern bool DestroyEnvironmentBlock(IntPtr environment);

    /// <summary>Whether an interactive user is logged on to the console.</summary>
    public static bool HasActiveUser => WTSGetActiveConsoleSessionId() != SessionNone;

    /// <summary>Runs a hidden process as the logged-on user so per-user installs (profile, Start menu, Apps list) land in that user's account.</summary>
    /// <param name="exe">Executable path.</param>
    /// <param name="args">Command-line arguments.</param>
    /// <param name="timeoutMs">Wait limit in milliseconds, or 0 for no limit.</param>
    /// <param name="elevate">Runs the program elevated: the user sees a UAC prompt on their desktop.</param>
    /// <param name="needsElevation">Set when the program could not start because it requires administrator rights.</param>
    /// <returns>The exit code, or null when the process could not be started in the user session (or timed out).</returns>
    public static int? RunHiddenAsUser(string exe, string args, int timeoutMs, bool elevate, out bool needsElevation)
    {
        needsElevation = false;
        if (elevate)
        {
            var powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            var list = args.Replace("'", "''", StringComparison.Ordinal);
            var script = $"try {{ $p = Start-Process -FilePath '{exe.Replace("'", "''", StringComparison.Ordinal)}' {(list.Trim().Length == 0 ? string.Empty : $"-ArgumentList '{list}' ")}-Verb RunAs -WindowStyle Hidden -Wait -PassThru; exit $p.ExitCode }} catch {{ exit -1 }}";
            return RunHiddenAsUser(powershell, $"-NoProfile -WindowStyle Hidden -Command \"{script.Replace("\"", "\\\"", StringComparison.Ordinal)}\"", timeoutMs, false, out _);
        }
        var session = WTSGetActiveConsoleSessionId();
        if (session == SessionNone || !WTSQueryUserToken(session, out var token)) return null;
        var env = IntPtr.Zero;
        try
        {
            var hasEnv = CreateEnvironmentBlock(out env, token, false);
            var startup = new StartupInfo { Size = Marshal.SizeOf<StartupInfo>(), Desktop = @"winsta0\default", Flags = 1, ShowWindow = 0 };
            var commandLine = new StringBuilder($"\"{exe}\" {args}");
            // CREATE_NO_WINDOW | CREATE_UNICODE_ENVIRONMENT
            var flags = 0x08000000u | (hasEnv ? CreateUnicodeEnvironment : 0u);
            if (!CreateProcessAsUser(token, exe, commandLine, IntPtr.Zero, IntPtr.Zero, false, flags, hasEnv ? env : IntPtr.Zero,
                    Path.GetDirectoryName(exe) ?? Environment.CurrentDirectory, ref startup, out var process))
            {
                needsElevation = Marshal.GetLastWin32Error() == ErrorElevationRequired;
                return null;
            }
            try
            {
                if (WaitForSingleObject(process.Process, timeoutMs > 0 ? (uint)timeoutMs : WaitInfinite) != 0) return null;
                return GetExitCodeProcess(process.Process, out var code) ? (int)code : null;
            }
            finally
            {
                CloseHandle(process.Thread);
                CloseHandle(process.Process);
            }
        }
        finally
        {
            if (env != IntPtr.Zero) DestroyEnvironmentBlock(env);
            CloseHandle(token);
        }
    }

    /// <summary>Closes an open Windows object handle.</summary>
    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr handle);

    /// <summary>Starts <paramref name="exe"/> with <paramref name="args"/> on the active desktop without waiting for it.</summary>
    /// <param name="exe">Executable path to start.</param>
    /// <param name="args">Arguments passed to the executable.</param>
    /// <returns><see langword="true"/> when process creation succeeds.</returns>
    public static bool Start(string exe, string args)
    {
        var session = WTSGetActiveConsoleSessionId();
        if (session == SessionNone || !WTSQueryUserToken(session, out var token)) return false;
        var env = IntPtr.Zero;
        try
        {
            // Without the user's own environment, %APPDATA% and friends resolve to the SYSTEM profile (wrong Start menu, temp and settings folders).
            var hasEnv = CreateEnvironmentBlock(out env, token, false);
            var startup = new StartupInfo { Size = Marshal.SizeOf<StartupInfo>(), Desktop = @"winsta0\default" };
            var commandLine = new StringBuilder($"\"{exe}\" {args}");
            if (!CreateProcessAsUser(token, exe, commandLine, IntPtr.Zero, IntPtr.Zero, false, hasEnv ? CreateUnicodeEnvironment : 0, hasEnv ? env : IntPtr.Zero,
                    Path.GetDirectoryName(exe) ?? Environment.CurrentDirectory, ref startup, out var process))
                return false;
            CloseHandle(process.Thread);
            CloseHandle(process.Process);
            return true;
        }
        finally
        {
            if (env != IntPtr.Zero) DestroyEnvironmentBlock(env);
            CloseHandle(token);
        }
    }

    /// <summary>Starts an installer on the active user's desktop from the service session.</summary>
    /// <param name="file">Installer path.</param>
    /// <param name="waitForExit">Whether to wait and return the installer exit status.</param>
    public static InstallResult Launch(string file, bool waitForExit)
    {
        var session = WTSGetActiveConsoleSessionId();
        if (session == SessionNone)
            return new InstallResult(false, "No interactive Windows user session is available for the installer fallback.");
        if (!WTSQueryUserToken(session, out var token))
            return new InstallResult(false, $"Could not access the active user session (Windows error {Marshal.GetLastWin32Error()}).");

        var env = IntPtr.Zero;
        try
        {
            var hasEnv = CreateEnvironmentBlock(out env, token, false);
            var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
            var escapedFile = file.Replace("'", "''", StringComparison.Ordinal);
            // PowerShell bridges the installer process so its exit code can be relayed to this process.
            var script = $"& {{ $p = Start-Process -FilePath '{escapedFile}' -Wait -PassThru; exit $p.ExitCode }}";
            var startup = new StartupInfo
            {
                Size = Marshal.SizeOf<StartupInfo>(),
                Desktop = @"winsta0\default"
            };
            var commandLine = new StringBuilder($"\"{powershell}\" -NoProfile -Command \"{script}\"");
            if (!CreateProcessAsUser(token, powershell, commandLine, IntPtr.Zero, IntPtr.Zero, false,
                    hasEnv ? CreateUnicodeEnvironment : 0, hasEnv ? env : IntPtr.Zero, Path.GetDirectoryName(powershell) ?? Environment.CurrentDirectory,
                    ref startup, out var process))
                return new InstallResult(false, $"Could not start the installer in the active user session (Windows error {Marshal.GetLastWin32Error()}).");

            try
            {
                if (!waitForExit) return new InstallResult(false, "Launched the installer in the active user session.");
                if (WaitForSingleObject(process.Process, WaitInfinite) != 0)
                    return new InstallResult(false, "Could not wait for the installer in the active user session.");
                if (!GetExitCodeProcess(process.Process, out var code))
                    return new InstallResult(false, $"Could not read the installer exit code (Windows error {Marshal.GetLastWin32Error()}).");
                return code is 0 or 3010 or 1641
                    ? new InstallResult(true, "Interactive installation completed successfully.")
                    : new InstallResult(false, $"Interactive installer exited with code {code}.");
            }
            finally
            {
                CloseHandle(process.Thread);
                CloseHandle(process.Process);
            }
        }
        finally
        {
            if (env != IntPtr.Zero) DestroyEnvironmentBlock(env);
            CloseHandle(token);
        }
    }
}
