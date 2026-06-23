using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace EduGuardAgent.Security;

[SupportedOSPlatform("windows")]
internal sealed class ExitPinStorage
{
    private readonly string _pinPath;

    public ExitPinStorage()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Config.AgentDataDir);
        Directory.CreateDirectory(dir);
        _pinPath = Path.Combine(dir, Config.ExitPinFileName);
    }

    public bool TryLoad(out string? pin)
    {
        pin = null;
        if (!File.Exists(_pinPath))
            return false;

        try
        {
            var encrypted = File.ReadAllBytes(_pinPath);
            var plain = ProtectedData.Unprotect(
                encrypted,
                optionalEntropy: null,
                DataProtectionScope.CurrentUser);
            pin = Encoding.UTF8.GetString(plain);
            return !string.IsNullOrEmpty(pin);
        }
        catch
        {
            Wipe();
            return false;
        }
    }

    public void Save(string pin)
    {
        var plain = Encoding.UTF8.GetBytes(pin);
        var encrypted = ProtectedData.Protect(
            plain,
            optionalEntropy: null,
            DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_pinPath, encrypted);
    }

    public void Wipe()
    {
        if (File.Exists(_pinPath))
            File.Delete(_pinPath);
    }
}
