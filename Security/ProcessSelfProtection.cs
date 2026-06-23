using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace EduGuardAgent.Security;

/// <summary>
/// Hardens the agent process against being killed from Task Manager (or any other tool)
/// by adding a deny-terminate ACE to the process DACL. This blocks "End task" with an
/// access-denied error <b>without</b> locking Task Manager itself.
///
/// This is always-on for every mode and is not configurable. Self-termination during a
/// clean shutdown is unaffected because the process exits through its own pseudo-handle,
/// which bypasses the DACL check (only foreign OpenProcess calls are filtered).
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ProcessSelfProtection
{
    private const int SE_KERNEL_OBJECT = 6;
    private const int DACL_SECURITY_INFORMATION = 0x00000004;
    private const uint ERROR_SUCCESS = 0;

    private const int PROCESS_TERMINATE = 0x0001;
    private const int PROCESS_SUSPEND_RESUME = 0x0800;

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern uint GetSecurityInfo(
        IntPtr handle,
        int objectType,
        int securityInfo,
        out IntPtr ppsidOwner,
        out IntPtr ppsidGroup,
        out IntPtr ppDacl,
        out IntPtr ppSacl,
        out IntPtr ppSecurityDescriptor);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern uint SetSecurityInfo(
        IntPtr handle,
        int objectType,
        int securityInfo,
        IntPtr psidOwner,
        IntPtr psidGroup,
        byte[]? pDacl,
        byte[]? pSacl);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    /// <summary>Adds a deny-terminate ACE to the current process. Best-effort.</summary>
    public static bool Protect(out string? error)
    {
        error = null;

        try
        {
            using var currentProcess = Process.GetCurrentProcess();
            var handle = currentProcess.Handle;

            var status = GetSecurityInfo(
                handle,
                SE_KERNEL_OBJECT,
                DACL_SECURITY_INFORMATION,
                out _,
                out _,
                out var pDacl,
                out _,
                out var pSecurityDescriptor);

            if (status != ERROR_SUCCESS)
            {
                error = $"GetSecurityInfo failed (Win32 {status}).";
                return false;
            }

            try
            {
                var acl = ReadOrCreateAcl(pDacl);

                var everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);

                if (AlreadyProtected(acl, everyone))
                    return true;

                var denyAce = new CommonAce(
                    AceFlags.None,
                    AceQualifier.AccessDenied,
                    PROCESS_TERMINATE | PROCESS_SUSPEND_RESUME,
                    everyone,
                    isCallback: false,
                    opaque: null);

                // Deny ACEs must precede allow ACEs to stay canonical.
                acl.InsertAce(0, denyAce);

                var newBytes = new byte[acl.BinaryLength];
                acl.GetBinaryForm(newBytes, 0);

                var setStatus = SetSecurityInfo(
                    handle,
                    SE_KERNEL_OBJECT,
                    DACL_SECURITY_INFORMATION,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    newBytes,
                    null);

                if (setStatus != ERROR_SUCCESS)
                {
                    error = $"SetSecurityInfo failed (Win32 {setStatus}).";
                    return false;
                }

                return true;
            }
            finally
            {
                if (pSecurityDescriptor != IntPtr.Zero)
                    LocalFree(pSecurityDescriptor);
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static RawAcl ReadOrCreateAcl(IntPtr pDacl)
    {
        if (pDacl == IntPtr.Zero)
            return new RawAcl(GenericAcl.AclRevision, 1);

        var size = (ushort)Marshal.ReadInt16(pDacl, 2);
        var daclBytes = new byte[size];
        Marshal.Copy(pDacl, daclBytes, 0, size);
        return new RawAcl(daclBytes, 0);
    }

    private static bool AlreadyProtected(RawAcl acl, SecurityIdentifier sid)
    {
        foreach (var ace in acl)
        {
            if (ace is CommonAce common
                && common.AceType == AceType.AccessDenied
                && common.SecurityIdentifier == sid
                && (common.AccessMask & PROCESS_TERMINATE) != 0)
            {
                return true;
            }
        }

        return false;
    }
}
