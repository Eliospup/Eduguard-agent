# Lovable — Supervision modes for EduGuard

Copy the **Prompt for Lovable** section below into Lovable. The Windows agent (`EduGuardAgent`) now uses **3 supervision modes** instead of the old College Student level. Each mode changes **enforcement**, **UI tone/colors**, and has **configurable rules** from the Dom dashboard.

---

## Prompt for Lovable

Implement **supervision modes** for EduGuard. A Dom assigns one of three modes per protected PC. Each mode has its own default rules (editable per device), a distinct Sub-facing UI tone, and optional feature flags.

### The 3 modes

| Slug | Display name | Intent |
|------|--------------|--------|
| `trusted_sub` | **Trusted Sub** | More trust, lighter tone, fewer locks. Task Manager allowed. |
| `sub` | **Sub** | Standard supervision — infantilizing UI, Task Manager blocked, full shields. **Default** for new agents. |
| `restricted_sub` | **Restricted Sub** | Strict supervision (kiosk-style lockdown planned later). Task Manager blocked, tighter defaults. |

**Replaces** the old `college_student` level entirely. Heartbeat telemetry must use the mode `slug` in the existing `level` field.

### Product behavior (Dom dashboard)

On the **agent detail / device dashboard**, add a **Supervision mode** section:

1. **Mode picker** — radio or segmented control: Trusted Sub / Sub / Restricted Sub
2. **Per-mode rule editors** (values stored per agent, sent to agent on Save):
   - **Screen time** — daily limit minutes (`1`–`1440`)
   - **Play time** — daily gaming limit minutes (`1`–`1440`) — reuses existing gaming UI
   - **Bedtime** — enabled, start, wake — reuses existing bedtime UI
   - **Study time** — enabled, days, start, end — reuses existing study time UI
   - **Feature flags**:
     - `block_task_manager` (boolean) — default **off** for trusted_sub, **on** for sub & restricted_sub
     - `vpn_shield` (boolean) — default **on** for all modes
3. Helper: *"Changing mode updates the Sub's UI tone and default strictness. Rule values below apply while this mode is active."*
4. **Save** → persist mode + all rules + queue `set_mode` command

**Important:** Rules are **per agent** in the database. When the Dom switches mode, load that agent's saved rules for the target mode (or mode defaults if never configured). The heartbeat must always return the **active mode** plus the **effective rules** for that agent.

### Persist per agent

```ts
supervision_mode: 'trusted_sub' | 'sub' | 'restricted_sub'  // default 'sub'

// Effective rules for the active mode (store per mode or one active snapshot):
screen_time_daily_limit_minutes: number
gaming_daily_limit_minutes: number
bedtime_enabled: boolean
bedtime_time: string          // "HH:mm"
wake_time: string             // "HH:mm"
study_time_enabled: boolean
study_time_start: string
study_time_end: string
study_time_days: string[]     // ["mon","tue",...]
mode_block_task_manager: boolean
mode_vpn_shield: boolean
```

Recommended schema: either **one row per (agent, mode)** for rule templates, or a JSON column `mode_rules: Record<slug, RuleSet>`.

### Agent → server (heartbeat REQUEST)

Existing field — send the active mode slug:

```json
{
  "level": "sub",
  "focused_window": "...",
  "running_apps": ["..."],
  "gaming_usage": { ... }
}
```

Valid values: `trusted_sub`, `sub`, `restricted_sub`.

### Server → agent (heartbeat RESPONSE) — extend `settings`

Send on every successful heartbeat while enrolled:

```json
{
  "ok": true,
  "settings": {
    "mode": {
      "slug": "sub",
      "display_name": "Sub",
      "features": {
        "block_task_manager": true,
        "vpn_shield": true
      }
    },
    "screen_time": {
      "daily_limit_minutes": 300
    },
    "gaming": {
      "daily_limit_minutes": 60,
      "extra_games": [],
      "ignored_games": [],
      "show_playtime_overlay": true
    },
    "bedtime": {
      "enabled": true,
      "time": "23:00",
      "wake_time": "07:00"
    },
    "study_time": {
      "enabled": false,
      "start_time": "09:00",
      "end_time": "17:00",
      "days": ["mon", "tue", "wed", "thu", "fri"]
    }
  }
}
```

