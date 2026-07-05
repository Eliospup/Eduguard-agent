using System.Text.Json;
using System.Text.Json.Serialization;
using EduGuardAgent.Security;

namespace EduGuardAgent.Services;

/// <summary>
/// Cross-process runtime on/off flag for image shield filtering.
/// Survives separate <see cref="ImageBlurExtensionService"/> instances (e.g. shutdown hooks).
/// </summary>
internal static class ImageShieldRuntimeStore
{
    private static readonly object Gate = new();
    private const string FileName = "image-shield-runtime.json";
    private static bool _inMemoryActive;
    private static bool _inMemoryInitialized;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };

    public static bool IsFilteringActive
    {
        get
        {
            lock (Gate)
            {
                if (_inMemoryInitialized)
                    return _inMemoryActive;

                return Load().FilteringActive;
            }
        }
    }

    public static void SetFilteringActive(bool active)
    {
        lock (Gate)
        {
            _inMemoryActive = active;
            _inMemoryInitialized = true;

            var state = Load() with { FilteringActive = active, UpdatedAtUtc = DateTime.UtcNow };
            Save(state);
        }
    }

    private static RuntimeState Load()
    {
        try
        {
            var status = SecureStateFile.Read(FileName, out var json);
            if (status != StateReadStatus.Ok)
                return RuntimeState.Default;

            return JsonSerializer.Deserialize<RuntimeState>(json, JsonOptions) ?? RuntimeState.Default;
        }
        catch (Exception ex)
        {
            AuditLog.Write($"Image shield runtime load failed: {ex.Message}");
            return RuntimeState.Default;
        }
    }

    private static void Save(RuntimeState state)
    {
        try
        {
            SecureStateFile.Write(FileName, JsonSerializer.Serialize(state, JsonOptions));
        }
        catch (Exception ex)
        {
            AuditLog.Write($"Image shield runtime save failed: {ex.Message}");
        }
    }

    private sealed record RuntimeState(
        [property: JsonPropertyName("filtering_active")] bool FilteringActive,
        [property: JsonPropertyName("updated_at_utc")] DateTime UpdatedAtUtc)
    {
        public static RuntimeState Default { get; } = new(false, DateTime.MinValue);
    }
}
