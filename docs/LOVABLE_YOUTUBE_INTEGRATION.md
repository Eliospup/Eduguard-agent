# Lovable — YouTube time limit for EduGuard

Copy the **Prompt for Lovable** section below into Lovable. The Windows agent (`EduGuardAgent`) already counts daily YouTube watch time, enforces a **global daily limit**, shows an **in-browser overlay**, and consumes `settings.youtube` + the `set_youtube_*` commands.

---

## Prompt for Lovable

Implement **YouTube time management** for EduGuard so a Dom can see how much YouTube the Sub watched today and control the daily limit + overlay.

### Product behavior

On the **agent detail / device dashboard**, add a **YouTube time** section with four parts:

1. **Today's YouTube time (read-only, from the agent)**
   - Big total: `12m of 30m used today` + progress bar.
   - This is built from data the **agent reports in its heartbeat** (see "Agent → server" below). Show an empty state when no YouTube was watched today.
   - Helper: *"Time counts only when a YouTube tab is open, in the foreground, and the window is not minimized (windowed or fullscreen both count)."*

2. **Edit the limit**
   - **Daily YouTube limit** (global): integer minutes, `1`–`1440`. Default `30`.
   - **Save** → persist + push to agent immediately.

3. **Overlay toggle**
   - Toggle: **Show YouTube time overlay** (default **on**).
   - When enabled, the agent shows a small HUD **top-left over the browser/app** while the Sub actively watches YouTube (YouTube tab in foreground, window not minimized): countdown of remaining daily time + progress bar.
   - When disabled, no HUD — YouTube time still counts when conditions above are met.
   - Helper: *"The Sub sees how much YouTube time is left while watching. Turn off if you prefer a cleaner screen."*
   - **Save** → persist + push via heartbeat and `set_youtube_overlay` command.

4. **Restricted mode toggle** (optional, default **off**)
   - Toggle: **YouTube restricted mode** — locks strict YouTube filtering in Chrome, Edge and Brave via browser policy (`ForceYouTubeRestrict = 2`).
   - Independent from SafeSearch (no longer applied automatically with SafeSearch).
   - Helper: *"Hides or limits mature YouTube content in supported Chromium browsers. Requires Guardi to run as administrator. Restart browsers after enabling."*
   - **Save** → persist + push via heartbeat and `set_youtube_restricted_mode` command.

### How the agent detects YouTube

- **Browsers** (Chrome, Edge, Firefox, Brave, Opera, Vivaldi, Arc): foreground window title contains ` - YouTube` or equals `YouTube`.
- **YouTube desktop app** (`youtube.exe`): detected by process name.
- **Does NOT count** when the browser/app is **minimized** (even if a YouTube tab is open).
- **Does count** in **windowed** and **fullscreen** modes, as long as the window is foreground and not minimized.
- At limit: agent **closes the browser/app process** and shows a popup (same enforcement model as gaming).

### Persist per agent

```ts
youtube_daily_limit_minutes: number       // default 30, range 1..1440
youtube_show_overlay: boolean             // default true
youtube_restricted_mode_enabled: boolean  // default false
```

### Agent → server (heartbeat REQUEST) — new field

The agent includes daily YouTube usage on each heartbeat:

```json
{
  "focused_window": "...",
  "running_apps": ["..."],
  "is_idle": false,
  "level": "college_student",
  "youtube_usage": {
    "date": "2026-06-14",
    "total_seconds": 720,
    "limit_minutes": 30
  }
}
```

- `total_seconds` = YouTube time today while YouTube is **in the foreground and not minimized** (resets at local midnight).
- Use this to render the dashboard "Today's YouTube time" block.

### Server → agent (heartbeat RESPONSE) — `settings.youtube`

Extend `POST /api/public/agent/heartbeat`:

```json
{
  "ok": true,
  "server_time": "2026-06-14T13:00:00Z",
  "settings": {
    "youtube": {
      "daily_limit_minutes": 30,
      "show_overlay": true,
      "restricted_mode_enabled": false
    }
  }
}
```

- Send on every successful heartbeat while enrolled.
- `daily_limit_minutes`, `show_overlay`, `restricted_mode_enabled` — full replace / idempotent sync.
- Validate `daily_limit_minutes` integer 1..1440.

