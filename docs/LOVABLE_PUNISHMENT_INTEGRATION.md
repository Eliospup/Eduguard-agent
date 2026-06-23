# Lovable — Punishment / auto-escalation for EduGuard

Copy the **Prompt for Lovable** section below into Lovable. The Windows agent (`EduGuardAgent`) already detects infractions, auto-escalates the enforced strictness level locally, persists punishment state across restarts, reports it in the heartbeat, and consumes `settings.punishment` + the `set_punishment` / `reset_punishment` commands.

---

## Concept

- Modes, from most permissive to strictest: **Trusted Sub → Sub → Restricted Sub**.
- **Trusted Sub is the default base mode.**
- The agent counts **infractions** (attempts to bypass the app). After a configurable number of infractions, it **escalates one strictness level on its own**.
- The escalated level is a **floor**: the *effective* mode the Sub experiences is the **stricter of** the Dom-set base mode and the punishment floor.
  - The Dom can always make it **stricter** with `set_mode`.
  - A `set_mode` that is **less strict** than the active floor is **ignored** until the floor decays or the Dom resets it.
- **Time decay:** when an escalation threshold is reached, a **fixed escalation duration** is added (`escalation_hours` + `escalation_minutes`, default **6h**). When the timer elapses with no new infractions, the floor drops **one level**.
- **Two thresholds:** `threshold_trusted_to_sub` (default **3**) while floor is 0; `threshold_sub_to_restricted` (default **3**) while floor ≥ 1 (including re-triggers at max floor).
- **Who adds time:**
  - **Escalation** (threshold reached): adds only the fixed escalation duration — never per-infraction extension on that same event.
  - **Non-escalating infraction while floor = 0** (Trusted Sub, counting toward first escalation): **no extra time** — count only.
  - **Non-escalating infraction while floor ≥ 1** (already punished): adds per-kind extension (`infraction_extensions`, default **30 min** each).
  - **At max floor** (Restricted Sub): hitting the threshold again adds another escalation duration (extends punishment).
- Only **time** (good behavior) or an explicit **Dom reset** brings the level back down.

### What counts as an infraction (agent-side)

Each kind can be **enabled or disabled** by the Dom (`infraction_kinds`). Disabled kinds are ignored entirely — no count, no escalation, no heartbeat event.

| API key | Kind | Trigger | Debounce |
| --- | --- | --- | --- |
| `vpn_attempt` | `VpnAttempt` | Launching a VPN app (VPN shield kills it) | 30s per app |
| `blocked_app_repeated` | `BlockedAppRepeated` | Repeatedly relaunching a blocked app | counts once per **3 kills within 60s** |
| `bypass_attempt` | `BypassAttempt` | Blocked system tool killed (Task Manager, Registry Editor, …) or a failed Dom-PIN on a protected action (e.g. kiosk exit / unlink) | 30s per source |
| `limit_ignored` | `LimitIgnored` | Hitting the screen-time / gaming / YouTube limit and continuing | 5 min per source |
| `study_time_violation` | `StudyTimeViolation` | Launching a blocked app during study time | 30s per app |

All kinds default to **enabled** (`true`).

---

## Prompt for Lovable

Implement a **Punishment / auto-escalation** system for EduGuard so a Dom can configure how rule-breaking automatically tightens the enforced mode, see the current punishment state, and reset it.

### Product behavior

On the **agent detail / device dashboard**, add a **Discipline** section with:

1. **Current standing (read-only, from the agent)**
   - Show **base mode**, **effective mode**, and whether a punishment is active.
   - If punished: show *"Auto-escalated to {Effective} — eases off {relative time from `punishment_until`}"* and a small infraction feed (kind + detail + time) from `recent_infractions`.
   - Show **infractions toward next escalation**: `{infraction_count} / {infraction_threshold}`.

2. **Rules (editable, pushed to agent)**
   - **Enabled** (default **on**).
   - **Threshold Trusted Sub → Sub** (`threshold_trusted_to_sub`): integer `1`–`50`, default **3**.
   - **Threshold Sub → Restricted** (`threshold_sub_to_restricted`): integer `1`–`50`, default **3**.
   - **Escalation duration** (`escalation_hours` + `escalation_minutes`): fixed time added on each escalation (default **6h 0m**).
   - **Per-infraction extension** (`infraction_extensions`): hours + minutes per kind (default **0h 30m** each) — applied only while already punished and the infraction did **not** trigger escalation.
   - Legacy fields still accepted: `infraction_threshold` (both thresholds), `infraction_extension_hours` (all kinds).
   - **What counts as an infraction** (`infraction_kinds`) — toggles, all default **on**:
     - **VPN attempt** (`vpn_attempt`) — launching a VPN app blocked by the shield.
     - **Blocked app (repeated)** (`blocked_app_repeated`) — relaunching a blocked app several times in a row.
     - **Bypass attempt** (`bypass_attempt`) — opening blocked system tools (Task Manager, Registry, …) or failing the Dom PIN on a protected action.
     - **Limit ignored** (`limit_ignored`) — hitting screen-time, gaming, or YouTube limits and trying to keep going.
     - **Study time violation** (`study_time_violation`) — using a blocked app during study time.
     - Helper: *"Turn off a rule if you don't want that behaviour to count toward auto-escalation. The agent still blocks it — it just won't tighten the mode."*
   - **Save** → persist + push via heartbeat `settings.punishment` and/or the `set_punishment` command.

