# Lovable — Study time integration for EduGuard

Copy the **Prompt for Lovable** section below into Lovable. The Windows agent (`EduGuardAgent`) supports **study mode**: during configurable days and hours, the Sub's PC enters **study mode** and blocks configurable distractions (games, YouTube, social sites, distraction apps).

---

## Prompt for Lovable

Implement **study time** for EduGuard so a Dom schedules focused homework hours when the Sub's protected PC enters **study mode**.

### Product behavior

On the **agent detail / device dashboard**, add a **Study time** section:

1. Toggle: **Enable study schedule** (default off until the Dom configures it)
2. Time picker **Start** — 24-hour `HH:mm` (e.g. `09:00`)
3. Time picker **End** — 24-hour `HH:mm` (e.g. `17:00`)
4. **Days of week** — multi-select checkboxes: Mon, Tue, Wed, Thu, Fri, Sat, Sun (default schedule)
5. **Per-day overrides** (optional) — toggle a weekday to set its own start/end; same pattern as bedtime weekly overrides
6. **Block toggles** (all default on):
   - Block games
   - Block YouTube
   - Block distracting sites (Discord, TikTok, Reddit, etc.)
   - Block distracting apps (Discord, Steam, Spotify, etc.)
7. Helper text: *"During study hours, Guardi blocks selected distractions. Screen time still counts; play time is not counted while games are blocked."*
8. **Save** → persist to DB + push to agent immediately

**Schedule rules (agent-side, already implemented):**

- Uses the **Sub's local PC timezone** (`DateTime.Now` on the agent)
- Default schedule: enabled **and** today is in `days` **and** current local time is inside `[start_time, end_time)`
- Per-day override: if `weekly[day]` exists with `enabled: true`, that day's `start_time` / `end_time` replace the defaults for that weekday
- Same-day window typical: `09:00` → `17:00`
- Overnight window supported: e.g. `22:00` → `06:00` (active from start until midnight, then from midnight until end)
- While study mode is active (per toggles):
  - Games killed + study-time popup
  - YouTube time not counted; overlay hidden when YouTube blocked
  - Distracting sites added to hosts block list for the session
  - Distracting apps killed on a 1s loop
- **Screen time** continues to count during study mode (unchanged)
- **Play time** is not counted while games are blocked during study
- Sub sees a toast when study starts (*"Focus until HH:mm"*) and when it ends

### Persist per agent

```ts
study_time_enabled: boolean
study_time_start: string       // "HH:mm" e.g. "09:00"
study_time_end: string         // "HH:mm" e.g. "17:00"
study_time_days: string[]      // e.g. ["mon","tue","wed","thu","fri"]
study_time_weekly: json | null // optional per-day overrides
study_time_block_games: boolean
study_time_block_youtube: boolean
study_time_block_distracting_sites: boolean
study_time_block_distracting_apps: boolean
```

Day tokens (lowercase, 3 letters): `sun`, `mon`, `tue`, `wed`, `thu`, `fri`, `sat`.

`study_time_weekly` shape (optional):

```json
{
  "mon": { "enabled": true, "start_time": "08:00", "end_time": "12:00" },
  "wed": { "enabled": true, "start_time": "14:00", "end_time": "18:00" }
}
```

### Heartbeat response — extend `POST /api/public/agent/heartbeat`

```json
{
  "ok": true,
  "server_time": "2026-06-14T14:30:00Z",
  "settings": {
    "study_time": {
      "enabled": true,
      "start_time": "09:00",
      "end_time": "17:00",
      "days": ["mon", "tue", "wed", "thu", "fri"],
      "weekly": {
        "wed": { "enabled": true, "start_time": "14:00", "end_time": "18:00" }
      },
      "block_games": true,
      "block_youtube": true,
      "block_distracting_sites": true,
      "block_distracting_apps": true
    }
  }
}
```

- Send on every successful heartbeat while enrolled
- If disabled: `"enabled": false` — omit times/days or send `null`
- Validate times: `^([01]\d|2[0-3]):[0-5]\d$`
- When `enabled: true`, require at least one day in `days` **or** at least one enabled entry in `weekly`

### Command `set_study_time` (instant apply on Save)

Register in `src/lib/agent-protocol/schemas.ts` and capabilities:

```json
{
  "type": "set_study_time",
  "label": "Set study time schedule",
  "group": "schedule",
  "fields": [
    { "name": "enabled", "type": "boolean" },
    { "name": "start_time", "type": "string" },
    { "name": "end_time", "type": "string" },
    { "name": "days", "type": "json" },
    { "name": "weekly", "type": "json" },
    { "name": "block_games", "type": "boolean" },
    { "name": "block_youtube", "type": "boolean" },
    { "name": "block_distracting_sites", "type": "boolean" },
    { "name": "block_distracting_apps", "type": "boolean" }
  ]
}
```

