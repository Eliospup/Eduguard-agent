using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography.X509Certificates;

namespace EduGuardAgent.Security;

/// <summary>
/// Authenticode trust checks used to decide whether another process is really a genuine Guardi
/// binary (not a same-path replacement an admin dropped in to drive the secure-state IPC pipe).
///
/// The check is relative to THIS running build:
///  - If we are unsigned, <see cref="MatchesSelfSigner"/> is a no-op that returns true — so
///    unsigned developer/self-hosted builds behave exactly as before (the caller's image-path
///    check is the only gate). The moment the shipped exe is Authenticode-signed, the check
///    activates automatically with no code change.
///  - If we are signed, a peer must carry a VALID Authenticode signature (verified with
///    WinVerifyTrust — a copied-in certificate with a broken hash is rejected) by the SAME
///    signer certificate as ourselves.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class AuthenticodeVerifier
{
    private static readonly object Gate = new();
    private static bool _selfResolved;
    private static string? _selfThumbprint;

    /// <summary>Our own Authenticode signer thumbprint, or null when this build is unsigned.</summary>
    public static string? SelfSignerThumbprint()
    {
        if (_selfResolved)
            return _selfThumbprint;

        lock (Gate)
        {
            if (_selfResolved)
                return _selfThumbprint;

            var path = Environment.ProcessPath;
            _selfThumbprint = path is not null && IsSignatureValid(path) ? SignerThumbprint(path) : null;
            _selfResolved = true;
            return _selfThumbprint;
        }
    }

    /// <summary>
    /// True when <paramref name="path"/> is trusted relative to this build: a no-op (true) for
    /// unsigned builds; otherwise a valid signature by our own signer certificate.
    /// </summary>
    public static bool MatchesSelfSigner(string? path)
    {
        var self = SelfSignerThumbprint();
        if (self is null)
            return true; // Unsigned build — path check (done by the caller) remains the only gate.

        if (string.IsNullOrEmpty(path) || !IsSignatureValid(path))
            return false;

        var theirs = SignerThumbprint(path);
        return theirs is not null && string.Equals(theirs, self, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The embedded Authenticode signer's thumbprint, or null if not signed / unreadable.</summary>
    public static string? SignerThumbprint(string path)
    {
        try
        {
            // CreateFromSignedFile extracts the Authenticode signer cert from a PE's security
            // directory — there is no X509CertificateLoader replacement for that, so the
            // SYSLIB0057 obsoletion is suppressed locally. GetCertHashString() returns the same
            // uppercase SHA-1 hex as X509Certificate2.Thumbprint, without the obsolete ctor.
#pragma warning disable SYSLIB0057
            var cert = X509Certificate.CreateFromSignedFile(path);
#pragma warning restore SYSLIB0057
            return cert.GetCertHashString();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>WinVerifyTrust: the file has a well-formed, untampered, chain-trusted signature.</summary>
    public static bool IsSignatureValid(string path)
    {
        try
        {
            var fileInfo = new WINTRUST_FILE_INFO
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(),
                pcwszFilePath = path,
                hFile = IntPtr.Zero,
                pgKnownSubject = IntPtr.Zero,
            };

            var pFile = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
            try
            {
                Marshal.StructureToPtr(fileInfo, pFile, false);

                var data = new WINTRUST_DATA
                {
                    cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
                    dwUIChoice = WTD_UI_NONE,
                    fdwRevocationChecks = WTD_REVOKE_NONE,
                    dwUnionChoice = WTD_CHOICE_FILE,
                    pFile = pFile,
                    dwStateAction = WTD_STATEACTION_IGNORE,
                    dwProvFlags = WTD_SAFER_FLAG,
                };

                var action = WINTRUST_ACTION_GENERIC_VERIFY_V2;
                var result = WinVerifyTrust(IntPtr.Zero, ref action, ref data);
                return result == 0; // ERROR_SUCCESS — trusted
            }
            finally
            {
                Marshal.FreeHGlobal(pFile);
            }
        }
        catch
        {
            return false;
        }
    }

    // ---- WinVerifyTrust interop ------------------------------------------------------------

    private const uint WTD_UI_NONE = 2;
    private const uint WTD_REVOKE_NONE = 0;
    private const uint WTD_CHOICE_FILE = 1;
    private const uint WTD_STATEACTION_IGNORE = 0;
    private const uint WTD_SAFER_FLAG = 0x100;

    private static Guid WINTRUST_ACTION_GENERIC_VERIFY_V2 =
        new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_FILE_INFO
    {
        public uint cbStruct;
        [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_DATA
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [DllImport("wintrust.dll", SetLastError = true)]
    private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid pgActionID, ref WINTRUST_DATA pWVTData);
}
