# Lovable — Gaming time limit & game management for EduGuard

Copy the **Prompt for Lovable** section below into Lovable. The Windows agent (`EduGuardAgent`) already recognizes games locally, counts combined daily play time, enforces a **global daily limit**, and consumes `settings.gaming.daily_limit_minutes` + the `set_gaming_limit` command.

This prompt adds, on the Dom dashboard: **see the list of games**, **add** games, **remove/ignore** games, and **edit the limit**.

---

## Prompt for Lovable

Implement **gaming management** for EduGuard so a Dom can see which games the Sub plays, manage the recognized game list, and control the daily play-time limit.

### Product behavior

On the **agent detail / device dashboard**, add a **Play time** section with three parts:

1. **Today's game time (read-only, from the agent)**
   - Big total: `23m of 1h used today` + progress bar.
   - A **per-game list**: each game's display name + time played today, sorted by most played.
   - This list is built from data the **agent reports in its heartbeat** (see "Agent → server" below). Show an empty state when no games were played today.

2. **Manage games**
   - A list of **custom games** the Dom added (display name + Windows process name, e.g. `My Game` → `mygame.exe`).
   - **Add game**: form with `name` (free text) and `exe` (process name, must match `^[a-zA-Z0-9 ._-]+\.exe$`, lowercased).
   - **Remove game**: removes from custom list.
   - **Ignore a detected game**: from the "Today's game time" list, allow the Dom to mark a game as **not counted** (adds it to an `ignored_games` list). Allow un-ignoring.
   - When a game is ignored, the agent **removes that game's time from today's total** (tuile + détail + `gaming_usage` heartbeat) and stops counting it going forward.
   - The agent already recognizes ~35 popular games + anything launched from Steam/Epic/Riot/GOG folders. `extra_games` only **adds** titles the agent doesn't know; `ignored_games` **excludes** titles from counting.

3. **Edit the limit**
   - **Daily play-time limit** (global, across all games): integer minutes, `1`–`1440`. Default `60`.
   - **Save** → persist + push to agent immediately.

4. **In-game overlay toggle**
   - Toggle: **Show play-time overlay on games** (default **on**).
   - When enabled, the agent shows a small HUD **top-left over the game** while the Sub actively plays (game in foreground): countdown of remaining daily play time + progress bar.
   - When disabled, no HUD — play time still counts when the game is in the foreground.
   - Helper: *"The Sub sees how much play time is left while gaming. Turn off if you prefer a cleaner screen."*
   - **Save** → persist + push via heartbeat and `set_gaming_overlay` command.

### Persist per agent

```ts
gaming_daily_limit_minutes: number          // default 60, range 1..1440
gaming_extra_games: { exe: string; name: string }[]   // Dom-added games
gaming_ignored_games: string[]               // exe names excluded from counting
gaming_show_playtime_overlay: boolean        // default true
```

### Agent → server (heartbeat REQUEST) — new field to display the list