3. **Reset button**
   - **Clear punishment** → sends the `reset_punishment` command. Returns the Sub to the base mode and zeroes the infraction count.

### Persist per agent

```ts
punishment_enabled: boolean                  // default true
punishment_infraction_threshold: number      // default 3, range 1..50
punishment_escalation_hours: number          // default 6
punishment_infraction_extension_hours: number// default 2
punishment_infraction_kinds: {               // all default true
  vpn_attempt: boolean
  blocked_app_repeated: boolean
  bypass_attempt: boolean
  limit_ignored: boolean
  study_time_violation: boolean
}
```

### Agent → server (heartbeat REQUEST) — new field

The agent includes punishment state on each heartbeat (and drains any new infractions since the last heartbeat):

```json
{
  "focused_window": "...",
  "running_apps": ["..."],
  "is_idle": false,
  "level": "sub",
  "punishment": {
    "base_level": "trusted_sub",
    "effective_level": "sub",
    "floor_index": 1,
    "is_punished": true,
    "infraction_count": 1,
    "punishment_until": "2026-06-15T02:00:00.0000000+00:00",
    "seconds_until_decay": 19842,
    "recent_infractions": [
      { "kind": "vpn_attempt", "detail": "Tried to launch a VPN (NordVPN).", "at": "2026-06-14T20:01:11.0000000+00:00" }
    ]
  }
}
```

Idle example (no active floor — still sent every heartbeat):

```json
{
  "punishment": {
    "base_level": "trusted_sub",
    "effective_level": "trusted_sub",
    "floor_index": 0,
    "is_punished": false,
    "infraction_count": 0
  }
}
```

Notes:
- `level` already reflects the **effective** (enforced) mode.
- `floor_index`: `0` = no punishment, `1` = Sub floor, `2` = Restricted Sub floor.
- `punishment_until` is when the floor next decays one level (ISO 8601, UTC). `null` when not punished.
- `recent_infractions` is **incremental** — only events not yet reported. Append them to a server-side log; don't expect the full history each time.
- **Heartbeat `punishment` is runtime state only** (`base_level`, `effective_level`, `floor_index`, `is_punished`, `infraction_count`, optional `punishment_until`, optional `seconds_until_decay`, optional `recent_infractions`). Config mirrors (`enabled`, `infraction_threshold`, `infraction_kinds`) belong in **`settings.punishment`** / `set_punishment`, not in the heartbeat request. **Send the `punishment` block on every heartbeat** once protocol ≥ 3 — even when idle (`floor_index: 0`) — so the dashboard never keeps stale punishment state.
- `recent_infractions[].kind` uses snake_case API keys (`vpn_attempt`, `blocked_app_repeated`, …).
- `is_punished` is `true` when `floor_index` is stricter than `base_level` (auto-escalation floor active).
- `seconds_until_decay` is seconds until the floor drops one level (0 when the timer has elapsed but the agent has not ticked yet). Prefer this for countdown UI; fall back to parsing `punishment_until` (UTC ISO 8601).

**Protocol gate:** the agent only includes the `punishment` block in heartbeat requests once the server advertises **`protocol_version` ≥ 3** on `/api/public/agent/capabilities`. Until then, punishment runs locally and `set_punishment` / `reset_punishment` commands still work — the agent simply omits unknown telemetry so heartbeats stay compatible with v2 backends.

### Server → agent (heartbeat RESPONSE settings) — new block

Bump **`protocol_version` to `3`** on `/api/public/agent/capabilities` when this schema ships so agents start sending `punishment` telemetry.

```json
{
  "ok": true,
  "settings": {
    "punishment": {
      "enabled": true,
      "infraction_threshold": 3,
      "escalation_hours": 6,
      "infraction_extension_hours": 2,
      "infraction_kinds": {
        "vpn_attempt": true,
        "blocked_app_repeated": true,
        "bypass_attempt": true,
        "limit_ignored": true,
        "study_time_violation": true
      }
    }
  }
}
```

