# Lovable — Image Shield (NSFW blur) for EduGuard

Copy the **Prompt for Lovable** section below. The Windows agent force-installs the
Guardi Image Shield browser extension when enabled, consumes `settings.image_shield`
on heartbeat, and accepts instant `set_image_shield` commands.

**Current agent capabilities (June 2025):**

| Browser | Dom can enable? | Agent enforces? | Notes |
|---------|-----------------|-----------------|-------|
| **Firefox** | Yes | Yes | Requires **Firefox Developer Edition** on the Sub's PC (unsigned local XPI). Show a confirmation popup when enabling. |
| Chrome | No (WIP) | No | Chrome Web Store review pending — toggles disabled in UI. |
| Edge | No (WIP) | No | Same as Chrome — work in progress. |
| Brave | No (WIP) | No | Same as Chrome — work in progress. |

The agent reports live status in every heartbeat under `image_shield` (see below).

**Runtime model (Firefox Dev, v0.7.2+):** the extension stays installed in the
browser but is **inactive** (no blur, no Guardi UI/theme) when Guardi is not
running or when Image Shield is disabled from the Dom dashboard. Guardi toggles
`shieldActive` via browser managed storage — no uninstall on Guardi exit.

---

## Prompt for Lovable

Add an **Image Shield** control to EduGuard so a Dom can turn on real-time
blurring of inappropriate images in the Sub's browser, **per supervision mode**
and **per browser**, and tune sensitivity.

### Product behavior

On the agent detail / device dashboard, add an **Image Shield** section:

#### 1. Master toggle — **Enable image shield**

- Default: **off** for newly linked agents (Dom must opt in).
- Helper text: *"Guardi blurs inappropriate images in the browser the instant they
  appear, on-device, with a Guardi badge. Nothing leaves the PC."*

#### 2. Per supervision mode

Three toggles (only visible when master is on):

| Mode | Slug |
|------|------|
| Trusted Sub | `trusted_sub` |
| Sub | `sub` |
| Restricted Sub | `restricted_sub` |

- Default: all **on** when master is on.
- Helper: *"Choose which supervision levels use image shield."*

#### 3. Per browser

Four browser rows (only visible when master is on):

| Browser | Key | UI state |
|---------|-----|----------|
| Firefox | `firefox` | **Enabled** — Dom can toggle on/off |
| Chrome | `chrome` | **Disabled** — badge "Work in progress", toggle locked off |
| Microsoft Edge | `edge` | **Disabled** — badge "Work in progress", toggle locked off |
| Brave | `brave` | **Disabled** — badge "Work in progress", toggle locked off |

**Firefox enable confirmation popup** (show when Dom turns Firefox **on**):

> **Firefox Developer Edition required**
>
> Image Shield on Firefox only works with **Firefox Developer Edition** installed
> on the Sub's computer. Guardi will close regular Firefox and ask the Sub to use
> Developer Edition instead.
>
> [Cancel] [Enable for Firefox]

Do **not** show this popup when disabling Firefox or when toggling modes.

#### 4. Advanced (collapsible)

- **Sensitivity** slider → `nsfw_threshold` (0.4 strict … 0.8 loose, default 0.45).
- **Min image size (px)** → `min_size` (default 80).

#### 5. Save

Persist per agent and queue `set_image_shield` immediately (see command below).
Also include the full `image_shield` block in heartbeat `settings` on every
successful heartbeat while enrolled.

#### 6. Live status from agent heartbeat

Read `heartbeat.image_shield` to show real enforcement state:

```ts
type ImageShieldHeartbeat = {
  global_enabled: boolean;
  effective_enabled: boolean;   // true only if master + current mode + ≥1 browser active
  mode: "trusted_sub" | "sub" | "restricted_sub";
  configured: boolean;          // agent has extension IDs / local Firefox pack
  policies_active: boolean;     // policies deployed on this PC right now
  has_server_config: boolean;
  browsers: Record<
    "firefox" | "chrome" | "edge" | "brave",
    {
      enabled_by_dom: boolean;
      available: boolean;       // agent can enforce if Dom enables
      enforced: boolean;        // actually running now
      unavailable_reason?: "work_in_progress" | "firefox_dev_edition_required";
      requires_dev_edition?: boolean;  // true for Firefox today
    }
  >;
};
```

