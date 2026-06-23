# Lovable integration — Agent screenshots (implemented)

**Status:** The Windows agent (`EduGuardAgent`) now captures and uploads screenshots.  
**Action required on Lovable:** implement the backend endpoint + Dom dashboard UI described below.

---

## Summary for Lovable

The Sub's PC agent:

1. **Every 5 minutes** (while enrolled and the app is running), captures the full virtual desktop as **JPEG** and uploads it.
2. Also uploads immediately when the Dom sends the existing **`screenshot`** command (`{}` payload).
3. Uses the same bearer token as heartbeat/commands.

Until `POST /api/public/agent/upload` exists and stores files, uploads will fail with HTTP 4xx/5xx — the agent logs the error and retries on the next interval.

---

## Endpoint to implement

### `POST /api/public/agent/upload`

**Auth:** `Authorization: Bearer <agent_token>` (same as other agent routes)

**Content-Type:** `multipart/form-data`

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `kind` | string | yes | Always `screenshot` for now |
| `captured_at` | string | yes | ISO-8601 UTC, e.g. `2026-06-14T12:05:00.0000000Z` |
| `trigger` | string | yes | `scheduled` (every 5 min) or `on_command` (Dom 📸 button / `screenshot` command) |
| `level` | string | no | Agent profile slug, e.g. `college_student` |
| `focused_window` | string | no | Window title at last heartbeat (context for Dom) |
| `file` | file | yes | JPEG image, field name **`file`** |

**Example curl (Dom/dev testing):**

```bash
curl -X POST "https://project--<id>-dev.lovable.app/api/public/agent/upload" \
  -H "Authorization: Bearer AGENT_TOKEN" \
  -F "kind=screenshot" \
  -F "captured_at=2026-06-14T12:00:00Z" \
  -F "trigger=on_command" \
  -F "level=college_student" \
  -F "focused_window=Mozilla Firefox - Example" \
  -F "file=@screenshot.jpg;type=image/jpeg"
```

### Success response `200` or `201`

```json
{
  "ok": true,
  "upload_id": "uuid",
  "url": "https://<storage-public-url>/screenshots/<agent_id>/<upload_id>.jpg"
}
```

- `url` must be **publicly readable** by the Dom dashboard (signed URL or public bucket path).
- `upload_id` is stored in your DB for listing/history.

### Error responses

| Code | Body example | Agent behavior |
|------|----------------|----------------|
| `401` | `{ "error": "unauthorized" }` | Agent wipes token, requires re-enrollment |
| `413` | `{ "error": "file_too_large" }` | Logged; retries next interval |
| `422` | `{ "error": "invalid_kind" }` | Logged |
| `501` | `{ "error": "not_implemented" }` | Logged until you ship the endpoint |

---

## Suggested database schema (Lovable Cloud)

Table `agent_screenshots` (or extend existing agent events):

| Column | Type | Notes |
|--------|------|-------|
| `id` | uuid | PK, returned as `upload_id` |
| `agent_id` | uuid | FK → agents |
| `captured_at` | timestamptz | from form field |
| `trigger` | text | `scheduled` \| `on_command` |
| `level` | text | nullable |
| `focused_window` | text | nullable |
| `storage_path` | text | bucket key |
| `public_url` | text | returned to agent |
| `created_at` | timestamptz | default now() |

Index: `(agent_id, captured_at DESC)` for Dom timeline.

---

## Dom dashboard — what to build

### 1. Screenshot gallery / timeline

On the agent detail page (Dom view):

- Poll or subscribe to `agent_screenshots` for the selected agent.
- Show thumbnails newest-first.
- Display `captured_at`, `trigger` badge (`auto` vs `requested`), and `focused_window` caption.
- Lightbox on click → full `public_url`.

### 2. Optional: manual capture button

The `screenshot` command already works on the agent side:

```json
{ "type": "screenshot", "payload": {} }
```

Wire your existing “Request screenshot” button to enqueue this command; the agent uploads with `trigger=on_command`.

### 3. Empty / error states

- **No endpoint yet:** show “Waiting for first screenshot — ensure `/upload` is deployed.”
- **Agent offline:** last screenshot timestamp + stale indicator (>10 min since last `scheduled` upload).

---

## Agent implementation details (already done)

| Setting | Value |
|---------|-------|
| Interval | **5 minutes** (`Config.ScreenshotIntervalMinutes`) |
| Format | JPEG, quality 72 |
| Max width | 1920 px (scaled down, aspect preserved) |
| Capture area | Full virtual desktop (all monitors) |
| Starts when | Enrollment succeeds or saved token loaded |
| Stops when | Enrollment reset, app closed, loop stopped |

**Local audit log:** `%AppData%\EduGuard\audit.log`  
**UI log:** “Safety screenshot sent to your Dom (scheduled|command).”

---

## `screenshot` command result (on-demand)

When Dom sends `screenshot`, agent reports:

**Success:**

```json
{
  "status": "done",
  "result": {
    "uploaded": true,
    "upload_id": "uuid",
    "url": "https://...",
    "captured_at": "2026-06-14T12:00:00Z"
  }
}
```

**Failure:**

```json
{
  "status": "failed",
  "result": { "error": "upload_failed" }
}
```

---

## Checklist for Lovable

- [ ] Create storage bucket (Lovable Cloud / Supabase Storage) for screenshots
- [ ] Implement `POST /api/public/agent/upload` (multipart parser + auth)
- [ ] Persist metadata row per upload
- [ ] Return public/signed `url` in response
- [ ] Dom UI: screenshot timeline on agent page
- [ ] (Optional) Realtime refresh when new row inserted
- [ ] Update `GET /api/public/agent/capabilities` docs if you version the API

---

## Copy-paste prompt for Lovable chat

```
The Windows EduGuard agent now uploads screenshots every 5 minutes and on screenshot commands.

Please implement POST /api/public/agent/upload (multipart, bearer auth) per docs/LOVABLE_SCREENSHOT_INTEGRATION.md:
- fields: kind, captured_at, trigger, level, focused_window, file (JPEG)
- response: { ok, upload_id, url }
- store in DB + object storage
- show a screenshot timeline on the Dom agent dashboard (newest first, focused_window caption, trigger badge)

The agent project is ready; uploads will work as soon as this endpoint is live.
```
