namespace EduGuardAgent.Security;

internal static class PayloadValidator
{
    public static bool IsValidProcessName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        return !name.Contains('\\')
            && !name.Contains('/')
            && !name.Contains("..");
    }

    public static bool IsValidHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return false;

        return !host.Contains(' ')
            && !host.Contains('/')
            && !host.Contains('\\')
            && !host.Contains("..");
    }
}
