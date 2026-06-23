// Firefox background: single source of truth for Guardi state, new-tab routing,
// search blocking and on-device inference. Content scripts never poll the agent
// directly; they ask this controller and receive broadcasts.

import { createInferenceEngine } from "./inference.js";
import { MSG_CLASSIFY, MSG_CLASSIFY_BLOB, MSG_RESULT, MSG_WARMUP, MSG_GET_STATE, shouldCacheVerdict } from "./shared.js";
import { syncGuardiChrome } from "./guardi-chrome.js";
import { createBackgroundStateWatcher } from "./active-state.js";
import { parseUiMode } from "./mode-ui.js";
import { installNewTabRedirect } from "./newtab-redirect.js";
import { installSearchGuard } from "./search-guard.js";
import { installYoutubeTimeGuard } from "./youtube-time-guard.js";
import { installExtensionHeartbeat } from "./heartbeat.js";

const api = typeof browser !== "undefined" ? browser : chrome;
const DEBUG = false;

const CLASSIFY_MAX_CONCURRENCY = 4;
const CLASSIFY_TIMEOUT_MS = 10000;
const CACHE_MAX = 4000;

const engine = createInferenceEngine({
  api,
  backends: ["wasm", "cpu"],
  logLabel: "[Guardi firefox]",
});

const verdictCache = new Map();
const classifyQueue = [];
let classifyRunning = 0;
let classifyGeneration = 0;
let modelReady = false;
let shieldActive = false;
let currentManaged = { shieldActive: false };
let lastStateRefreshAt = 0;