- `settings.mode.slug` — required when mode is assigned
- `settings.screen_time` — **new** — daily screen allowance
- Other blocks reuse existing protocols (see `LOVABLE_GAMING_INTEGRATION.md`, `LOVABLE_BEDTIME_INTEGRATION.md`, `LOVABLE_STUDY_TIME_INTEGRATION.md`)

### Command `set_mode` (instant apply on Save)

```json
{
  "type": "set_mode",
  "label": "Set supervision mode",
  "group": "mode",
  "fields": [
    { "name": "slug", "type": "string" },
    { "name": "display_name", "type": "string" },
    { "name": "features", "type": "json" },
    { "name": "daily_limit_minutes", "type": "number" }
  ]
}
```

Queue on Save (example — switching to Sub with screen limit):

```json
{
  "type": "set_mode",
  "payload": {
    "slug": "sub",
    "display_name": "Sub",
    "features": {
      "block_task_manager": true,
      "vpn_shield": true,
      "block_registry_editor": false,
      "block_command_prompt": false,
      "block_powershell": false,
      "block_system_config": false,
      "block_control_panel": false,
      "block_process_tools": false
    },
    "daily_limit_minutes": 300
  }
}
```

Also queue the existing commands when rules change: `set_bedtime`, `set_gaming_limit`, `set_study_time`, etc.

#### Security feature flags (all optional, default by mode)

Each flag, when `true`, makes the agent close the matching tool as soon as it launches.
All flags are toggleable from the web dashboard via `set_mode` (or `settings.mode.features`
on the heartbeat). Omitting a flag leaves the current value untouched.

| Flag | Locks | trusted_sub | sub | restricted_sub |
|------|-------|:-:|:-:|:-:|
| `block_task_manager` | `taskmgr.exe` | off | **on** | **on** |
| `block_registry_editor` | `regedit.exe` | off | off | **on** |
| `block_command_prompt` | `cmd.exe` | off | off | **on** |
| `block_powershell` | `powershell.exe`, `pwsh.exe`, `powershell_ise.exe` | off | off | **on** |
| `block_system_config` | `msconfig.exe`, `mmc.exe` | off | off | **on** |
| `block_control_panel` | `control.exe`, `systemsettings.exe` | off | off | off |
| `block_process_tools` | Process Explorer / Process Hacker / System Informer / Procmon / PC Hunter | off | off | **on** |
| `kiosk_mode` | Locked full-screen EduGuard shell (see below) | off | off | off (Dom enables) |
| `vpn_shield` | VPN clients (existing) | on | on | on |

#### Always-on protection (not configurable)

Independently of the flags above and of the mode, the agent protects itself from being
closed via Task Manager. There is no flag to disable this — it applies to every mode.

Two layers:

1. **Deny-terminate ACE** on its own process — blocks "End task" with access denied for a
   non-elevated Task Manager.
2. **Mutual watchdog** — a second hidden instance (`--watchdog`) relaunches the agent the
   instant it is killed, and the agent relaunches the watchdog if that one is killed. Each
   side is started through a throwaway launcher so it is orphaned and stays out of the
   other's process tree (an "End process tree" on one does not take the other down), while
   keeping its elevated token. This survives an **elevated** Task Manager kill (which can
   bypass layer 1 via `SeDebugPrivilege`).

A Dom-sanctioned quit (PIN exit) signals an intentional-shutdown event so neither side
resurrects the other. For local development, set the environment variable
`EDUGUARD_NO_GUARD=1` to disable resurrection.

> Expect **two** `EduGuardAgent.exe` entries in Task Manager — the agent and its watchdog.

