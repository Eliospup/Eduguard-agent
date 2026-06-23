# Lovable — Weekly per-day limits (bedtime, screen, gaming, YouTube)

Copy the **Prompt for Lovable** section below. The Windows agent already applies per-day overrides; the web app must let the Dom edit them and push via heartbeat `settings` + commands.

---

## Prompt for Lovable

Add **per-day schedules** for bedtime, screen time, gaming time, and YouTube time on the agent dashboard. Same day keys as study time: `sun`, `mon`, `tue`, `wed`, `thu`, `fri`, `sat`.

### UI

For each section (Sleepy time, Screen time, Gaming, YouTube):

1. Keep the existing **default** fields (bedtime/wake times or daily minute cap).
2. Add a **Weekly schedule** tab or expandable grid: 7 rows (Sun–Sat).
   - **Bedtime:** per day — optional override toggle, bedtime `HH:mm`, wake `HH:mm` (inherit default when row empty).
   - **Screen / Gaming / YouTube:** per day — optional minute cap (inherit `daily_limit_minutes` when empty).
3. Show **today's effective limit** read-only from heartbeat usage (`limit_minutes` on gaming/youtube/screen payloads).
4. **Save** → persist per agent + push immediately.

### Data model (per agent)

```ts
// defaults (existing)
bedtime_enabled, bedtime_time, wake_time
screen_time_daily_limit_minutes
gaming_daily_limit_minutes
youtube_daily_limit_minutes

// new JSON columns (nullable)
bedtime_weekly: Record<string, { enabled?: boolean; time?: string; wake_time?: string }>
screen_time_weekly_limits: Record<string, number>   // minutes 1–1440
gaming_weekly_limits: Record<string, number>
youtube_weekly_limits: Record<string, number>
```

### Heartbeat `settings` (merge into existing blocks)

```json
{
  "settings": {
    "bedtime": {
      "enabled": true,
      "time": "23:00",
      "wake_time": "07:00",
      "weekly": {
        "fri": { "time": "22:00", "wake_time": "08:00" },
        "sun": { "enabled": false }
      }
    },
    "screen_time": {
      "daily_limit_minutes": 300,
      "weekly_limits": { "sat": 480, "sun": 480 }
    },
    "gaming": {
      "daily_limit_minutes": 60,
      "weekly_limits": { "sat": 120 }
    },
    "youtube": {
      "daily_limit_minutes": 30,
      "weekly_limits": { "sat": 60 }
    }
  }
}
```

Omit `weekly` / `weekly_limits` when no overrides → agent uses defaults only.

### Commands (queue on Save)

| Command | Payload fields |
| --- | --- |
| `set_bedtime` | `enabled`, `time`, `wake_time`, optional `weekly` object |
| `set_mode` | `screen_time: { daily_limit_minutes, weekly_limits }` (with mode slug/features as today) |
| `set_gaming_limit` | `daily_limit_minutes`, optional `weekly_limits` |
| `set_youtube_limit` | `daily_limit_minutes`, optional `weekly_limits` |

Example `set_bedtime` weekly override:

```json
{
  "type": "set_bedtime",
  "payload": {
    "enabled": true,
    "time": "23:00",
    "wake_time": "07:00",
    "weekly": {
      "fri": { "time": "22:00", "wake_time": "08:00" },
      "sun": { "enabled": false }
    }
  }
}
```

### Rules

- Top-level values = **default** for all days; per-day keys **override** only that day.
- Minute limits: each override `1`–`1440`.
- Bedtime per day: partial override allowed (`time` only, `wake_time` only, or `enabled: false` to disable bedtime that day).
- Agent resolves **today's** limit at local midnight; heartbeat usage already reports `limit_minutes` for the current day.