Queue on Save:

```json
{
  "type": "set_study_time",
  "payload": {
    "enabled": true,
    "start_time": "09:00",
    "end_time": "17:00",
    "days": ["mon", "tue", "wed", "thu", "fri"],
    "weekly": {
      "wed": { "enabled": true, "start_time": "14:00", "end_time": "18:00" }
    },
    "block_games": true,
    "block_youtube": true,
    "block_distracting_sites": true,
    "block_distracting_apps": true
  }
}
```

Agent reports:

```json
{
  "status": "done",
  "result": {
    "enabled": true,
    "start_time": "09:00",
    "end_time": "17:00",
    "days": ["mon", "tue", "wed", "thu", "fri"],
    "weekly": {
      "wed": { "enabled": true, "start_time": "14:00", "end_time": "18:00" }
    },
    "block_games": true,
    "block_youtube": true,
    "block_distracting_sites": true,
    "block_distracting_apps": true
  }
}
```

### Agent-side behavior (already implemented)

| Event | Agent behavior |
|--------|----------------|
| Study window starts | Toast: *"Study time started — Focus until HH:mm"* |
| Study window active | Block per toggles (games / YouTube / sites / apps) |
| Study window ends | Toast: *"Study time ended"*; temporary site blocks removed |
| Study window inactive | Normal play-time / YouTube rules apply |
| Play time during study | Not counted when games blocked |
| Screen time during study | **Still counted** |
| In-game HUD during study | Hidden when games blocked |
| Default (before sync) | Study schedule **disabled** |

**Popup copy (agent, Sub-facing) — blocked game/app:**

- Title: **Study time — no games!** (tone varies by supervision mode)
- Message: *"It's study time right now, little one! {Name} isn't allowed until your Dom's study window ends. Guardi tucked it away so you can focus."*

This is **different** from the play-time limit popup (*"Play time's up!"*).

### API checklist

- [ ] DB: study time fields per agent (see Persist section)
- [ ] Dom UI: Study time section — enable, start/end, days, weekly overrides, block toggles
- [ ] Heartbeat returns `settings.study_time` on every successful response
- [ ] Command `set_study_time` + capabilities entry (all fields)
- [ ] Validate times (`HH:mm`) and days (`sun`…`sat`, at least one day or weekly entry when enabled)

### UX copy (Dom dashboard)

- Section title: **Study time**
- Toggle: **Enable study schedule**
- Start label: **Study starts at**
- End label: **Study ends at**
- Days label: **Active on**
- Weekly overrides: **Custom hours per day** (optional)
- Block section: **During study hours, block:**
- Helper: *"Screen time still counts. Play time pauses only when games are blocked."*
- Saved toast: **Study schedule updated — Guardi will enforce focus rules during study hours.**

### Example layout

```
┌─────────────────────────────────────┐
│ Study time                          │
│ [✓] Enable study schedule           │
│ Starts at:  [ 09:00 ▼ ]             │
│ Ends at:    [ 17:00 ▼ ]             │
│ Active on:  [✓]Mon [✓]Tue [✓]Wed     │
│             [✓]Thu [✓]Fri [ ]Sat [ ]Sun │
│ Per-day overrides (optional)        │
│ [✓] Wed  14:00 → 18:00              │
│ During study, block:                │
│ [✓] Games  [✓] YouTube              │
│ [✓] Social sites  [✓] Distraction apps │
│ [ Save schedule ]                   │
└─────────────────────────────────────┘
```

### Testing with the Windows agent

1. Set study window **2 min from now** → **10 min later**, today checked
2. Save → heartbeat/command includes full `settings.study_time`
3. Launch a game during the window → game closes + **study time** popup (not play limit)
4. Open Discord / YouTube (if toggles on) → blocked or closed
5. Check restrictions tile: *"Study until HH:mm — games, YouTube, … blocked"*
6. After window ends → end toast + normal rules
7. Disable schedule → study rules never apply

---

## Protocol reference

| Mechanism | Fields |
|-----------|--------|
| Heartbeat `settings.study_time` | `enabled`, `start_time`, `end_time`, `days[]`, `weekly?`, `block_games`, `block_youtube`, `block_distracting_sites`, `block_distracting_apps` |
| Command `set_study_time` | Same fields as heartbeat payload |

Protocol version: **2**.
