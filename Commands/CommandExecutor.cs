using System.Text.Json;
using System.Windows;
using EduGuardAgent.Agent;
using EduGuardAgent.Models;
using EduGuardAgent.Security;
using EduGuardAgent.Services;

namespace EduGuardAgent.Commands;

internal sealed class CommandExecutor
{
    private readonly IAgentNotifier _notifier;
    private readonly SessionState _sessionState;
    private readonly UrlBlockingService _urlBlocking;

    public CommandExecutor(IAgentNotifier notifier, SessionState sessionState, UrlBlockingService urlBlocking)
    {
        _notifier = notifier;
        _sessionState = sessionState;
        _urlBlocking = urlBlocking;
    }

    public (bool Success, object Result) Execute(AgentCommand command)
    {
        AuditLog.Write($"Executing {command.Type} ({command.Id})");

        if (!AgentCapabilityRegistry.IsSupported(command.Type))
            return (false, CommandResults.Unsupported(command.Type));

        try
        {
            return command.Type switch
            {
                "show_message" => ExecuteShowMessage(command),
                "lock_screen" => ExecuteLockScreen(command),
                "unlock_screen" => ExecuteUnlockScreen(command),
                "kill_process" => ExecuteKillProcess(command),
                "block_app" => ExecuteBlockApp(command),
                "unblock_app" => ExecuteUnblockApp(command),
                "block_url" => ExecuteBlockUrl(command),
                "unblock_url" => ExecuteUnblockUrl(command),
                "set_bedtime" => ExecuteSetBedtime(command),
                "set_exit_pin" => ExecuteSetExitPin(command),
                "set_gaming_limit" => ExecuteSetGamingLimit(command),
                "set_gaming_games" => ExecuteSetGamingGames(command),
                "set_gaming_overlay" => ExecuteSetGamingOverlay(command),
                "set_youtube_limit" => ExecuteSetYoutubeLimit(command),
                "set_youtube_overlay" => ExecuteSetYoutubeOverlay(command),
                "set_youtube_restricted_mode" => ExecuteSetYoutubeRestrictedMode(command),
                "set_study_time" => ExecuteSetStudyTime(command),
                "set_mode" => ExecuteSetMode(command),
                "set_kiosk_apps" => ExecuteSetKioskApps(command),
                "set_punishment" => ExecuteSetPunishment(command),
                "reset_punishment" => ExecuteResetPunishment(command),
                "set_image_shield" => ExecuteSetImageShield(command),
                _ => (false, CommandResults.Unsupported(command.Type)),
            };
        }
        catch (Exception ex)
        {
            AuditLog.Write($"Command {command.Id} failed: {ex.Message}");
            return (false, new { error = ex.Message });
        }
    }

    private (bool Success, object Result) ExecuteShowMessage(AgentCommand command)
    {
        var text = GetPayloadString(command, "text");
        if (string.IsNullOrWhiteSpace(text))
            return (false, new { error = "missing text" });

        _notifier.DomMessageReceived(text);

        return (true, new { shown = true });
    }

    private (bool Success, object Result) ExecuteLockScreen(AgentCommand command)
    {
        _ = command;
        _notifier.DomLockRequested();
        return (true, new { locked = true, mode = "overlay" });
    }

    private (bool Success, object Result) ExecuteUnlockScreen(AgentCommand command)
    {
        _ = command;
        _notifier.DomLockReleased();
        return (true, new { unlocked = true });
    }

    private (bool Success, object Result) ExecuteKillProcess(AgentCommand command)
    {
        var name = GetPayloadString(command, "name");
        if (!PayloadValidator.IsValidProcessName(name))
            return (false, new { error = "invalid process name" });

        var processName = name!.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? name[..^4]
            : name;

        var processes = System.Diagnostics.Process.GetProcessesByName(processName);
        if (processes.Length == 0)
            return (false, new { error = "process not found" });

        var killed = 0;
        foreach (var process in processes)
        {
            try
            {
                process.Kill(entireProcessTree: true);
                killed++;
            }
            catch
            {
                // Some processes may refuse termination.
            }
            finally
            {
                process.Dispose();
            }
        }

        if (killed > 0)
            _notifier.AppClosedByGuardi(name!, AppBlockCategory.DomImmediate);

        return killed > 0
            ? (true, new { killed })
            : (false, new { error = "could not kill process" });
    }

    private (bool Success, object Result) ExecuteBlockApp(AgentCommand command)
    {
        var name = GetPayloadString(command, "name");
        if (!PayloadValidator.IsValidProcessName(name))
            return (false, new { error = "invalid process name" });

        _sessionState.BlockApp(name!, AppBlockCategory.DomManual);
        _notifier.RestrictionsChanged();
        return (true, new { blocked = name });
    }