Show a small status chip per browser, e.g. *"Firefox — active"*, *"Chrome — coming soon"*.

---

### Persist per agent (database)

```ts
image_shield_enabled: boolean;        // master, default false
image_shield_per_mode: {
  trusted_sub: boolean;
  sub: boolean;
  restricted_sub: boolean;
};                                    // default all true when master on
image_shield_per_browser: {
  firefox: boolean;
  chrome: boolean;   // always false until WIP lifted
  edge: boolean;
  brave: boolean;
};                                    // default firefox false, others false
image_shield_nsfw_threshold: number;  // 0.1–0.99, default 0.45
image_shield_min_size: number;        // px, default 80
```

---

### Heartbeat `settings` (extend existing block)

Send on **every** successful heartbeat while enrolled:

```json
{
  "settings": {
    "image_shield": {
      "enabled": true,
      "per_mode": {
        "trusted_sub": { "enabled": true },
        "sub": { "enabled": true },
        "restricted_sub": { "enabled": true }
      },
      "per_browser": {
        "firefox": { "enabled": true },
        "chrome": { "enabled": false },
        "edge": { "enabled": false },
        "brave": { "enabled": false }
      },
      "nsfw_threshold": 0.45,
      "min_size": 80,
      "sexy_weight": 1.0,
      "max_per_second": 24
    }
  }
}
```

- `enabled: false` → agent removes extension policies and stops enforcement.
- Per-mode / per-browser keys are optional on partial updates; prefer sending the **full** object on heartbeat.
- Tuning fields are optional — omit to keep agent defaults.

---

### Command `set_image_shield` (instant apply on Save)

Register in capabilities / schema:

```json
{
  "type": "set_image_shield",
  "label": "Set image shield",
  "group": "protection",
  "fields": [
    { "name": "enabled", "type": "boolean" },
    { "name": "per_mode", "type": "object" },
    { "name": "per_browser", "type": "object" },
    { "name": "nsfw_threshold", "type": "number" },
    { "name": "min_size", "type": "number" }
  ]
}
```

Queue on Save:

```json
{
  "type": "set_image_shield",
  "payload": {
    "enabled": true,
    "per_mode": {
      "trusted_sub": { "enabled": true },
      "sub": { "enabled": true },
      "restricted_sub": { "enabled": false }
    },
    "per_browser": {
      "firefox": { "enabled": true },
      "chrome": { "enabled": false },
      "edge": { "enabled": false },
      "brave": { "enabled": false }
    },
    "nsfw_threshold": 0.45,
    "min_size": 80
  }
}
```

---

### Agent-side behavior (implemented)

- Persists Dom policy to `%AppData%\EduGuard\image-shield-policy.json`.
- Applies / removes extension policies based on **master + current mode + per-browser**.
- **Firefox**: Developer Edition + local unsigned XPI, or signed AMO XPI when configured.
- **Chrome / Edge / Brave**: force-install from Chrome Web Store (`pooilkajkfmogajdafmaphmjecofpbbk`, unlisted listing).
- Reconciles on: startup, enrollment, mode change, heartbeat settings, `set_image_shield` command.
- Sends `image_shield` telemetry in every heartbeat for dashboard sync.
- Fail-safe: any image the model cannot verify stays blurred while shield is active.

---

## Deployment prerequisites (one-time, outside Lovable)

1. Build the extension: `cd extension && npm run build`
2. Run Guardi as **Administrator** on the Sub's PC.
3. Install **Firefox Developer Edition** (for Firefox local mode) and/or use **Google Chrome** (Web Store force-install).
4. `extension/store-config.json` contains the Chrome Web Store ID; `ExtensionGuardEnforceChromium` is enabled in `Config.cs`.

See `docs/EXTENSION_LOCAL_TEST.md` for local Firefox and Chrome testing.
