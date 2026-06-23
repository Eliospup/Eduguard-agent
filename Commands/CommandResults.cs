namespace EduGuardAgent.Commands;

internal static class CommandResults
{
    public static object Unsupported(string? type = null) =>
        type is null
            ? new { error = "unsupported_command" }
            : new { error = "unsupported_command", type };
}
