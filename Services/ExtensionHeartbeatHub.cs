using EduGuardAgent.Security;
using System.Text.Json;

namespace EduGuardAgent.Services;

internal sealed record ExtensionHeartbeatSnapshot(
    DateTimeOffset At,
    string ExtensionId,
    string? Version,
    bool ShieldActive,
    bool ModelReady);

/// <summary>
/// Last extension heartbeat per browser, received from the local HTTP channel.
/// </summary>
internal static class ExtensionHeartbeatHub
{
    private static readonly object Gate = new();
    private static readonly Dictionary<BrowserKind, ExtensionHeartbeatSnapshot> Last = new();
    private static readonly Dictionary<BrowserKind, DateTimeOffset> LastAuditAt = new();
    private static readonly TimeSpan AuditThrottle = TimeSpan.FromMinutes(5);

    public static void Record(BrowserKind kind, ExtensionHeartbeatSnapshot snapshot)
    {
        lock (Gate)
            Last[kind] = snapshot;
    }

    public static ExtensionHeartbeatSnapshot? Get(BrowserKind kind)
    {
        lock (Gate)
            return Last.TryGetValue(kind, out var snap) ? snap : null;
    }

    public static void Clear(BrowserKind kind)
    {
        lock (Gate)
            Last.Remove(kind);
    }

    public static BrowserKind? ParseBrowserKind(string? browser) =>
        browser?.Trim().ToLowerInvariant() switch
        {
            "chrome" => BrowserKind.Chrome,
            "edge" => BrowserKind.Edge,
            "brave" => BrowserKind.Brave,
            "firefox" => BrowserKind.Firefox,
            _ => null,
        };

    public static void RecordFromPayload(string? browser, string? extensionId, string? version, bool shieldActive, bool modelReady)
    {
        var kind = ParseBrowserKind(browser);
        if (kind is null)
            return;

        var snap = new ExtensionHeartbeatSnapshot(
            DateTimeOffset.UtcNow,
            extensionId ?? "",
            version,
            shieldActive,
            modelReady);

        Record(kind.Value, snap);

        var shouldAudit = false;
        lock (Gate)
        {
            if (!LastAuditAt.TryGetValue(kind.Value, out var lastAudit)
                || DateTimeOffset.UtcNow - lastAudit >= AuditThrottle)
            {
                LastAuditAt[kind.Value] = DateTimeOffset.UtcNow;
                shouldAudit = true;
            }
        }

        if (shouldAudit)
        {
            AuditLog.Write(
                $"Extension heartbeat — {kind.Value}: {extensionId} v{version ?? "?"} " +
                $"shield={(shieldActive ? "on" : "off")} model={(modelReady ? "ready" : "idle")}");
        }
    }

    public static bool ReadBoolProperty(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el))
            return false;

        return el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => el.TryGetInt32(out var n) && n != 0,
            JsonValueKind.String => el.GetString() is { } s
                && (bool.TryParse(s, out var b) && b || s == "1"),
            _ => false,
        };
    }
}
