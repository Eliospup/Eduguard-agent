# Lovable — Bedtime & wake-up integration for EduGuard

Copy the **Prompt for Lovable** section below into Lovable. The Windows agent (`EduGuardAgent`) already supports bedtime lock, wake-up unlock, reminders, heartbeat `settings`, and the `set_bedtime` command.

---

## Prompt for Lovable

Implement **bedtime schedule** for EduGuard so a Dom controls when the Sub's protected PC **locks at night** and **unlocks in the morning**.

### Product behavior

1. On the **agent detail / device dashboard**, add a **Sleepy time** section:
   - Toggle: **Enable bedtime schedule** (default on for College Student)
   - Time picker **Bedtime** — 24-hour `HH:mm` (e.g. `23:00`) — when Guardi locks the screen
   - Time picker **Wake-up time** — 24-hour `HH:mm` (e.g. `07:00`) — when Guardi **automatically unlocks** the screen
   - Both times use the **Sub's local PC timezone** (`DateTime.Now` on the agent — no UTC conversion unless you later store agent timezone)
   - Helper text: *"Guardi locks at bedtime, unlocks at wake-up. The Sub gets reminders 1h, 30m, and 5m before bedtime."*
   - **Save** → persist to DB + push to agent immediately

2. **Persist per agent**:
   ```ts
   bedtime_enabled: boolean
   bedtime_time: string      // "HH:mm" e.g. "23:00"
   wake_time: string         // "HH:mm" e.g. "07:00"
   ```

3. **Heartbeat response** — extend `POST /api/public/agent/heartbeat`:

   ```json
   {
     "ok": true,
     "server_time": "2026-06-14T21:00:00Z",
     "settings": {
       "bedtime": {
         "enabled": true,
         "time": "23:00",
         "wake_time": "07:00"
       }
     }
   }
   ```

   - Send on every successful heartbeat while enrolled
   - If disabled: `"enabled": false`, omit times or send `null`
   - Validate: `wake_time` must differ from `time` for overnight schedules (typical: bedtime 22:00–23:59, wake 05:00–09:00)

4. **Command `set_bedtime`** (instant apply on Save):

   Register in `src/lib/agent-protocol/schemas.ts` and capabilities:

   ```json
   {
     "type": "set_bedtime",
     "label": "Set bedtime schedule",
     "group": "schedule",
     "fields": [
       { "name": "enabled", "type": "boolean" },
       { "name": "time", "type": "string" },
       { "name": "wake_time", "type": "string" }
     ]
   }
   ```

   Queue on Save:

   ```json
   {
     "type": "set_bedtime",
     "payload": {
       "enabled": true,
       "time": "23:00",
       "wake_time": "07:00"
     }
   }
   ```

   Agent reports: `{ "status": "done", "result": { "enabled": true, "time": "23:00", "wake_time": "07:00" } }`

### Agent-side behavior (already implemented)

| Event | Agent behavior |
|--------|----------------|
| **Bedtime** (`time`) | Fullscreen lock, moon icon, goodnight message |
| **1h / 30m / 5m before bedtime** | Centered toast 3 seconds (once each per night) |
| **Wake-up** (`wake_time`) | **Automatic unlock** — overlay closes, Sub can use PC again |
| **Overnight window** | e.g. 23:00 → 07:00: locked between bedtime and wake-up |
| **Default** (before sync) | Bedtime `23:00`, wake `07:00`, enabled |

The agent does **not** unlock at midnight anymore — only at the Dom's **wake_time**.

### API checklist

- [ ] DB: `bedtime_enabled`, `bedtime_time`, `wake_time` per agent
- [ ] Dom UI: bedtime + wake-up pickers on device page
- [ ] Heartbeat returns `settings.bedtime` with `wake_time`
- [ ] `set_bedtime` command includes `wake_time`
- [ ] Capabilities lists `set_bedtime` with all three fields
- [ ] Validate both times: `^([01]\d|2[0-3]):[0-5]\d$`

### UX copy (Dom dashboard)

- Section title: **Sleepy time**
- Bedtime label: **Lock computer at**
- Wake label: **Unlock computer at**
- Helper: *"Between these times the screen stays locked. Guardi unlocks automatically at wake-up."*
- Saved toast: **Schedule updated — Guardi will lock and unlock on time.**

### Example layout

```
┌─────────────────────────────────────┐
│ Sleepy time                         │
│ [✓] Enable bedtime schedule         │
│ Lock at:    [ 23:00 ▼ ]             │
│ Unlock at:  [ 07:00 ▼ ]             │
│ Reminders 1h, 30m, 5m before lock.  │
│ [ Save schedule ]                   │
└─────────────────────────────────────┘
```

### Testing with the Windows agent

1. Set bedtime **2 min from now**, wake **5 min after bedtime**
2. Heartbeat includes `wake_time`
3. At bedtime → lock overlay
4. At wake-up → **auto unlock** (no Dom command needed)
5. Change wake to later → agent picks up on next heartbeat/command

---

## Protocol reference

| Mechanism | Fields |
|-----------|--------|
| Heartbeat `settings.bedtime` | `enabled`, `time`, `wake_time` |
| Command `set_bedtime` | `{ "enabled", "time", "wake_time" }` |

Protocol version: **2**.