    private (bool Success, object Result) ExecuteUnblockApp(AgentCommand command)
    {
        var name = GetPayloadString(command, "name");
        if (!PayloadValidator.IsValidProcessName(name))
            return (false, new { error = "invalid process name" });

        _sessionState.UnblockApp(name!);
        _notifier.RestrictionsChanged();
        return (true, new { unblocked = name });
    }

    private (bool Success, object Result) ExecuteBlockUrl(AgentCommand command)
    {
        var host = GetPayloadString(command, "host");
        if (!PayloadValidator.IsValidHost(host))
            return (false, new { error = "invalid host" });

        var normalized = UrlBlocklistStore.NormalizeHost(host!);
        if (normalized is null)
            return (false, new { error = "invalid host" });

        if (!_urlBlocking.Block(normalized))
            return (false, new { error = "already blocked or failed to save" });

        _notifier.RestrictionsChanged();
        _notifier.Log($"Blocked website: {normalized}");
        return (true, new { blocked = normalized });
    }

    private (bool Success, object Result) ExecuteUnblockUrl(AgentCommand command)
    {
        var host = GetPayloadString(command, "host");
        if (!PayloadValidator.IsValidHost(host))
            return (false, new { error = "invalid host" });

        var normalized = UrlBlocklistStore.NormalizeHost(host!);
        if (normalized is null)
            return (false, new { error = "invalid host" });

        if (!_urlBlocking.Unblock(normalized))
            return (false, new { error = "host was not blocked" });

        _notifier.RestrictionsChanged();
        _notifier.Log($"Unblocked website: {normalized}");
        return (true, new { unblocked = normalized });
    }

    private (bool Success, object Result) ExecuteSetBedtime(AgentCommand command)
    {
        var enabled = GetPayloadBool(command, "enabled") ?? true;
        var time = GetPayloadString(command, "time");
        var wakeTime = GetPayloadString(command, "wake_time");
        var weekly = ParseWeeklyBedtime(command);
        if (time is not null && !BedtimeSettings.TryParseTime(time, out _))
            return (false, new { error = "invalid time — use HH:mm (24h)" });
        if (wakeTime is not null && !BedtimeSettings.TryParseTime(wakeTime, out _))
            return (false, new { error = "invalid wake_time — use HH:mm (24h)" });

        var blueLightFilterEnabled = GetPayloadBool(command, "blue_light_filter_enabled");

        _notifier.BedtimeSettingsReceived(new BedtimeSettingsPayload
        {
            Enabled = enabled,
            Time = time,
            WakeTime = wakeTime,
            Weekly = weekly,
            BlueLightFilterEnabled = blueLightFilterEnabled,
        });

        return (true, new
        {
            enabled,
            time,
            wake_time = wakeTime,
            weekly,
            blue_light_filter_enabled = blueLightFilterEnabled ?? true,
        });
    }

    private (bool Success, object Result) ExecuteSetExitPin(AgentCommand command)
    {
        if (command.Payload is null || !command.Payload.ContainsKey("pin"))
            return (false, new { error = "missing pin" });

        var pin = GetPayloadPin(command);
        if (pin is not null && !ExitPinService.IsValidFormat(pin))
            return (false, new { error = "invalid pin — use 4-8 digits" });

        _notifier.ExitPinReceived(pin);
        return (true, new { updated = true });
    }

    private (bool Success, object Result) ExecuteSetGamingLimit(AgentCommand command)
    {
        var minutes = GetPayloadInt(command, "daily_limit_minutes");
        var weekly = ParseWeeklyMinuteLimits(command);
        if (minutes is null && weekly is null)
            return (false, new { error = "missing daily_limit_minutes or weekly_limits" });
        if (minutes is not null && (minutes < 1 || minutes > 1440))
            return (false, new { error = "invalid daily_limit_minutes — use 1-1440" });
        if (weekly is not null && !ValidateWeeklyMinuteLimits(weekly, out var weeklyError))
            return (false, new { error = weeklyError });

        _notifier.GamingSettingsReceived(new GamingSettingsPayload
        {
            DailyLimitMinutes = minutes,
            WeeklyLimits = weekly,
        });
        return (true, new { daily_limit_minutes = minutes, weekly_limits = weekly });
    }

