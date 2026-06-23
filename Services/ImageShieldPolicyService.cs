using System.Text.Json;
using System.Text.Json.Serialization;
using EduGuardAgent.Models;
using EduGuardAgent.Security;

namespace EduGuardAgent.Services;

internal static class ImageShieldBrowserKeys
{
    public const string Firefox = "firefox";
    public const string Chrome = "chrome";
    public const string Edge = "edge";
    public const string Brave = "brave";

    public static bool IsKnown(string? key) =>
        key is Firefox or Chrome or Edge or Brave;

    public static string ForKind(BrowserKind kind) => kind switch
    {
        BrowserKind.Firefox => Firefox,
        BrowserKind.Chrome => Chrome,
        BrowserKind.Edge => Edge,
        BrowserKind.Brave => Brave,
        _ => Firefox,
    };

    public static BrowserKind? ToKind(string? key) => key switch
    {
        Firefox => BrowserKind.Firefox,
        Chrome => BrowserKind.Chrome,
        Edge => BrowserKind.Edge,
        Brave => BrowserKind.Brave,
        _ => null,
    };
}

internal sealed class ImageShieldPolicyService
{
    private readonly object _lock = new();
    private ImageShieldPolicyState _state;
    private bool _policiesActive;

    public ImageShieldPolicyService()
    {
        _state = (ImageShieldPolicyStore.Load() ?? ImageShieldPolicyState.LocalDefaults()).Normalize();
        ImageShieldPolicy.Current = this;
    }

    public bool HasServerConfig
    {
        get
        {
            lock (_lock)
                return _state.HasServerConfig;
        }
    }

    public bool PoliciesActive
    {
        get
        {
            lock (_lock)
                return _policiesActive;
        }
    }

    public void SetPoliciesActive(bool active)
    {
        lock (_lock)
            _policiesActive = active;
    }

    public bool ApplyFromServer(ImageShieldSettingsPayload payload)
    {
        lock (_lock)
        {
            var before = _state.ComputeSignature(null);
            _state = _state.Merge(payload, fromServer: true).Normalize();
            var changed = before != _state.ComputeSignature(null);
            if (changed)
                ImageShieldPolicyStore.Save(_state);
            return changed;
        }
    }

    public bool ApplyLocal(ImageShieldSettingsPayload payload)
    {
        lock (_lock)
        {
            var before = _state.ComputeSignature(null);
            _state = _state.Merge(payload, fromServer: false).Normalize();
            var changed = before != _state.ComputeSignature(null);
            if (changed)
                ImageShieldPolicyStore.Save(_state);
            return changed;
        }
    }

    public bool GlobalEnabled
    {
        get
        {
            lock (_lock)
                return _state.GlobalEnabled;
        }
    }

    public bool IsEffectivelyEnabled(string? modeSlug) =>
        IsEffectivelyEnabledForModeAndBrowsers(modeSlug);

    public bool IsEffectivelyEnabledForModeAndBrowsers(string? modeSlug)
    {
        lock (_lock)
        {
            if (!_state.GlobalEnabled)
                return false;

            var mode = AgentModeSlugs.Normalize(modeSlug);
            if (_state.PerMode.TryGetValue(mode, out var toggle) && !toggle.Enabled)
                return false;

            foreach (var (key, browserToggle) in _state.PerBrowser)
            {
                if (!browserToggle.Enabled)
                    continue;

                if (ImageShieldBrowserKeys.ToKind(key) is { } kind && IsBrowserAgentAvailable(kind))
                    return true;
            }

            return false;
        }
    }

    public bool IsBrowserDomEnabled(BrowserKind kind)
    {
        var key = ImageShieldBrowserKeys.ForKind(kind);
        lock (_lock)
        {
            if (_state.PerBrowser.TryGetValue(key, out var toggle))
                return toggle.Enabled;

            return kind == BrowserKind.Firefox;
        }
    }

    public static bool IsBrowserAgentAvailable(BrowserKind kind) => kind switch
    {
        BrowserKind.Firefox =>
            Config.ExtensionGuardEnforceFirefox
            && (Config.ExtensionGuardFirefoxLocalMode
                || ExtensionConfigResolver.Active?.IsFirefoxStoreReady == true),
        BrowserKind.Chrome or BrowserKind.Edge or BrowserKind.Brave =>
            Config.ExtensionGuardEnforceChromium
            && (ChromiumUnpackedMode.IsActive
                || ExtensionConfigResolver.Active?.IsChromiumReady == true),
        _ => false,
    };

    public bool ShouldEnforceBrowser(BrowserKind kind) =>
        IsBrowserDomEnabled(kind) && IsBrowserAgentAvailable(kind);

