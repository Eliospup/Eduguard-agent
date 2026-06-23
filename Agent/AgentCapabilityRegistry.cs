using EduGuardAgent.Models;

namespace EduGuardAgent.Agent;

internal static class AgentCapabilityRegistry
{
    public static IReadOnlySet<string> SupportedCommandTypes { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "show_message",
            "lock_screen",
            "unlock_screen",
            "kill_process",
            "block_app",
            "unblock_app",
            "block_url",
            "unblock_url",
            "screenshot",
            "set_bedtime",
            "set_exit_pin",
            "set_gaming_limit",
            "set_gaming_games",
            "set_gaming_overlay",
            "set_youtube_limit",
            "set_youtube_overlay",
            "set_youtube_restricted_mode",
            "set_study_time",
            "set_mode",
            "set_kiosk_apps",
            "set_punishment",
            "reset_punishment",
            "set_image_shield",
        };

    public static bool IsSupported(string type) => SupportedCommandTypes.Contains(type);

    public static CapabilitySyncResult SyncWithServer(CapabilitiesResponse server)
    {
        var serverTypes = server.Commands
            .Select(c => c.Type)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var unimplemented = serverTypes
            .Except(SupportedCommandTypes, StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var unknownToServer = SupportedCommandTypes
            .Except(serverTypes, StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new CapabilitySyncResult
        {
            Fetched = true,
            ServerProtocolVersion = server.ProtocolVersion,
            ServerCommandTypes = serverTypes.OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList(),
            UnimplementedByAgent = unimplemented,
            UnknownToServer = unknownToServer,
        };
    }
}

internal sealed class CapabilitySyncResult
{
    public bool Fetched { get; init; }
    public int? ServerProtocolVersion { get; init; }
    public IReadOnlyList<string> ServerCommandTypes { get; init; } = [];
    public IReadOnlyList<string> UnimplementedByAgent { get; init; } = [];
    public IReadOnlyList<string> UnknownToServer { get; init; } = [];

    public static CapabilitySyncResult FetchFailed() => new() { Fetched = false };
}
