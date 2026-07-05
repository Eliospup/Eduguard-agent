using System.Text.Json;

namespace EduGuardAgent.Security;

internal sealed class SubProfileStore
{
    private static string FileName => Config.SubProfileFileName;

    public StoredSubProfile Load()
    {
        var status = SecureStateFile.Read(FileName, out var json);

        if (status == StateReadStatus.Ok)
        {
            try
            {
                return JsonSerializer.Deserialize<StoredSubProfile>(json) ?? StoredSubProfile.Empty;
            }
            catch
            {
                status = StateReadStatus.Tampered;
            }
        }

        if (status == StateReadStatus.Tampered)
            AuditLog.Write("SECURITY: sub profile failed integrity check.");

        return StoredSubProfile.Empty;
    }

    public void Save(StoredSubProfile profile)
    {
        SecureStateFile.Write(FileName, JsonSerializer.Serialize(profile));
    }

    internal sealed class StoredSubProfile
    {
        public string? DisplayName { get; init; }

        public static StoredSubProfile Empty => new();
    }
}
