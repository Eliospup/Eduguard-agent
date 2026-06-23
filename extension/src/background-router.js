// Service worker — relays classification to the offscreen document via
// runtime messaging (single offscreen listener) and caches verdicts.

import { MSG_CLASSIFY, MSG_CLASSIFY_BLOB, MSG_RESULT, MSG_WARMUP, MSG_GET_STATE, shouldCacheVerdict } from "./shared.js";
import { syncGuardiChrome } from "./guardi-chrome.js";
import { createBackgroundStateWatcher } from "./active-state.js";
import { parseUiMode } from "./mode-ui.js";
import { installNewTabRedirect } from "./newtab-redirect.js";
import { installSearchGuard } from "./search-guard.js";
import { installYoutubeTimeGuard } from "./youtube-time-guard.js";
import { installExtensionHeartbeat } from "./heartbeat.js";

const api = typeof browser !== "undefined" ? browser : chrome;
const OFFSCREEN_URL = "offscreen.html";
const hasOffscreen = typeof api.offscreen?.createDocument === "function";
const DEBUG = false;
const MODEL_WARMUP_TIMEOUT_MS = 120000;

const verdictCache = new Map();
const CACHE_MAX = 4000;
let offscreenReady = null;
let modelReady = false;
let shieldActive = false;
let currentManaged = { shieldActive: false };

function log(...args) {
  if (DEBUG) console.info("[Guardi bg]", ...args);
}

function cacheKeyFor(msg) {
  if (msg.type === MSG_CLASSIFY) return `u:${msg.url}|${msg.width}x${msg.height}`;
  return `b:${msg.buffer?.byteLength}:${msg.mime}:${msg.width}x${msg.height}`;
}

function cachePut(key, verdict) {
  if (verdictCache.size >= CACHE_MAX) {
    let n = Math.ceil(CACHE_MAX * 0.1);
    for (const k of verdictCache.keys()) {
      verdictCache.delete(k);
      if (--n <= 0) break;
    }
  }
  verdictCache.set(key, verdict);
}

function withTimeout(promise, ms) {
  let timer;
  const timeout = new Promise((_, reject) => {
    timer = setTimeout(() => reject(new Error("warmup-timeout")), ms);
  });
  return Promise.race([promise, timeout]).finally(() => clearTimeout(timer));
}

async function warmupOffscreen() {
  const resp = await withTimeout(
    forwardToOffscreen({ type: MSG_WARMUP, guardiOffscreen: true }),
    MODEL_WARMUP_TIMEOUT_MS
  );
  modelReady = !!resp?.ready;
  log("model ready:", modelReady, resp?.backend);
  return modelReady;
}

async function ensureOffscreen() {
  if (!hasOffscreen || !shieldActive) return false;

  if (!offscreenReady) {
    offscreenReady = (async () => {
      const existing = await api.runtime.getContexts?.({
        contextTypes: ["OFFSCREEN_DOCUMENT"],
      });
      if (!existing?.some((c) => c.documentUrl?.endsWith(OFFSCREEN_URL))) {
        await api.offscreen.createDocument({
          url: OFFSCREEN_URL,
          reasons: ["WORKERS"],
          justification: "On-device NSFW image classification with TensorFlow.js",
        });
        log("offscreen created");
      }
      await warmupOffscreen();
      return true;
    })().catch((err) => {
      offscreenReady = null;
      modelReady = false;
      console.warn("[Guardi bg] offscreen init failed:", err?.message || err);
      throw err;
    });
  }

  await offscreenReady;
  return true;
}

function forwardToOffscreen(msg) {
  return api.runtime.sendMessage({ ...msg, guardiOffscreen: true });
}

const newTabCtrl = installNewTabRedirect(api);
const youtubeGuard = installYoutubeTimeGuard(api, () => currentManaged);

async function classify(msg) {
  if (!shieldActive) {
    return {
      type: MSG_RESULT,
      id: msg.id,
      url: msg.url,
      verdict: { block: false, reason: "inactive", score: 0 },
    };
  }

  const key = cacheKeyFor(msg);
  const cached = verdictCache.get(key);
  if (cached) return { type: MSG_RESULT, id: msg.id, url: msg.url, verdict: cached };

  if (!(await ensureOffscreen())) throw new Error("offscreen unavailable");

  let resp;
  try {
    resp = await forwardToOffscreen(msg);
  } catch (err) {
    log("forward failed, recreating offscreen:", err?.message);
    offscreenReady = null;
    await ensureOffscreen();
    resp = await forwardToOffscreen(msg);
  }

  const verdict = resp?.verdict;
  if (verdict && shouldCacheVerdict(verdict)) cachePut(key, verdict);
  return resp || { type: MSG_RESULT, id: msg.id, url: msg.url, verdict: { block: false, reason: "no-response", score: 0 } };
}

async function onShieldStateChange(active, managed) {
  const wasActive = shieldActive;
  shieldActive = active;
  currentManaged = managed || { shieldActive: active };
  youtubeGuard.onManagedChange();
  syncGuardiChrome(api, active, parseUiMode(currentManaged));
  if (active) {
    if (!wasActive) {
      ensureOffscreen().catch(() => {});
      newTabCtrl.rescanAllBlankTabs();
    }
  } else {
    modelReady = false;
    offscreenReady = null;
    verdictCache.clear();
    if (wasActive && hasOffscreen) {
      try {
        await api.offscreen.closeDocument?.();
      } catch {
        // ignore
      }
    }
    newTabCtrl.releaseGuardiNewTabs();
  }
}

const shieldWatcher = createBackgroundStateWatcher(api, {
  onChange: (active, managed) => {
    onShieldStateChange(active, managed);
    youtubeGuard.sync();
  },
  getHeartbeatStatus: () => ({ shieldActive, modelReady }),
});
installExtensionHeartbeat(api, () => ({ shieldActive, modelReady }));
const searchGuard = installSearchGuard(api, () => shieldActive);

api.runtime.onMessage.addListener((msg, _sender, sendResponse) => {
  if (!msg || msg.guardiOffscreen) return false;

  if (searchGuard.handleMessage(msg, sendResponse)) return true;

  if (msg.type === MSG_GET_STATE) {
    shieldWatcher.refresh().then(({ active, managed }) => sendResponse({ active, managed }));
    return true;
  }

  if (msg.type === "guardi:ping") {
    sendResponse({ ok: true, modelReady, shieldActive });
    return false;
  }

  if (msg.type === MSG_CLASSIFY || msg.type === MSG_CLASSIFY_BLOB) {
    classify(msg)
      .then(sendResponse)
      .catch((err) =>
        sendResponse({
          type: MSG_RESULT,
          id: msg.id,
          verdict: { block: false, reason: "error", error: String(err?.message || err), score: 0 },
        })
      );
    return true;
  }

  return false;
});

(async () => {
  await shieldWatcher.refresh();
  shieldWatcher.start();
  await newTabCtrl.syncTabsForCurrentState();
})();

api.alarms?.create?.("guardi-keepalive", { periodInMinutes: 1 });
api.alarms?.onAlarm?.addListener?.((alarm) => {
  if (alarm.name === "guardi-keepalive" && shieldActive) ensureOffscreen().catch(() => {});
});