#### Kiosk mode (`kiosk_mode`, restricted_sub)

When `kiosk_mode` is `true` and the agent is enrolled, EduGuard shows a **full-screen shell**
(home screen + app tiles) with the taskbar hidden. Approved apps open in **normal windows**
on top — not forced fullscreen. The child launches apps from the kiosk tiles. Only
Dom-approved executables may stay in the foreground.

**Hardening (agent-side):** taskbar re-hidden every guard tick (including secondary monitors),
Win/Alt shortcuts blocked, unauthorized windows minimized, EduGuard re-maximized if resized or
minimized, hidden from the taskbar while kiosk is active.

> **Limitations (cannot be fully closed in user space):**
> - `Ctrl+Alt+Del` / Sign out / Switch user (Secure Attention Sequence)
> - A second physical PC or VM with its own session
> - Dom PIN via **Ask to leave** (intentional escape hatch)
> - `EDUGUARD_NO_GUARD=1` disables the resurrection watchdog (development only)
>
> For development, set env var `EDUGUARD_NO_GUARD=1` to also disable the resurrection watchdog.

##### Approved apps

The list of apps the child may launch in the kiosk. Synced on the heartbeat under
`settings.kiosk.approved_apps`, and/or pushed with the `set_kiosk_apps` command. Persisted
locally so the kiosk keeps working offline.

On first run (empty list), the agent **auto-detects common productivity apps** on the PC
(Chrome, Edge, Firefox, Office, Notepad, Zoom, Teams, VLC, etc.) and **whitelists them by
default**. Newly installed catalog apps are merged in automatically. Dom can review and toggle
apps in **Local settings → Kiosk apps** or via the web dashboard / `set_kiosk_apps`.

```json
{
  "kiosk": {
    "approved_apps": [
      { "name": "Navigateur", "path": "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe", "args": "--kiosk", "icon": "🌐" },
      { "name": "Bloc-notes", "path": "C:\\Windows\\System32\\notepad.exe", "icon": "📝" }
    ]
  }
}
```

- `name` (required), `path` (required, must end in `.exe`), `args` (optional), `icon` (optional emoji/char).
- An empty / missing list shows the "Aucune application autorisée" screen — nothing is launchable.

##### Command `set_kiosk_apps`

```json
{
  "type": "set_kiosk_apps",
  "payload": {
    "apps": [
      { "name": "Navigateur", "path": "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe", "args": "--kiosk", "icon": "🌐" }
    ]
  }
}
```

Agent reports: `{ "status": "done", "result": { "approved_apps": 1 } }`

Agent reports: `{ "status": "done", "result": { "slug": "sub", "daily_limit_minutes": 300 } }`

### Agent-side behavior (already implemented)

| Mode | UI tone | Theme | Task Manager | VPN shield (default) | Screen default | Gaming default |
|------|---------|-------|--------------|----------------------|----------------|----------------|
| `trusted_sub` | Mature, neutral | Teal | Allowed | On | 8h | 2h |
| `sub` | Infantilizing | Sky blue | **Blocked** | On | 5h | 1h |
| `restricted_sub` | Strict | Pastel blue | **Blocked** | On | 3h | 30m |

| Feature | Status |
|---------|--------|
| Mode picker in agent UI (3 steps) | ✅ implemented |
| Per-mode UI copy (tone) | ✅ implemented |
| Per-mode color theme | ✅ implemented |
| `settings.mode` + `settings.screen_time` sync | ✅ implemented |
| Command `set_mode` | ✅ implemented |
| Task Manager block (`taskmgr.exe`) | ✅ implemented (sub & restricted) |
| System tool locks (registry/cmd/powershell/msconfig/control panel/process tools) | ✅ implemented (configurable) |
| Anti-close protection (deny-terminate ACE, always on) | ✅ implemented |
| Kiosk lockdown for `restricted_sub` (`kiosk_mode` + `approved_apps`) | ✅ implemented (configurable, overlay-based) |
| Command `set_kiosk_apps` | ✅ implemented |