The agent will include its current daily usage on each heartbeat. Read it and store/show it (do not trust it as config, it's telemetry):

```json
{
  "focused_window": "...",
  "running_apps": ["..."],
  "is_idle": false,
  "level": "college_student",
  "gaming_usage": {
    "date": "2026-06-14",
    "total_seconds": 1380,
    "limit_minutes": 60,
    "games": [
      { "key": "valorant.exe", "name": "Valorant", "seconds": 900 },
      { "key": "minecraft.exe", "name": "Minecraft", "seconds": 480 }
    ]
  }
}
```

- `total_seconds` = play time today while a recognized game is **in the foreground** (resets at local midnight).
- `games[]` = per-game breakdown (only games played today).
- Use this to render the dashboard "Today's game time" + per-game list.

### Server → agent (heartbeat RESPONSE) — `settings.gaming`

Extend `POST /api/public/agent/heartbeat`:

```json
{
  "ok": true,
  "server_time": "2026-06-14T13:00:00Z",
  "settings": {
    "gaming": {
      "daily_limit_minutes": 60,
      "extra_games": [
        { "exe": "mygame.exe", "name": "My Game" }
      ],
      "ignored_games": ["minecraft.exe"],
      "show_playtime_overlay": true
    }
  }
}
```

- Send on every successful heartbeat while enrolled.
- `daily_limit_minutes`, `extra_games`, `ignored_games`, `show_playtime_overlay` — full replace / idempotent sync.
- Validate `exe` server-side: `^[a-z0-9 ._-]+\.exe$` (lowercase), `daily_limit_minutes` integer 1..1440.

### Commands (instant apply on Save)

Register in `src/lib/agent-protocol/schemas.ts` and capabilities.

1. `set_gaming_limit` — **already supported by the agent**:

```json
{
  "type": "set_gaming_limit",
  "label": "Set daily play time limit",
  "group": "gaming",
  "fields": [ { "name": "daily_limit_minutes", "type": "number" } ]
}
```

Queue on Save:

```json
{ "type": "set_gaming_limit", "payload": { "daily_limit_minutes": 90 } }
```

Agent reports: `{ "status": "done", "result": { "daily_limit_minutes": 90 } }`

2. `set_gaming_games` (full list replace):

```json
{
  "type": "set_gaming_games",
  "label": "Set managed games",
  "group": "gaming",
  "fields": [
    { "name": "extra_games", "type": "json" },
    { "name": "ignored_games", "type": "json" }
  ]
}
```

Queue on Save:

```json
{
  "type": "set_gaming_games",
  "payload": {
    "extra_games": [ { "exe": "mygame.exe", "name": "My Game" } ],
    "ignored_games": ["minecraft.exe"]
  }
}
```

3. `set_gaming_overlay` (instant toggle):

```json
{
  "type": "set_gaming_overlay",
  "label": "Show in-game play time overlay",
  "group": "gaming",
  "fields": [ { "name": "show_playtime_overlay", "type": "boolean" } ]
}
```

Queue on Save:

```json
{ "type": "set_gaming_overlay", "payload": { "show_playtime_overlay": false } }
```

Agent reports: `{ "status": "done", "result": { "show_playtime_overlay": false } }`

### Agent-side status

| Feature | Agent status |
|---------|--------------|
| Local game recognition (catalog + Steam/Epic/Riot/GOG heuristic) | ✅ implemented |
| Count play time **only when game is in foreground** | ✅ implemented |
| In-game HUD overlay (countdown + progress bar, top-left) | ✅ implemented |
| `show_playtime_overlay` toggle (heartbeat + command) | ✅ implemented |
| Enforce **global** daily limit (closes game + notifies) | ✅ implemented |
| Report `gaming_usage` in heartbeat | ✅ implemented |
| `extra_games` / `ignored_games` + purge ignored time | ✅ implemented |

### API checklist

- [ ] DB: `gaming_daily_limit_minutes`, `gaming_extra_games`, `gaming_ignored_games`, `gaming_show_playtime_overlay` per agent
- [ ] Dom UI: Play time section — today's per-game list, add/remove/ignore games, limit editor, **overlay toggle**
- [ ] Heartbeat **stores** `gaming_usage` from the agent and renders it
- [ ] Heartbeat **returns** `settings.gaming` (all fields including `show_playtime_overlay`)
- [ ] Commands: `set_gaming_limit`, `set_gaming_games`, `set_gaming_overlay` + capabilities entries
- [ ] Validate `daily_limit_minutes` (1..1440) and `exe` (`^[a-z0-9 ._-]+\.exe$`)

### UX copy (Dom dashboard)

- Section title: **Play time**
- Today block: **Games today** — `{used} of {limit} used`
- Manage block: **Recognized games** / **Add a game** / **Stop counting** (ignore)
- Limit field: **Daily play time limit (minutes)**
- Overlay toggle: **Show play-time overlay on games**
- Saved toast: **Play time settings updated — Guardi will apply them right away.**

### Example layout

```
┌──────────────────────────────────────────────┐
│ Play time                                      │
│ Games today: 23m of 1h  [▓▓▓▓░░░░░░]           │
│  • Valorant     15m                            │
│  • Minecraft     8m                            │
│                                                │
│ Daily limit: [ 60 ] min                        │
│ [✓] Show play-time overlay on games            │
│ [ Save ]                                       │
│                                                │
│ Recognized games (custom)                      │
│  • My Game (mygame.exe)            [Remove]     │
│  [ + Add a game ]                              │
└──────────────────────────────────────────────┘
```

---

## Protocol reference

| Mechanism | Fields |
|-----------|--------|
| Heartbeat request `gaming_usage` | `date`, `total_seconds`, `limit_minutes`, `games[] { key, name, seconds }` |
| Heartbeat response `settings.gaming` | `daily_limit_minutes`, `extra_games[]`, `ignored_games[]`, `show_playtime_overlay` |
| Command `set_gaming_limit` | `{ "daily_limit_minutes" }` |
| Command `set_gaming_games` | `{ "extra_games[]", "ignored_games[]" }` |
| Command `set_gaming_overlay` | `{ "show_playtime_overlay" }` |

Protocol version: **2**.
