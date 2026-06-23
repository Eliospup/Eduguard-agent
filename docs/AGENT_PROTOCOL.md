# EduGuard Windows Agent — Protocol Specification

**Protocol version: 2**

The web app maintains a single source of truth for command types and
payloads in `src/lib/agent-protocol/schemas.ts`. At startup the agent
SHOULD call `GET /api/public/agent/capabilities` to discover which
commands the server currently understands.

This document describes the HTTP protocol between the EduGuard web backend
and a Windows agent that runs on the Sub's PC. Implement the agent in any
language you like (C# .NET, Rust + windows-rs, Python + pywin32 packaged
as `.exe` with PyInstaller, Go, etc.).

## Base URL

- Production: `https://project--<project-id>.lovable.app`
- Preview:    `https://project--<project-id>-dev.lovable.app`

All endpoints live under `/api/public/agent/`.

## Authentication

Every endpoint except `/register` requires a bearer token issued at
registration. Send it as:

```
Authorization: Bearer <agent_token>
```

Store the token locally on the PC, encrypted via Windows DPAPI
(`ProtectedData.Protect` in .NET) so only the local user account can read it.

## Endpoints

### 1. `POST /api/public/agent/register`

First-time enrollment using a one-time 8-char code provided by the Dom from
the web UI. Code is valid 10 minutes.

Request:
```json
{
  "code": "ABCD1234",
  "name": "Sub's main PC",
  "os_info": { "version": "Windows 11 Pro 23H2", "hostname": "DESKTOP-XYZ" }
}
```

Response 200:
```json
{ "agent_id": "uuid", "agent_token": "long-random-token" }
```

The `agent_token` is returned **once only** — store it immediately.

### 2. `POST /api/public/agent/heartbeat`

Send every 5–10 seconds. Updates `last_seen_at` and inserts a status report.

Request:
```json
{
  "focused_window": "Mozilla Firefox - Reddit",
  "running_apps": ["chrome.exe", "discord.exe", "steam.exe"],
  "is_idle": false
}
```

Response 200:
```json
{
  "ok": true,
  "server_time": "2026-06-13T22:00:00Z",
  "settings": {
    "bedtime": {
      "enabled": true,
      "time": "23:00",
      "wake_time": "07:00"
    }
  }
}
```

`settings.bedtime.time` and `settings.bedtime.wake_time` use 24-hour `HH:mm` in the Sub's **local PC timezone**.
The PC locks at `time` and **unlocks automatically** at `wake_time`.
See `docs/LOVABLE_BEDTIME_INTEGRATION.md` for the full Dom dashboard spec.

### 3. `GET /api/public/agent/commands`

Poll every 2–5 seconds (or piggy-back on the heartbeat). Returns pending
commands and atomically marks them as `running`. Stale pending commands
(>1 h) are auto-expired.

Response 200:
```json
{
  "commands": [
    { "id": "uuid", "type": "kill_process", "payload": { "name": "chrome.exe" }, "issued_at": "..." },
    { "id": "uuid", "type": "lock_screen",  "payload": {}, "issued_at": "..." }
  ]
}
```

### 4. `POST /api/public/agent/commands/{id}/result`

Report execution outcome.

Request:
```json
{ "status": "done", "result": { "killed": 3 } }
```
or
```json
{ "status": "failed", "result": { "error": "process not found" } }
```

### 5. `POST /api/public/agent/upload`

Upload files from the agent (screenshots). **Implemented on the Windows agent** — the Lovable backend must add this route (see `docs/LOVABLE_SCREENSHOT_INTEGRATION.md`).

**Auth:** Bearer token

**Content-Type:** `multipart/form-data`

| Field | Required | Description |
|-------|----------|-------------|
| `kind` | yes | `screenshot` |
| `captured_at` | yes | ISO-8601 UTC |
| `trigger` | yes | `scheduled` or `on_command` |
| `level` | no | Profile slug |
| `focused_window` | no | Last focused window title |
| `file` | yes | JPEG binary |

Response `200`:
```json
{ "ok": true, "upload_id": "uuid", "url": "https://..." }
```