    public IReadOnlyDictionary<string, bool> BuildBrowserActiveMap(string? modeSlug)
    {
        lock (_lock)
        {
            var mode = AgentModeSlugs.Normalize(modeSlug);
            var modeEnabled = _state.GlobalEnabled
                && (!_state.PerMode.TryGetValue(mode, out var modeToggle) || modeToggle.Enabled);

            var result = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var key in new[] { ImageShieldBrowserKeys.Firefox, ImageShieldBrowserKeys.Chrome, ImageShieldBrowserKeys.Edge, ImageShieldBrowserKeys.Brave })
            {
                var kind = ImageShieldBrowserKeys.ToKind(key)!.Value;
                var browserEnabled = _state.PerBrowser.TryGetValue(key, out var browserToggle)
                    ? browserToggle.Enabled
                    : kind == BrowserKind.Firefox;
                result[key] = modeEnabled && browserEnabled && IsBrowserAgentAvailable(kind);
            }

            result["chromium"] = result[ImageShieldBrowserKeys.Chrome]
                || result[ImageShieldBrowserKeys.Edge]
                || result[ImageShieldBrowserKeys.Brave];
            return result;
        }
    }

    public ImageShieldSettings BuildTuningSettings(string? modeSlug)
    {
        lock (_lock)
        {
            var def = ImageShieldSettings.Default;
            return new ImageShieldSettings(
                _state.MinSize is { } ms ? Math.Clamp(ms, 16, 1024) : def.MinSize,
                _state.MaxPerSecond is { } mps ? Math.Clamp(mps, 1, 60) : def.MaxPerSecond,
                _state.NsfwThreshold is { } th ? Math.Clamp(th, 0.1, 0.99) : def.NsfwThreshold,
                def.ThumbThreshold,
                _state.SexyWeight is { } sw ? Math.Clamp(sw, 0, 1) : def.SexyWeight,
                ShieldActive: true,
                UiMode: AgentModeSlugs.Normalize(modeSlug));
        }
    }

    public string ComputeSignature(string? modeSlug)
    {
        lock (_lock)
            return _state.ComputeSignature(modeSlug);
    }

    public void ApplyTo(HeartbeatRequest request, string? modeSlug)
    {
        lock (_lock)
        {
            var effective = IsEffectivelyEnabledForModeAndBrowsers(modeSlug);
            var browsers = new Dictionary<string, ImageShieldBrowserStatusPayload>(StringComparer.Ordinal);

            foreach (var key in new[] { ImageShieldBrowserKeys.Firefox, ImageShieldBrowserKeys.Chrome, ImageShieldBrowserKeys.Edge, ImageShieldBrowserKeys.Brave })
            {
                var kind = ImageShieldBrowserKeys.ToKind(key)!.Value;
                var domEnabled = _state.PerBrowser.TryGetValue(key, out var toggle) && toggle.Enabled;
                var available = IsBrowserAgentAvailable(kind);
                string? reason = null;
                bool? requiresDev = null;

                if (!available)
                {
                    reason = kind == BrowserKind.Firefox
                        ? "firefox_dev_edition_required"
                        : "work_in_progress";
                }
                else if (kind == BrowserKind.Firefox
                         && Config.ExtensionGuardFirefoxLocalMode
                         && ExtensionConfigResolver.Active?.IsFirefoxStoreReady != true)
                {
                    requiresDev = true;
                }

                browsers[key] = new ImageShieldBrowserStatusPayload
                {
                    EnabledByDom = domEnabled,
                    Available = available,
                    Enforced = effective && domEnabled && available && _policiesActive,
                    UnavailableReason = available ? null : reason,
                    RequiresDevEdition = requiresDev,
                };
            }

            request.ImageShield = new ImageShieldAgentStatusPayload
            {
                GlobalEnabled = _state.GlobalEnabled,
                EffectiveEnabled = effective,
                Mode = AgentModeSlugs.Normalize(modeSlug),
                Configured = ExtensionConfigResolver.IsReady,
                PoliciesActive = _policiesActive,
                HasServerConfig = _state.HasServerConfig,
                Browsers = browsers,
            };
        }
    }
}

internal sealed class ImageShieldPolicyState
{
    [JsonPropertyName("global_enabled")]
    public bool GlobalEnabled { get; init; } = true;

    [JsonPropertyName("has_server_config")]
    public bool HasServerConfig { get; init; }

    [JsonPropertyName("per_mode")]
    public Dictionary<string, ImageShieldToggleState> PerMode { get; init; } =
        new(StringComparer.Ordinal);

    [JsonPropertyName("per_browser")]
    public Dictionary<string, ImageShieldToggleState> PerBrowser { get; init; } =
        new(StringComparer.Ordinal);

    [JsonPropertyName("min_size")]
    public int? MinSize { get; init; }

    [JsonPropertyName("nsfw_threshold")]
    public double? NsfwThreshold { get; init; }

    [JsonPropertyName("sexy_weight")]
    public double? SexyWeight { get; init; }

    [JsonPropertyName("max_per_second")]
    public int? MaxPerSecond { get; init; }

    public static ImageShieldPolicyState LocalDefaults() => new()
    {
        GlobalEnabled = true,
        HasServerConfig = false,
        PerMode = AllModesEnabled(),
        PerBrowser = LocalBrowserDefaults(),
    };

    public static ImageShieldPolicyState EnrolledPendingDefaults() => new()
    {
        GlobalEnabled = false,
        HasServerConfig = false,
        PerMode = AllModesEnabled(),
        PerBrowser = AllBrowsersDisabled(),
    };