    private (bool Success, object Result) ExecuteSetGamingGames(AgentCommand command)
    {
        if (command.Payload is null)
            return (false, new { error = "missing payload" });

        List<GamingExtraGamePayload>? extra = null;
        List<string>? ignored = null;
        Dictionary<string, int>? gameLimits = null;

        if (command.Payload.ContainsKey("extra_games"))
        {
            if (!command.Payload.TryGetValue("extra_games", out var extraElement))
                return (false, new { error = "invalid extra_games" });

            extra = ParseExtraGames(extraElement);
            if (extra is null)
                return (false, new { error = "invalid extra_games" });
        }

        if (command.Payload.ContainsKey("ignored_games"))
        {
            if (!command.Payload.TryGetValue("ignored_games", out var ignoredElement))
                return (false, new { error = "invalid ignored_games" });

            ignored = ParseIgnoredGames(ignoredElement);
            if (ignored is null)
                return (false, new { error = "invalid ignored_games" });
        }

        if (command.Payload.ContainsKey("game_limits"))
        {
            if (!command.Payload.TryGetValue("game_limits", out var limitsElement))
                return (false, new { error = "invalid game_limits" });

            gameLimits = ParseGameLimits(limitsElement);
            if (gameLimits is null)
                return (false, new { error = "invalid game_limits" });
        }

        if (extra is null && ignored is null && gameLimits is null)
            return (false, new { error = "missing extra_games, ignored_games, or game_limits" });

        _notifier.GamingSettingsReceived(new GamingSettingsPayload
        {
            ExtraGames = extra,
            IgnoredGames = ignored,
            GameLimits = gameLimits,
        }, replaceGameLists: true);

        return (true, new
        {
            extra_games = extra?.Count ?? 0,
            ignored_games = ignored?.Count ?? 0,
            game_limits = gameLimits?.Count ?? 0,
        });
    }

    private (bool Success, object Result) ExecuteSetGamingOverlay(AgentCommand command)
    {
        if (command.Payload is null || !command.Payload.TryGetValue("show_playtime_overlay", out var value))
            return (false, new { error = "missing show_playtime_overlay" });

        var show = value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            _ => (bool?)null,
        };

        if (show is null)
            return (false, new { error = "invalid show_playtime_overlay — use boolean" });

        _notifier.GamingSettingsReceived(new GamingSettingsPayload
        {
            ShowPlaytimeOverlay = show,
        });