### Mode default rule suggestions (Dom can override)

| Rule | trusted_sub | sub | restricted_sub |
|------|-------------|-----|----------------|
| Screen time | 480 min | 300 min | 180 min |
| Gaming | 120 min | 60 min | 30 min |
| Bedtime | off | 23:00→07:00 | 22:00→07:00 |
| Study time | off | off | Mon–Fri 09:00–17:00 |
| Block Task Manager | no | **yes** | **yes** |

### API checklist

- [ ] DB: `supervision_mode` per agent + per-mode or active rule snapshot
- [ ] Dom UI: mode picker + rule editors (screen, gaming, bedtime, study, features)
- [ ] Heartbeat **reads** `level` slug for dashboard display
- [ ] Heartbeat **returns** `settings.mode`, `settings.screen_time`, plus existing settings blocks
- [ ] Command `set_mode` + capabilities entry
- [ ] On mode switch: send full rule bundle (mode + screen + gaming + bedtime + study commands)
- [ ] Deprecate `college_student` everywhere in web UI

### UX copy (Dom dashboard)

- Section title: **Supervision mode**
- Picker labels: **Trusted Sub** / **Sub** / **Restricted Sub**
- Helper (Trusted): *"More freedom, mature UI, fewer built-in locks."*
- Helper (Sub): *"Standard supervision — playful UI, Task Manager blocked."*
- Helper (Restricted): *"Strict rules. Secure kiosk mode coming soon."*
- Saved toast: **Supervision mode updated — Guardi will apply the new rules right away.**

### Example layout

```
┌──────────────────────────────────────────────┐
│ Supervision mode                              │
│ ( ) Trusted Sub  (•) Sub  ( ) Restricted Sub  │
│                                               │
│ Screen allowance: [ 300 ] min                 │
│ Play time limit:  [ 60 ] min                  │
│ Bedtime: [✓] 23:00 → 07:00                    │
│ Study time: [ ] ...                           │
│ [✓] Block Task Manager  [✓] VPN shield        │
│ [ Save ]                                      │
└──────────────────────────────────────────────┘
```

### Testing with the Windows agent

1. Set mode to `sub` → sky-blue UI, infantilizing copy, Task Manager closes instantly
2. Switch to `trusted_sub` → teal theme, mature copy, Task Manager works
3. Change screen limit to 60 min → agent applies within next heartbeat/command
4. Heartbeat request shows `"level": "trusted_sub"` after switch

---

## Protocol reference

| Mechanism | Fields |
|-----------|--------|
| Heartbeat request `level` | `trusted_sub` \| `sub` \| `restricted_sub` |
| Heartbeat `settings.mode` | `slug`, `display_name?`, `features { block_task_manager, vpn_shield, block_registry_editor, block_command_prompt, block_powershell, block_system_config, block_control_panel, block_process_tools, kiosk_mode }` |
| Heartbeat `settings.screen_time` | `daily_limit_minutes` |
| Heartbeat `settings.kiosk` | `approved_apps [ { name, path, args?, icon? } ]` |
| Command `set_mode` | `{ slug, display_name?, features?, daily_limit_minutes? }` |
| Command `set_kiosk_apps` | `{ apps: [ { name, path, args?, icon? } ] }` |
| Existing settings | `bedtime`, `gaming`, `study_time`, `exit_pin` (unchanged) |

Protocol version: **2**.

## Related docs

- [LOVABLE_GAMING_INTEGRATION.md](./LOVABLE_GAMING_INTEGRATION.md)
- [LOVABLE_BEDTIME_INTEGRATION.md](./LOVABLE_BEDTIME_INTEGRATION.md)
- [LOVABLE_STUDY_TIME_INTEGRATION.md](./LOVABLE_STUDY_TIME_INTEGRATION.md)