function cacheKeyFor(msg) {
  if (msg.url && typeof msg.url === "string") return `u:${msg.url}|${msg.width || 0}x${msg.height || 0}`;
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

function inactiveResult(msg, reason = "inactive") {
  return {
    type: MSG_RESULT,
    id: msg?.id,
    url: msg?.url,
    verdict: { block: false, reason, score: 0 },
  };
}

function withTimeout(promise, ms) {
  let timer;
  const timeout = new Promise((_, reject) => {
    timer = setTimeout(() => reject(new Error("timeout")), ms);
  });
  return Promise.race([promise, timeout]).finally(() => clearTimeout(timer));
}

function cancelQueuedClassifications() {
  classifyGeneration++;
  while (classifyQueue.length) {
    const job = classifyQueue.shift();
    try {
      job.sendResponse(inactiveResult(job.msg));
    } catch {
      // The message port may already be closed.
    }
  }
}

function drainClassifyQueue() {
  if (!shieldActive) return;
  while (classifyRunning < CLASSIFY_MAX_CONCURRENCY && classifyQueue.length) {
    const job = classifyQueue.shift();
    if (job.generation !== classifyGeneration) {
      try {
        job.sendResponse(inactiveResult(job.msg));
      } catch {
        // ignore closed ports
      }
      continue;
    }
    classifyRunning++;
    runClassifyJob(job);
  }
}

async function runClassifyJob(job) {
  const { msg, key, generation, sendResponse } = job;
  try {
    if (!shieldActive || generation !== classifyGeneration) {
      sendResponse(inactiveResult(msg));
      return;
    }

    const verdict = await withTimeout(engine.classifyMessage(msg), CLASSIFY_TIMEOUT_MS);
    if (!shieldActive || generation !== classifyGeneration) {
      sendResponse(inactiveResult(msg));
      return;
    }

    if (shouldCacheVerdict(verdict)) cachePut(key, verdict);
    sendResponse({ type: MSG_RESULT, id: msg.id, url: msg.url, verdict });
  } catch (err) {
    sendResponse({
      type: MSG_RESULT,
      id: msg.id,
      url: msg.url,
      verdict: { block: false, reason: "error", error: String(err?.message || err), score: 0 },
    });
  } finally {
    classifyRunning = Math.max(0, classifyRunning - 1);
    drainClassifyQueue();
  }
}

function queueClassification(msg, sendResponse) {
  if (!shieldActive) {
    sendResponse(inactiveResult(msg));
    return false;
  }

  const key = cacheKeyFor(msg);
  const cached = verdictCache.get(key);
  if (cached) {
    sendResponse({ type: MSG_RESULT, id: msg.id, url: msg.url, verdict: cached });
    return false;
  }

  const isBlob = msg.type === MSG_CLASSIFY_BLOB;
  classifyQueue.push({ msg, key, sendResponse, generation: classifyGeneration, priority: isBlob ? 0 : 1 });
  classifyQueue.sort((a, b) => a.priority - b.priority);
  drainClassifyQueue();
  return true;
}

const newTabCtrl = installNewTabRedirect(api, {
  confirmActive: () => shieldActive,
  enabled: true,
});
const youtubeGuard = installYoutubeTimeGuard(api, () => currentManaged);

async function warmupModel() {
  try {
    await engine.warmup();
    modelReady = true;
  } catch (err) {
    modelReady = false;
    console.warn("[Guardi firefox] model warmup failed:", err?.message || err);
  }
}

async function onShieldStateChange(active, managed) {
  const wasActive = shieldActive;
  shieldActive = active;
  currentManaged = managed || { shieldActive: active };
  lastStateRefreshAt = Date.now();
  youtubeGuard.onManagedChange();
  engine.applyManaged(currentManaged);
  syncGuardiChrome(api, active, parseUiMode(currentManaged));

  if (active) {
    if (!wasActive) {
      engine.loadSettings().catch(() => {});
      warmupModel();
    }
    await newTabCtrl.syncTabsForCurrentState();
    drainClassifyQueue();
    return;
  }

  modelReady = false;
  verdictCache.clear();
  cancelQueuedClassifications();
  await newTabCtrl.releaseGuardiNewTabs();
}

const shieldWatcher = createBackgroundStateWatcher(api, {
  onChange: (active, managed) => {
    onShieldStateChange(active, managed);
    youtubeGuard.sync();
  },
  getHeartbeatStatus: () => ({ shieldActive, modelReady }),
  pollMs: 1200,
});
installExtensionHeartbeat(api, () => ({ shieldActive, modelReady }));
const searchGuard = installSearchGuard(api, () => shieldActive);

api.runtime.onMessage.addListener((msg, _sender, sendResponse) => {
  if (!msg || msg.guardiOffscreen) return false;

  if (searchGuard.handleMessage(msg, sendResponse)) return true;

  if (msg.type === MSG_GET_STATE) {
    sendResponse({ active: shieldActive, managed: currentManaged });
    if (Date.now() - lastStateRefreshAt > 2000) {
      shieldWatcher.refresh().catch(() => {});
    }
    return false;
  }

  if (msg.type === "guardi:ping") {
    sendResponse({ ok: true, modelReady, shieldActive });
    return false;
  }

  if (msg.type === MSG_WARMUP) {
    if (!shieldActive) {
      sendResponse({ ready: false, reason: "inactive" });
      return false;
    }
    engine
      .warmup()
      .then(() => {
        modelReady = true;
        sendResponse({ ready: true, backend: engine.getBackend() });
      })
      .catch((err) => sendResponse({ ready: false, error: String(err?.message || err) }));
    return true;
  }

  if (msg.type === MSG_CLASSIFY || msg.type === MSG_CLASSIFY_BLOB) {
    return queueClassification(msg, sendResponse);
  }

  return false;
});

(async () => {
  await shieldWatcher.refresh();
  shieldWatcher.start();
  for (const delay of [350, 1000]) {
    setTimeout(() => shieldWatcher.refresh(), delay);
  }
  await newTabCtrl.syncTabsForCurrentState();
})();

api.alarms?.create?.("guardi-keepalive", { periodInMinutes: 1 });
api.alarms?.onAlarm?.addListener?.((alarm) => {
  if (alarm.name !== "guardi-keepalive") return;
  if (!shieldActive) return;
  warmupModel();
});