        return (true, new { show_playtime_overlay = show });
    }

    private (bool Success, object Result) ExecuteSetYoutubeLimit(AgentCommand command)
    {
        var minutes = GetPayloadInt(command, "daily_limit_minutes");
        var weekly = ParseWeeklyMinuteLimits(command);
        if (minutes is null && weekly is null)
            return (false, new { error = "missing daily_limit_minutes or weekly_limits" });
        if (minutes is not null && (minutes < 1 || minutes > 1440))
            return (false, new { error = "invalid daily_limit_minutes — use 1-1440" });
        if (weekly is not null && !ValidateWeeklyMinuteLimits(weekly, out var weeklyError))
            return (false, new { error = weeklyError });

        _notifier.YoutubeSettingsReceived(new YoutubeSettingsPayload
        {
            DailyLimitMinutes = minutes,
            WeeklyLimits = weekly,
        });
        return (true, new { daily_limit_minutes = minutes, weekly_limits = weekly });
    }

    private (bool Success, object Result) ExecuteSetYoutubeOverlay(AgentCommand command)
    {
        if (command.Payload is null || !command.Payload.TryGetValue("show_overlay", out var value))
            return (false, new { error = "missing show_overlay" });

        var show = value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            _ => (bool?)null,
        };

        if (show is null)
            return (false, new { error = "invalid show_overlay — use boolean" });

        _notifier.YoutubeSettingsReceived(new YoutubeSettingsPayload
        {
            ShowOverlay = show,
        });

        return (true, new { show_overlay = show });
    }

    private (bool Success, object Result) ExecuteSetYoutubeRestrictedMode(AgentCommand command)
    {
        if (command.Payload is null || !command.Payload.TryGetValue("restricted_mode_enabled", out var value))
            return (false, new { error = "missing restricted_mode_enabled" });

        var enabled = value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            _ => (bool?)null,
        };

        if (enabled is null)
            return (false, new { error = "invalid restricted_mode_enabled — use boolean" });

        _notifier.YoutubeSettingsReceived(new YoutubeSettingsPayload
        {
            RestrictedModeEnabled = enabled,
        });

        return (true, new { restricted_mode_enabled = enabled });
    }

    private (bool Success, object Result) ExecuteSetStudyTime(AgentCommand command)
    {
        var enabled = GetPayloadBool(command, "enabled") ?? true;
        var startTime = GetPayloadString(command, "start_time");
        var endTime = GetPayloadString(command, "end_time");
        var weekly = ParseWeeklyStudy(command);
        var blockGames = GetPayloadBool(command, "block_games");
        var blockYoutube = GetPayloadBool(command, "block_youtube");
        var blockDistractingSites = GetPayloadBool(command, "block_distracting_sites");
        var blockDistractingApps = GetPayloadBool(command, "block_distracting_apps");

        if (startTime is not null && !BedtimeSettings.TryParseTime(startTime, out _))
            return (false, new { error = "invalid start_time — use HH:mm (24h)" });
        if (endTime is not null && !BedtimeSettings.TryParseTime(endTime, out _))
            return (false, new { error = "invalid end_time — use HH:mm (24h)" });

        List<string>? days = null;
        if (command.Payload is not null && command.Payload.TryGetValue("days", out var daysElement))
        {
            days = ParseStudyDays(daysElement);
            if (days is null)
                return (false, new { error = "invalid days — use mon,tue,wed,thu,fri,sat,sun" });
        }

        if (enabled && days is { Count: 0 } && weekly is null)
            return (false, new { error = "at least one day is required when study time is enabled" });

        _notifier.StudyTimeSettingsReceived(new StudyTimeSettingsPayload
        {
            Enabled = enabled,
            StartTime = startTime,
            EndTime = endTime,
            Days = days,
            Weekly = weekly,
            BlockGames = blockGames,
            BlockYoutube = blockYoutube,
            BlockDistractingSites = blockDistractingSites,
            BlockDistractingApps = blockDistractingApps,
        });

        return (true, new
        {
            enabled,
            start_time = startTime,
            end_time = endTime,
            days,
            weekly,
            block_games = blockGames ?? true,
            block_youtube = blockYoutube ?? true,
            block_distracting_sites = blockDistractingSites ?? true,
            block_distracting_apps = blockDistractingApps ?? true,
        });
    }

    private static Dictionary<string, StudyDayPayload>? ParseWeeklyStudy(AgentCommand command)
    {
        if (command.Payload is null || !command.Payload.TryGetValue("weekly", out var element))
            return null;

        if (element.ValueKind != JsonValueKind.Object)
            return null;

        var weekly = new Dictionary<string, StudyDayPayload>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
        {
            if (!DayScheduleKeys.TryParse(property.Name, out _) || property.Value.ValueKind != JsonValueKind.Object)
                continue;

            bool? dayEnabled = null;
            string? start = null;
            string? end = null;

            foreach (var dayProperty in property.Value.EnumerateObject())
            {
                switch (dayProperty.Name)
                {
                    case "enabled":
                        dayEnabled = dayProperty.Value.ValueKind switch
                        {
                            JsonValueKind.True => true,
                            JsonValueKind.False => false,
                            JsonValueKind.String when bool.TryParse(dayProperty.Value.GetString(), out var parsed) => parsed,
                            _ => null,
                        };
                        break;
                    case "start_time":
                        start = dayProperty.Value.GetString();
                        break;
                    case "end_time":
                        end = dayProperty.Value.GetString();
                        break;
                }
            }

            if (start is not null && !BedtimeSettings.TryParseTime(start, out _))
                continue;
            if (end is not null && !BedtimeSettings.TryParseTime(end, out _))
                continue;

            weekly[property.Name] = new StudyDayPayload
            {
                Enabled = dayEnabled,
                StartTime = start,
                EndTime = end,
            };
        }

        return weekly.Count == 0 ? null : weekly;
    }

    private static List<string>? ParseStudyDays(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
            return null;

        var days = new List<string>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                return null;

            var day = item.GetString();
            if (string.IsNullOrWhiteSpace(day))
                return null;

            var normalized = day.Trim().ToLowerInvariant();
            if (normalized is not ("sun" or "mon" or "tue" or "wed" or "thu" or "fri" or "sat"))
                return null;

            if (!days.Contains(normalized, StringComparer.Ordinal))
                days.Add(normalized);
        }

        return days;
    }

    private (bool Success, object Result) ExecuteSetMode(AgentCommand command)
    {
        var slug = GetPayloadString(command, "slug");
        if (slug is not null && !AgentModeSlugs.IsKnown(slug))
            return (false, new { error = "invalid slug — use trusted_sub, sub, or restricted_sub" });

        var displayName = GetPayloadString(command, "display_name");
        ModeFeaturesPayload? features = null;
        ScreenTimeSettingsPayload? screenTime = null;

        if (command.Payload is not null)
        {
            if (command.Payload.TryGetValue("features", out var featuresElement))
            {
                features = ParseModeFeatures(featuresElement);
                if (features is null)
                    return (false, new { error = "invalid features" });
            }

            if (command.Payload.ContainsKey("daily_limit_minutes")
                || command.Payload.ContainsKey("weekly_limits"))
            {
                int? minutes = null;
                if (command.Payload.TryGetValue("daily_limit_minutes", out var minutesElement))
                {
                    minutes = minutesElement.ValueKind switch
                    {
                        JsonValueKind.Number when minutesElement.TryGetInt32(out var number) => number,
                        JsonValueKind.String when int.TryParse(minutesElement.GetString(), out var parsed) => parsed,
                        _ => null,
                    };

                    if (minutes is null or < 1 or > 1440)
                        return (false, new { error = "invalid daily_limit_minutes — use 1-1440" });
                }

                var weekly = ParseWeeklyMinuteLimits(command);
                if (weekly is not null && !ValidateWeeklyMinuteLimits(weekly, out var weeklyError))
                    return (false, new { error = weeklyError });

                if (minutes is null && weekly is null)
                    return (false, new { error = "invalid screen_time — provide daily_limit_minutes and/or weekly_limits" });

                screenTime = new ScreenTimeSettingsPayload
                {
                    DailyLimitMinutes = minutes,
                    WeeklyLimits = weekly,
                };
            }
        }

        if (slug is null && features is null && screenTime is null)
            return (false, new { error = "missing slug, features, daily_limit_minutes, or weekly_limits" });

        _notifier.ModeSettingsReceived(
            new ModeSettingsPayload
            {
                Slug = slug,
                DisplayName = displayName,
                Features = features,
            },
            screenTime,
            domOverride: slug is not null);

        return (true, new
        {
            slug,
            display_name = displayName,
            daily_limit_minutes = screenTime?.DailyLimitMinutes,
        });
    }

    private static ModeFeaturesPayload? ParseModeFeatures(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        if (!TryReadOptionalBool(element, "block_task_manager", out var blockTaskManager)
            || !TryReadOptionalBool(element, "vpn_shield", out var vpnShield)
            || !TryReadOptionalBool(element, "block_registry_editor", out var blockRegistryEditor)
            || !TryReadOptionalBool(element, "block_command_prompt", out var blockCommandPrompt)
            || !TryReadOptionalBool(element, "block_powershell", out var blockPowerShell)
            || !TryReadOptionalBool(element, "block_system_config", out var blockSystemConfig)
            || !TryReadOptionalBool(element, "block_control_panel", out var blockControlPanel)
            || !TryReadOptionalBool(element, "block_process_tools", out var blockProcessTools)
            || !TryReadOptionalBool(element, "block_process_killers", out var blockProcessKillers)
            || !TryReadOptionalBool(element, "kiosk_mode", out var kioskMode))
        {
            return null;
        }

        return new ModeFeaturesPayload
        {
            BlockTaskManager = blockTaskManager,
            VpnShield = vpnShield,
            BlockRegistryEditor = blockRegistryEditor,
            BlockCommandPrompt = blockCommandPrompt,
            BlockPowerShell = blockPowerShell,
            BlockSystemConfig = blockSystemConfig,
            BlockControlPanel = blockControlPanel,
            BlockProcessTools = blockProcessTools,
            BlockProcessKillers = blockProcessKillers,
            KioskMode = kioskMode,
        };
    }

    private static bool TryReadOptionalBool(JsonElement element, string property, out bool? value)
    {
        value = null;
        if (!element.TryGetProperty(property, out var found))
            return true;

        switch (found.ValueKind)
        {
            case JsonValueKind.True:
                value = true;
                return true;
            case JsonValueKind.False:
                value = false;
                return true;
            default:
                return false;
        }
    }

    private (bool Success, object Result) ExecuteSetPunishment(AgentCommand command)
    {
        var enabled = GetPayloadBool(command, "enabled");
        var thresholdTrusted = GetPayloadInt(command, "threshold_trusted_to_sub");
        var thresholdRestricted = GetPayloadInt(command, "threshold_sub_to_restricted");
        var legacyThreshold = GetPayloadInt(command, "infraction_threshold");
        var escalationHours = GetPayloadInt(command, "escalation_hours");
        var escalationMinutes = GetPayloadInt(command, "escalation_minutes");
        var extensionHours = GetPayloadDouble(command, "infraction_extension_hours");
        InfractionKindsPayload? infractionKinds = null;
        InfractionExtensionsPayload? infractionExtensions = null;

        if (command.Payload is not null)
        {
            if (command.Payload.TryGetValue("infraction_kinds", out var kindsElement))
            {
                infractionKinds = ParseInfractionKinds(kindsElement);
                if (infractionKinds is null)
                    return (false, new { error = "invalid infraction_kinds" });
            }

            if (command.Payload.TryGetValue("infraction_extensions", out var extensionsElement))
            {
                infractionExtensions = ParseInfractionExtensions(extensionsElement);
                if (infractionExtensions is null)
                    return (false, new { error = "invalid infraction_extensions" });
            }
        }

        if (enabled is null
            && thresholdTrusted is null
            && thresholdRestricted is null
            && legacyThreshold is null
            && escalationHours is null
            && escalationMinutes is null
            && extensionHours is null
            && infractionKinds is null
            && infractionExtensions is null)
        {
            return (false, new
            {
                error = "missing punishment settings fields",
            });
        }

        if (thresholdTrusted is not null && (thresholdTrusted < 1 || thresholdTrusted > 50))
            return (false, new { error = "invalid threshold_trusted_to_sub — use 1-50" });

        if (thresholdRestricted is not null && (thresholdRestricted < 1 || thresholdRestricted > 50))
            return (false, new { error = "invalid threshold_sub_to_restricted — use 1-50" });

        if (legacyThreshold is not null && (legacyThreshold < 1 || legacyThreshold > 50))
            return (false, new { error = "invalid infraction_threshold — use 1-50" });

        if (escalationHours is not null && (escalationHours < 0 || escalationHours > 720))
            return (false, new { error = "invalid escalation_hours — use 0-720" });

        if (escalationMinutes is not null && (escalationMinutes < 0 || escalationMinutes > 59))
            return (false, new { error = "invalid escalation_minutes — use 0-59" });

        if (extensionHours is not null && (extensionHours < 0 || extensionHours > 720))
            return (false, new { error = "invalid infraction_extension_hours — use 0-720" });

        var defaults = PunishmentSettings.Default;
        _notifier.PunishmentSettingsReceived(new PunishmentSettingsPayload
        {
            Enabled = enabled,
            ThresholdTrustedToSub = thresholdTrusted,
            ThresholdSubToRestricted = thresholdRestricted,
            InfractionThreshold = legacyThreshold,
            EscalationHours = escalationHours,
            EscalationMinutes = escalationMinutes,
            InfractionExtensionHours = extensionHours,
            InfractionExtensions = infractionExtensions,
            InfractionKinds = infractionKinds,
        });

        var merged = PunishmentService.MergeSettingsForResponse(defaults, new PunishmentSettingsPayload
        {
            Enabled = enabled,
            ThresholdTrustedToSub = thresholdTrusted,
            ThresholdSubToRestricted = thresholdRestricted,
            InfractionThreshold = legacyThreshold,
            EscalationHours = escalationHours,
            EscalationMinutes = escalationMinutes,
            InfractionExtensionHours = extensionHours,
            InfractionExtensions = infractionExtensions,
            InfractionKinds = infractionKinds,
        });

        return (true, new
        {
            enabled = merged.Enabled,
            threshold_trusted_to_sub = merged.ThresholdTrustedToSub,
            threshold_sub_to_restricted = merged.ThresholdSubToRestricted,
            escalation_hours = merged.EscalationHours,
            escalation_minutes = merged.EscalationMinutes,
            infraction_extensions = merged.InfractionExtensions.ToPayload(),
            infraction_kinds = merged.InfractionKinds.ToPayload(),
        });
    }

    private static InfractionExtensionsPayload? ParseInfractionExtensions(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        return new InfractionExtensionsPayload
        {
            VpnAttempt = ParseDurationParts(element, "vpn_attempt"),
            BlockedAppRepeated = ParseDurationParts(element, "blocked_app_repeated"),
            BypassAttempt = ParseDurationParts(element, "bypass_attempt"),
            LimitIgnored = ParseDurationParts(element, "limit_ignored"),
            StudyTimeViolation = ParseDurationParts(element, "study_time_violation"),
            BlockedSearch = ParseDurationParts(element, "blocked_search"),
        };
    }

    private static DurationPartsPayload? ParseDurationParts(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.Object)
            return null;

        int? hours = null;
        int? minutes = null;
        if (element.TryGetProperty("hours", out var hoursElement) && hoursElement.TryGetInt32(out var parsedHours))
            hours = parsedHours;
        if (element.TryGetProperty("minutes", out var minutesElement) && minutesElement.TryGetInt32(out var parsedMinutes))
            minutes = parsedMinutes;

        if (hours is null && minutes is null)
            return null;

        return new DurationPartsPayload { Hours = hours, Minutes = minutes };
    }

    private static InfractionKindsPayload? ParseInfractionKinds(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        if (!TryReadOptionalBool(element, "vpn_attempt", out var vpnAttempt)
            || !TryReadOptionalBool(element, "blocked_app_repeated", out var blockedAppRepeated)
            || !TryReadOptionalBool(element, "bypass_attempt", out var bypassAttempt)
            || !TryReadOptionalBool(element, "limit_ignored", out var limitIgnored)
            || !TryReadOptionalBool(element, "study_time_violation", out var studyTimeViolation)
            || !TryReadOptionalBool(element, "blocked_search", out var blockedSearch))
        {
            return null;
        }

        return new InfractionKindsPayload
        {
            VpnAttempt = vpnAttempt,
            BlockedAppRepeated = blockedAppRepeated,
            BypassAttempt = bypassAttempt,
            LimitIgnored = limitIgnored,
            StudyTimeViolation = studyTimeViolation,
            BlockedSearch = blockedSearch,
        };
    }

    private (bool Success, object Result) ExecuteResetPunishment(AgentCommand command)
    {
        _ = command;
        _notifier.PunishmentResetRequested();
        return (true, new { reset = true });
    }

    private (bool Success, object Result) ExecuteSetImageShield(AgentCommand command)
    {
        if (command.Payload is null)
            return (false, new { error = "missing payload" });

        ImageShieldSettingsPayload? payload;
        try
        {
            var json = JsonSerializer.Serialize(command.Payload);
            payload = JsonSerializer.Deserialize<ImageShieldSettingsPayload>(json);
        }
        catch
        {
            payload = null;
        }

        if (payload is null)
            return (false, new { error = "invalid payload" });

        if (payload.PerMode is not null)
        {
            foreach (var mode in payload.PerMode.Keys)
            {
                if (!AgentModeSlugs.IsKnown(mode))
                    return (false, new { error = $"invalid per_mode key — use trusted_sub, sub, or restricted_sub (got {mode})" });
            }
        }

        if (payload.PerBrowser is not null)
        {
            foreach (var browser in payload.PerBrowser.Keys)
            {
                if (!ImageShieldBrowserKeys.IsKnown(browser))
                    return (false, new { error = $"invalid per_browser key — use firefox, chrome, edge, or brave (got {browser})" });
            }
        }

        _notifier.ImageShieldSettingsReceived(payload);

        return (true, new
        {
            enabled = payload.Enabled,
            per_mode = payload.PerMode?.Count ?? 0,
            per_browser = payload.PerBrowser?.Count ?? 0,
        });
    }

    private (bool Success, object Result) ExecuteSetKioskApps(AgentCommand command)
    {
        if (command.Payload is null || !command.Payload.TryGetValue("apps", out var appsElement))
            return (false, new { error = "missing apps" });

        var apps = ParseKioskApps(appsElement);
        if (apps is null)
            return (false, new { error = "invalid apps — each needs a name and a .exe path" });

        _notifier.KioskSettingsReceived(new KioskSettingsPayload { ApprovedApps = apps });

        return (true, new { approved_apps = apps.Count });
    }

    private static List<KioskAppPayload>? ParseKioskApps(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
            return null;

        var apps = new List<KioskAppPayload>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                return null;

            if (!item.TryGetProperty("name", out var nameEl) || !item.TryGetProperty("path", out var pathEl))
                return null;

            var name = nameEl.GetString();
            var path = pathEl.GetString();
            if (string.IsNullOrWhiteSpace(name) || !KioskAppRegistry.IsValidPath(path))
                return null;

            var args = item.TryGetProperty("args", out var argsEl) && argsEl.ValueKind == JsonValueKind.String
                ? argsEl.GetString()
                : null;
            var icon = item.TryGetProperty("icon", out var iconEl) && iconEl.ValueKind == JsonValueKind.String
                ? iconEl.GetString()
                : null;

            apps.Add(new KioskAppPayload
            {
                Name = name!.Trim(),
                Path = path!.Trim(),
                Args = args,
                Icon = icon,
            });
        }

        return apps;
    }

    private static List<GamingExtraGamePayload>? ParseExtraGames(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
            return null;

        var games = new List<GamingExtraGamePayload>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                return null;

            if (!item.TryGetProperty("exe", out var exeEl) || !item.TryGetProperty("name", out var nameEl))
                return null;

            var exe = exeEl.GetString();
            var name = nameEl.GetString();
            if (!GamingGameRegistry.IsValidExe(exe) || string.IsNullOrWhiteSpace(name))
                return null;

            games.Add(new GamingExtraGamePayload
            {
                Exe = exe!,
                Name = name!.Trim(),
            });
        }

        return games;
    }

    private static List<string>? ParseIgnoredGames(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
            return null;

        var ignored = new List<string>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                return null;

            var exe = item.GetString();
            if (!GamingGameRegistry.IsValidExe(exe))
                return null;

            ignored.Add(exe!);
        }

        return ignored;
    }

    private static Dictionary<string, int>? ParseGameLimits(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        var limits = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
        {
            if (!GamingGameRegistry.IsValidExe(property.Name))
                return null;

            var minutes = property.Value.ValueKind switch
            {
                JsonValueKind.Number when property.Value.TryGetInt32(out var number) => number,
                JsonValueKind.String when int.TryParse(property.Value.GetString(), out var parsed) => parsed,
                _ => -1,
            };

            if (minutes < 0 || minutes > 1440)
                return null;

            limits[property.Name] = minutes;
        }

        return limits;
    }

    private static Dictionary<string, int>? ParseWeeklyMinuteLimits(AgentCommand command)
    {
        if (command.Payload is null || !command.Payload.TryGetValue("weekly_limits", out var element))
            return null;

        if (element.ValueKind != JsonValueKind.Object)
            return null;

        var weekly = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
        {
            if (!DayScheduleKeys.TryParse(property.Name, out _))
                continue;

            var minutes = property.Value.ValueKind switch
            {
                JsonValueKind.Number when property.Value.TryGetInt32(out var number) => number,
                JsonValueKind.String when int.TryParse(property.Value.GetString(), out var parsed) => parsed,
                _ => (int?)null,
            };

            if (minutes is null)
                continue;

            weekly[property.Name] = minutes.Value;
        }

        return weekly.Count == 0 ? null : weekly;
    }

    private static bool ValidateWeeklyMinuteLimits(Dictionary<string, int> weekly, out string error)
    {
        foreach (var (_, minutes) in weekly)
        {
            if (minutes is < 1 or > 1440)
            {
                error = "invalid weekly_limits — each day must be 1-1440 minutes";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    private static Dictionary<string, BedtimeDayPayload>? ParseWeeklyBedtime(AgentCommand command)
    {
        if (command.Payload is null || !command.Payload.TryGetValue("weekly", out var element))
            return null;

        if (element.ValueKind != JsonValueKind.Object)
            return null;

        var weekly = new Dictionary<string, BedtimeDayPayload>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
        {
            if (!DayScheduleKeys.TryParse(property.Name, out _) || property.Value.ValueKind != JsonValueKind.Object)
                continue;

            bool? enabled = null;
            string? time = null;
            string? wakeTime = null;

            foreach (var dayProperty in property.Value.EnumerateObject())
            {
                switch (dayProperty.Name)
                {
                    case "enabled":
                        enabled = dayProperty.Value.ValueKind switch
                        {
                            JsonValueKind.True => true,
                            JsonValueKind.False => false,
                            JsonValueKind.String when bool.TryParse(dayProperty.Value.GetString(), out var parsed) => parsed,
                            _ => null,
                        };
                        break;
                    case "time":
                        time = dayProperty.Value.GetString();
                        break;
                    case "wake_time":
                        wakeTime = dayProperty.Value.GetString();
                        break;
                }
            }

            if (time is not null && !BedtimeSettings.TryParseTime(time, out _))
                continue;
            if (wakeTime is not null && !BedtimeSettings.TryParseTime(wakeTime, out _))
                continue;

            weekly[property.Name] = new BedtimeDayPayload
            {
                Enabled = enabled,
                Time = time,
                WakeTime = wakeTime,
            };
        }

        return weekly.Count == 0 ? null : weekly;
    }

    private static int? GetPayloadInt(AgentCommand command, string key)
    {
        if (command.Payload is null || !command.Payload.TryGetValue(key, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), out var parsed) => parsed,
            _ => null,
        };
    }

    private static double? GetPayloadDouble(AgentCommand command, string key)
    {
        if (command.Payload is null || !command.Payload.TryGetValue(key, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDouble(out var number) => number,
            JsonValueKind.String when double.TryParse(
                value.GetString(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsed) => parsed,
            _ => null,
        };
    }

    private static string? GetPayloadPin(AgentCommand command)
    {
        if (command.Payload is null || !command.Payload.TryGetValue("pin", out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => value.GetString(),
            _ => null,
        };
    }

    private static bool? GetPayloadBool(AgentCommand command, string key)
    {
        if (command.Payload is null || !command.Payload.TryGetValue(key, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            _ => null,
        };
    }

    private static string? GetPayloadString(AgentCommand command, string key)
    {
        if (command.Payload is null || !command.Payload.TryGetValue(key, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            _ => value.ToString(),
        };
    }

}