    public ImageShieldPolicyState Normalize() => new()
    {
        GlobalEnabled = GlobalEnabled,
        HasServerConfig = HasServerConfig,
        PerMode = PerMode is { Count: > 0 }
            ? new Dictionary<string, ImageShieldToggleState>(PerMode, StringComparer.Ordinal)
            : AllModesEnabled(),
        PerBrowser = PerBrowser is { Count: > 0 }
            ? new Dictionary<string, ImageShieldToggleState>(PerBrowser, StringComparer.Ordinal)
            : LocalBrowserDefaults(),
        MinSize = MinSize,
        NsfwThreshold = NsfwThreshold,
        SexyWeight = SexyWeight,
        MaxPerSecond = MaxPerSecond,
    };

    public ImageShieldPolicyState Merge(ImageShieldSettingsPayload payload, bool fromServer)
    {
        var perMode = new Dictionary<string, ImageShieldToggleState>(PerMode, StringComparer.Ordinal);
        if (payload.PerMode is not null)
        {
            foreach (var (mode, toggle) in payload.PerMode)
            {
                if (!AgentModeSlugs.IsKnown(mode))
                    continue;

                perMode[AgentModeSlugs.Normalize(mode)] = new ImageShieldToggleState { Enabled = toggle.Enabled };
            }
        }

        var perBrowser = new Dictionary<string, ImageShieldToggleState>(PerBrowser, StringComparer.Ordinal);
        if (payload.PerBrowser is not null)
        {
            foreach (var (browser, toggle) in payload.PerBrowser)
            {
                if (!ImageShieldBrowserKeys.IsKnown(browser))
                    continue;

                perBrowser[browser.ToLowerInvariant()] = new ImageShieldToggleState { Enabled = toggle.Enabled };
            }
        }

        return new ImageShieldPolicyState
        {
            GlobalEnabled = payload.Enabled,
            HasServerConfig = fromServer || HasServerConfig,
            PerMode = perMode,
            PerBrowser = perBrowser,
            MinSize = payload.MinSize ?? MinSize,
            NsfwThreshold = payload.NsfwThreshold ?? NsfwThreshold,
            SexyWeight = payload.SexyWeight ?? SexyWeight,
            MaxPerSecond = payload.MaxPerSecond ?? MaxPerSecond,
        };
    }

    public string ComputeSignature(string? modeSlug)
    {
        var mode = AgentModeSlugs.Normalize(modeSlug);
        var modePart = PerMode.TryGetValue(mode, out var m) ? m.Enabled : true;
        var browserPart = string.Join("|", PerBrowser.OrderBy(p => p.Key).Select(p => $"{p.Key}:{p.Value.Enabled}"));
        return $"{GlobalEnabled}|{mode}:{modePart}|{browserPart}|{MinSize}|{MaxPerSecond}|{NsfwThreshold}|{SexyWeight}";
    }

    private static Dictionary<string, ImageShieldToggleState> AllModesEnabled() =>
        new(StringComparer.Ordinal)
        {
            [AgentModeSlugs.TrustedSub] = new() { Enabled = true },
            [AgentModeSlugs.Sub] = new() { Enabled = true },
            [AgentModeSlugs.RestrictedSub] = new() { Enabled = true },
        };

    private static Dictionary<string, ImageShieldToggleState> LocalBrowserDefaults() =>
        new(StringComparer.Ordinal)
        {
            [ImageShieldBrowserKeys.Firefox] = new() { Enabled = true },
            [ImageShieldBrowserKeys.Chrome] = new() { Enabled = Config.ExtensionGuardEnforceChromium },
            [ImageShieldBrowserKeys.Edge] = new() { Enabled = false },
            [ImageShieldBrowserKeys.Brave] = new() { Enabled = false },
        };

    private static Dictionary<string, ImageShieldToggleState> AllBrowsersDisabled() =>
        new(StringComparer.Ordinal)
        {
            [ImageShieldBrowserKeys.Firefox] = new() { Enabled = false },
            [ImageShieldBrowserKeys.Chrome] = new() { Enabled = false },
            [ImageShieldBrowserKeys.Edge] = new() { Enabled = false },
            [ImageShieldBrowserKeys.Brave] = new() { Enabled = false },
        };
}

internal sealed class ImageShieldToggleState
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }
}

internal static class ImageShieldPolicyStore
{
    private static readonly string PolicyPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Config.AgentDataDir,
        "image-shield-policy.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };

    public static ImageShieldPolicyState? Load()
    {
        try
        {
            if (!File.Exists(PolicyPath))
                return null;

            var json = File.ReadAllText(PolicyPath);
            return JsonSerializer.Deserialize<ImageShieldPolicyState>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            AuditLog.Write($"Image shield policy load failed: {ex.Message}");
            return null;
        }
    }

    public static void Save(ImageShieldPolicyState state)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PolicyPath)!);
            var json = JsonSerializer.Serialize(state, JsonOptions);
            File.WriteAllText(PolicyPath, json);
        }
        catch (Exception ex)
        {
            AuditLog.Write($"Image shield policy save failed: {ex.Message}");
        }
    }
}
