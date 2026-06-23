# Guardi Image Shield (browser extension)

Blurs inappropriate images **on-device**, in real time, in the browser. Every
candidate image is blurred the moment it appears; the background worker then
classifies it with a local NSFW model (InceptionV3) and **un-blurs only the safe
ones**. Blocked images keep the blur and show a centered blue Guardi shield.
Nothing is ever sent to a server.

Targets: **Chromium** (Chrome / Edge / Brave) and **Firefox**, Manifest V3.

## Build

```bash
cd extension
npm install
npm run build:chromium    # → dist/chromium (offscreen + WebGL)
npm run build:firefox       # → dist/firefox (background script + WebGL/WASM)
npm run build               # both targets
```

## Load for development

- **Chrome/Edge/Brave**: `chrome://extensions` → Developer mode → *Load unpacked*
  → `dist/chromium`.
- **Firefox**: `about:debugging#/runtime/this-firefox` → *Load Temporary Add-on*
  → select **`manifest.json`** (the file, not the folder):
  `C:\Users\vferr\Projects\EduGuardAgent\extension\dist\firefox\manifest.json`

> **Firefox temporary add-ons are removed when Firefox closes.** This is normal.
> Re-load via `about:debugging` after each restart, or use the EduGuard agent
> force-install (signed `.xpi` + `policies.json`) for a permanent install.

After reload, **refresh open tabs** (F5) so the new content script loads.

## Architecture

| | Chromium | Firefox |
|---|----------|---------|
| Inference | Offscreen document (WebGL → WASM → CPU) | Background script (WebGL → WASM → CPU) |
| Background | Thin service-worker router | Full inference in-process |
| Extension ID | Chrome Web Store / policy | `image-shield@guardi.app` (gecko) |

Firefox has no `chrome.offscreen` API, so inference runs in a **background
script** (not a service worker) which keeps DOM/WebGL access.

## Force-install (managed devices)

The EduGuard Windows agent installs this extension as a **forced** policy on
Chromium and Firefox. See `docs/LOVABLE_IMAGE_SHIELD_INTEGRATION.md` and
`ImageBlurExtensionService`.

- Chromium: registry `ExtensionInstallForcelist`
- Firefox: `policies.json` → `ExtensionSettings` + XPI URL
  (`Config.ImageShieldFirefoxInstallUrl`)

### Local end-to-end test (no Web Store)

```bash
cd extension
npm install
npm run pack:host      # build + CRX/XPI + sync Config.cs
npm run serve:host     # keep running — serves http://127.0.0.1:8765/
```

Then rebuild the agent and run **as Administrator**. See
`docs/EXTENSION_LOCAL_TEST.md`.

## Tuning (Dom-controlled, no rebuild)

Managed storage policy keys: `minSize`, `nsfwThreshold`, `thumbThreshold`,
`sexyWeight`, `maxPerSecond` — see `src/shared.js` defaults.

## Known limits

- Covers `<img>` elements only (not CSS `background-image` or native apps).
- InceptionV3 warmup ~30–90 s on first load after extension reload.
- Detection is probabilistic; thresholds are tunable via managed storage.