### Commands (instant apply on Save)

Register in `src/lib/agent-protocol/schemas.ts` and capabilities.

1. `set_youtube_limit`:

```json
{
  "type": "set_youtube_limit",
  "label": "Set daily YouTube time limit",
  "group": "youtube",
  "fields": [ { "name": "daily_limit_minutes", "type": "number" } ]
}
```

Queue on Save:

```json
{ "type": "set_youtube_limit", "payload": { "daily_limit_minutes": 45 } }
```

Agent reports: `{ "status": "done", "result": { "daily_limit_minutes": 45 } }`

2. `set_youtube_overlay` (instant toggle):

```json
{
  "type": "set_youtube_overlay",
  "label": "Show YouTube time overlay",
  "group": "youtube",
  "fields": [ { "name": "show_overlay", "type": "boolean" } ]
}
```

Queue on Save:

```json
{ "type": "set_youtube_overlay", "payload": { "show_overlay": false } }
```

Agent reports: `{ "status": "done", "result": { "show_overlay": false } }`

3. `set_youtube_restricted_mode` (instant toggle):

```json
{
  "type": "set_youtube_restricted_mode",
  "label": "YouTube restricted mode",
  "group": "youtube",
  "fields": [ { "name": "restricted_mode_enabled", "type": "boolean" } ]
}
```

Queue on Save:

```json
{ "type": "set_youtube_restricted_mode", "payload": { "restricted_mode_enabled": true } }
```

Agent reports: `{ "status": "done", "result": { "restricted_mode_enabled": true } }`

### Agent-side status

| Feature | Agent status |
|---------|--------------|
| Detect YouTube in browser title (Chrome, Edge, Firefox, etc.) | ✅ implemented |
| Detect YouTube desktop app | ✅ implemented |
| Count time only when YouTube is foreground **and not minimized** | ✅ implemented |
| Works in windowed and fullscreen (not minimized) | ✅ implemented |
| HUD overlay (countdown + progress bar, top-left) | ✅ implemented |
| `show_overlay` toggle (heartbeat + command) | ✅ implemented |
| YouTube restricted mode toggle (Chrome/Edge/Brave policy, default off) | ✅ implemented |
| Enforce global daily limit (closes app + notifies) | ✅ implemented |
| Block YouTube during study time | ✅ implemented |
| Report `youtube_usage` in heartbeat | ✅ implemented |

### API checklist

- [ ] DB: `youtube_daily_limit_minutes`, `youtube_show_overlay`, `youtube_restricted_mode_enabled` per agent
- [ ] Dom UI: YouTube time section — today's usage, limit editor, **overlay toggle**, **restricted mode toggle**
- [ ] Heartbeat **stores** `youtube_usage` from the agent and renders it
- [ ] Heartbeat **returns** `settings.youtube` (all fields)
- [ ] Commands: `set_youtube_limit`, `set_youtube_overlay`, `set_youtube_restricted_mode` + capabilities entries
- [ ] Validate `daily_limit_minutes` (1..1440)

### UX copy (Dom dashboard)

- Section title: **YouTube time**
- Today block: **YouTube today** — `{used} of {limit} used`
- Limit field: **Daily YouTube limit (minutes)**
- Overlay toggle: **Show YouTube time overlay**
- Saved toast: **YouTube settings updated — Guardi will apply them right away.**

### Example layout

```
┌──────────────────────────────────────────────┐
│ YouTube time                                   │
│ YouTube today: 12m of 30m  [▓▓▓▓░░░░░░]        │
│                                                │
│ Daily limit: [ 30 ] min                        │
│ [✓] Show YouTube time overlay                  │
│ [ Save ]                                       │
└──────────────────────────────────────────────┘
```

---

## Protocol reference

| Mechanism | Fields |
|-----------|--------|
| Heartbeat request `youtube_usage` | `date`, `total_seconds`, `limit_minutes` |
| Heartbeat response `settings.youtube` | `daily_limit_minutes`, `show_overlay` |
| Command `set_youtube_limit` | `{ "daily_limit_minutes" }` |
| Command `set_youtube_overlay` | `{ "show_overlay" }` |

Protocol version: **2**.
