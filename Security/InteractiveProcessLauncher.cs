using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace EduGuardAgent.Security;

/// <summary>
/// Launches a process into the active interactive desktop session from a SYSTEM context
/// (Session 0). Used by the SYSTEM guardian to resurrect the user-mode agent: a SYSTEM
/// service cannot simply Process.Start a GUI into the user's session because of Session 0
/// isolation, so it must grab the console session's user token and CreateProcessAsUser.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class InteractiveProcessLauncher
{
    /// <summary>Id of the active console (interactive) session, or 0xFFFFFFFF if none.</summary>
    public static uint ActiveConsoleSessionId() => WTSGetActiveConsoleSessionId();

    public static bool TryLaunchInActiveSession(string exePath, string? arguments, out string? error)
    {
        error = null;
        var userToken = IntPtr.Zero;
        var linkedToken = IntPtr.Zero;
        var primaryToken = IntPtr.Zero;
        var environment = IntPtr.Zero;

        try
        {
            var sessionId = WTSGetActiveConsoleSessionId();
            if (sessionId == 0xFFFFFFFF)
            {
                error = "No active console session.";
                return false;
            }

            if (!WTSQueryUserToken(sessionId, out userToken))
            {
                error = $"WTSQueryUserToken failed (Win32 {Marshal.GetLastWin32Error()}).";
                return false;
            }

            // WTSQueryUserToken hands back the UAC-filtered (limited) token. The agent's
            // manifest requires administrator, and CreateProcessAsUser does not trigger
            // elevation, so launching with the filtered token fails with ERROR_ELEVATION_
            // REQUIRED (740). Use the user's linked (full, elevated) token when there is one.
            var tokenForDuplicate = userToken;
            linkedToken = TryGetLinkedToken(userToken);
            if (linkedToken != IntPtr.Zero)
                tokenForDuplicate = linkedToken;

            var attrs = new SECURITY_ATTRIBUTES { nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>() };
            if (!DuplicateTokenEx(tokenForDuplicate, TOKEN_ALL_ACCESS, ref attrs,
                    SecurityImpersonation, TokenPrimary, out primaryToken))
            {
                error = $"DuplicateTokenEx failed (Win32 {Marshal.GetLastWin32Error()}).";
                return false;
            }

            if (!CreateEnvironmentBlock(out environment, primaryToken, inherit: false))
                environment = IntPtr.Zero; // Non-fatal; proceed without a custom block.

            var startupInfo = new STARTUPINFO
            {
                cb = Marshal.SizeOf<STARTUPINFO>(),
                lpDesktop = @"winsta0\default",
            };

            var commandLine = string.IsNullOrEmpty(arguments) ? $"\"{exePath}\"" : $"\"{exePath}\" {arguments}";
            var flags = CREATE_UNICODE_ENVIRONMENT | CREATE_NO_WINDOW;

            var created = CreateProcessAsUser(
                primaryToken,
                null,
                commandLine,
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                flags,
                environment,
                Path.GetDirectoryName(exePath),
                ref startupInfo,
                out var processInfo);

            if (!created)
            {
                error = $"CreateProcessAsUser failed (Win32 {Marshal.GetLastWin32Error()}).";
                return false;
            }

            if (processInfo.hProcess != IntPtr.Zero) CloseHandle(processInfo.hProcess);
            if (processInfo.hThread != IntPtr.Zero) CloseHandle(processInfo.hThread);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
        finally
        {
            if (environment != IntPtr.Zero) DestroyEnvironmentBlock(environment);
            if (primaryToken != IntPtr.Zero) CloseHandle(primaryToken);
            if (linkedToken != IntPtr.Zero) CloseHandle(linkedToken);
            if (userToken != IntPtr.Zero) CloseHandle(userToken);
        }
    }

    /// <summary>
    /// Returns the user's linked (elevated) token when the supplied token is the UAC-filtered
    /// one for an admin, or <see cref="IntPtr.Zero"/> if there is no elevated counterpart.
    /// </summary>
    private static IntPtr TryGetLinkedToken(IntPtr token)
    {
        var buffer = Marshal.AllocHGlobal(IntPtr.Size);
        try
        {
            if (GetTokenInformation(token, TokenLinkedToken, buffer, IntPtr.Size, out _))
                return Marshal.ReadIntPtr(buffer); // TOKEN_LINKED_TOKEN.LinkedToken

            return IntPtr.Zero;
        }
        catch
        {
            return IntPtr.Zero;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private const uint TOKEN_ALL_ACCESS = 0xF01FF;
    private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    private const uint CREATE_NO_WINDOW = 0x08000000;
    private const int SecurityImpersonation = 2;
    private const int TokenPrimary = 1;
    private const int TokenLinkedToken = 19;

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(
        IntPtr tokenHandle, int tokenInformationClass, IntPtr tokenInformation,
        int tokenInformationLength, out int returnLength);

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQueryUserToken(uint sessionId, out IntPtr phToken);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DuplicateTokenEx(
        IntPtr hExistingToken, uint dwDesiredAccess, ref SECURITY_ATTRIBUTES lpTokenAttributes,
        int impersonationLevel, int tokenType, out IntPtr phNewToken);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool CreateEnvironmentBlock(out IntPtr lpEnvironment, IntPtr hToken, bool inherit);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool DestroyEnvironmentBlock(IntPtr lpEnvironment);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessAsUser(
        IntPtr hToken, string? lpApplicationName, string lpCommandLine,
        IntPtr lpProcessAttributes, IntPtr lpThreadAttributes, bool bInheritHandles,
        uint dwCreationFlags, IntPtr lpEnvironment, string? lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct SECURITY_ATTRIBUTES
    {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        public bool bInheritHandle;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }
}