The agent uploads **every 5 minutes** while enrolled, plus on `screenshot` commands.

## Command Types

| Type            | Payload                          | Recommended Windows API                                         |
|-----------------|----------------------------------|-----------------------------------------------------------------|
| `kill_process`  | `{ "name": "chrome.exe" }`       | `Process.GetProcessesByName(...).Kill()`                        |
| `block_app`     | `{ "name": "steam.exe" }`        | Maintain a local blocklist, kill on every poll if seen          |
| `unblock_app`   | `{ "name": "steam.exe" }`        | Remove from local blocklist                                     |
| `block_url`     | `{ "host": "reddit.com" }`       | Append `127.0.0.1 reddit.com` to `C:\Windows\System32\drivers\etc\hosts` |
| `unblock_url`   | `{ "host": "reddit.com" }`       | Remove from `hosts`                                             |
| `lock_screen`   | `{}`                             | Full-screen in-app lock overlay (Dom lock)                        |
| `unlock_screen` | `{}`                             | Dismisses the Dom lock overlay                                    |
| `force_logoff`  | `{}`                             | `user32.dll!ExitWindowsEx(EWX_LOGOFF, 0)`                       |
| `show_message`  | `{ "text": "..." }`              | Toast via `Microsoft.Toolkit.Uwp.Notifications` or MessageBox   |
| `set_wallpaper` | `{ "url": "https://..." }`       | Download then `SystemParametersInfo(SPI_SETDESKWALLPAPER, ...)` |
| `screenshot`    | `{}`                             | `Graphics.CopyFromScreen` → upload to a future storage endpoint |

Editing `hosts` and force logoff require running the agent **as administrator**.
Install it as a Windows Service for persistent supervision the Sub cannot
casually stop.

## Minimal C# pseudo-loop

```csharp
var http = new HttpClient { BaseAddress = new Uri(BASE_URL) };
http.DefaultRequestHeaders.Authorization = new("Bearer", token);

while (true) {
  await http.PostAsJsonAsync("/api/public/agent/heartbeat", CollectStatus());

  var resp = await http.GetFromJsonAsync<CmdList>("/api/public/agent/commands");
  foreach (var cmd in resp.commands) {
    var (ok, result) = Execute(cmd);
    await http.PostAsJsonAsync(
      $"/api/public/agent/commands/{cmd.id}/result",
      new { status = ok ? "done" : "failed", result }
    );
  }

  await Task.Delay(5000);
}
```

## Security recommendations

- Pin the TLS certificate or at minimum validate it (no `ServerCertificateCustomValidationCallback = (_,_,_,_) => true`).
- Store the token via DPAPI scoped to `LocalMachine` when running as a service, `CurrentUser` when running per user.
- On 401 from any endpoint, wipe the local token and require re-enrollment.
- Validate command payloads before executing (reject paths with `..`, reject process names containing path separators).
- Log every executed command locally for audit, and report execution outcome honestly even on failure.

## Future endpoints (not yet implemented)

- WebSocket push instead of polling (lower latency for `lock_screen` commands).

## Capability discovery

### `GET /api/public/agent/capabilities` (no auth)

Returns:
```json
{
  "protocol_version": 2,
  "commands": [
    { "type": "kill_process", "label": "Kill process", "group": "process",
      "fields": [{ "name": "name", "type": "string" }] },
    { "type": "unblock_url",  "label": "Unblock URL",  "group": "web",
      "fields": [{ "name": "host", "type": "string" }] }
  ]
}
```

If the agent receives a command whose `type` it doesn't know how to handle,
it MUST report `{ "status": "failed", "result": { "error": "unsupported_command" } }`
so the Dom sees it instead of the command silently hanging.

## Quick test without a Windows machine

The repo ships `scripts/mock-agent.ts`. From any machine with Bun:
```bash
bun scripts/mock-agent.ts https://project--<id>-dev.lovable.app ABCD1234
```
It registers, heartbeats, polls commands and acknowledges them — handy
for iterating on the web UI before the real `.exe` is ready.
