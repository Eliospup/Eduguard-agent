using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace EduGuardAgent.Security;

[SupportedOSPlatform("windows")]
internal sealed class TokenStorage
{
    private readonly string _tokenPath;

    public TokenStorage()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Config.AgentDataDir);
        Directory.CreateDirectory(dir);
        _tokenPath = Path.Combine(dir, Config.TokenFileName);
    }

    public bool TryLoad(out string? token)
    {
        token = null;
        if (!File.Exists(_tokenPath))
            return false;

        try
        {
            var encrypted = File.ReadAllBytes(_tokenPath);
            var plain = ProtectedData.Unprotect(
                encrypted,
                optionalEntropy: null,
                DataProtectionScope.CurrentUser);
            token = Encoding.UTF8.GetString(plain);
            return !string.IsNullOrWhiteSpace(token);
        }
        catch
        {
            Wipe();
            return false;
        }
    }

    public void Save(string token)
    {
        var plain = Encoding.UTF8.GetBytes(token);
        var encrypted = ProtectedData.Protect(
            plain,
            optionalEntropy: null,
            DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_tokenPath, encrypted);
    }

    public void Wipe()
    {
        if (File.Exists(_tokenPath))
            File.Delete(_tokenPath);
    }
}