All fields are optional; the agent merges any provided field over its current config. For `infraction_kinds`, only the keys you send are updated — omitted keys keep their current value.

### Commands

- `set_punishment` — payload (any subset):
  - `enabled` (bool)
  - `infraction_threshold` (1–50)
  - `escalation_hours` (0–720)
  - `infraction_extension_hours` (0–720)
  - `infraction_kinds` (object with any subset of the five boolean keys above)
- `reset_punishment` — no payload. Clears the floor and infraction count immediately.

Example `set_punishment` to disable study-time and limit infractions only:

```json
{
  "type": "set_punishment",
  "payload": {
    "infraction_kinds": {
      "study_time_violation": false,
      "limit_ignored": false
    }
  }
}
```

### UX copy suggestions

- Section title: **Discipline**
- Enabled toggle helper: *"When the Sub keeps breaking rules, Guardi automatically tightens the mode. Good behavior relaxes it over time."*
- Reset confirm: *"Clear the current punishment and reset the infraction count back to the base mode?"*
- Standing (punished): *"Escalated to {Effective} after repeated infractions — eases off automatically around {time}."*

---

## Prompt Lovable — harmoniser l'état punition (dashboard ↔ agent)

**Problème observé :** le dashboard affiche Trusted Sub (mode Dom / base) alors que l'agent applique encore Sub ou Restricted Sub (punition locale persistante). Le compte à rebours de dé-escalade n'est pas aligné.

**Règle d'or : l'agent est la source de vérité pour l'état runtime.** Le dashboard ne doit jamais déduire seul que la punition est terminée.

### Ce que le heartbeat envoie (protocol ≥ 3)

À **chaque** heartbeat, le bloc `punishment` est présent :

| Champ | Usage dashboard |
| --- | --- |
| `base_level` | Mode configuré par le Dom (ex. `trusted_sub`) — affichage secondaire |
| `effective_level` | **Mode réellement appliqué** — à afficher comme mode courant |
| `floor_index` | `0` = pas de punition, `1` = Sub, `2` = Restricted Sub |
| `is_punished` | `true` si `floor_index` > strictness(`base_level`) |
| `infraction_count` / seuil config | Progression vers la prochaine escalade |
| `punishment_until` | UTC ISO — instant de la prochaine dé-escalade d'un cran |
| `seconds_until_decay` | Compte à rebours prêt à l'emploi (secondes) |
| `recent_infractions` | Journal incrémental à append côté serveur |

Le champ racine `level` du heartbeat = `effective_level` (même valeur).

### À implémenter côté serveur / UI

1. **Persister un snapshot** `agent_punishment_state` mis à jour à chaque heartbeat (pas seulement à l'escalade).
2. **Afficher `effective_level`**, pas le slug mode Dom seul, dans la carte device / Discipline.
3. **Ne pas faire de dé-escalade côté serveur** en comparant `punishment_until` à l'heure serveur — seul l'agent décrémente le floor après bon comportement ; le dashboard reflète ce que l'agent rapporte.
4. **Compte à rebours** : utiliser `seconds_until_decay` (rafraîchi ~10 s) ou `punishment_until` en UTC ; masquer si `is_punished === false`.
5. **Quand `is_punished === false`** : afficher Trusted Sub / base mode, pas une punition fantôme.
6. **Protocol &lt; 3** : afficher un bandeau *"État de punition inconnu — l'agent peut différer du dashboard"* ; ne pas affirmer Trusted Sub comme mode effectif.
7. **`reset_punishment`** : seul moyen Dom d'annuler immédiatement ; attendre le prochain heartbeat pour confirmer `floor_index: 0`.

### Schéma Zod suggéré (heartbeat request `punishment`)

```ts
punishment: z.object({
  base_level: z.enum(['trusted_sub', 'sub', 'restricted_sub']),
  effective_level: z.enum(['trusted_sub', 'sub', 'restricted_sub']),
  floor_index: z.number().int().min(0).max(2),
  is_punished: z.boolean(),
  infraction_count: z.number().int().min(0),
  punishment_until: z.string().datetime().nullable().optional(),
  seconds_until_decay: z.number().int().min(0).nullable().optional(),
  recent_infractions: z.array(z.object({
    kind: z.string(),
    detail: z.string(),
    at: z.string().datetime(),
  })).optional(),
}).optional() // absent only when protocol < 3
```

### Copy UI Discipline (punie)

*"Mode appliqué : **{effective_level}** (base Dom : {base_level}) — dé-escalade dans **{countdown}**"*

Utiliser `formatDuration(seconds_until_decay)` côté client, recalculé à la réception de chaque heartbeat.
