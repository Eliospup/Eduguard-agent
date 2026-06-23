// Service worker — routes classification to the offscreen document (fast WebGL/WASM).

import { MSG_CLASSIFY, MSG_CLASSIFY_BLOB, MSG_RESULT } from "./shared.js";
import { classifyCpu } from "./classify-cpu.js";

const api = typeof browser !== "undefined" ? browser : chrome;
const OFFSCREEN_URL = "offscreen.html";
const hasOffscreen = typeof api.offscreen?.createDocument === "function";

const verdictCache = new Map();
const CACHE_MAX = 4000;
let offscreenReady = null;

function cacheKeyFor(msg) {
  if (msg.type === MSG_CLASSIFY) return `u:${msg.url}|${msg.width}x${msg.height}`;
  return `b:${msg.buffer?.byteLength}:${msg.mime}:${msg.width}x${msg.height}`;
}

function cacheGet(key) {
  return verdictCache.get(key);
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

async function ensureOffscreen() {
  if (!hasOffscreen) return false;

  if (!offscreenReady) {
    offscreenReady = (async () => {
      const existing = await api.runtime.getContexts?.({
        contextTypes: ["OFFSCREEN_DOCUMENT"],
      });
      if (existing?.some((c) => c.documentUrl?.endsWith(OFFSCREEN_URL))) return true;

      await api.offscreen.createDocument({
        url: OFFSCREEN_URL,
        reasons: ["WORKERS"],
        justification: "On-device NSFW image classification with TensorFlow.js",
      });
      return true;
    })().catch((err) => {
      offscreenReady = null;
      throw err;
    });
  }

  await offscreenReady;
  return true;
}

async function forwardToOffscreen(msg) {
  return api.runtime.sendMessage({ ...msg, guardiOffscreen: true });
}

async function classify(msg) {
  const key = cacheKeyFor(msg);
  const cached = cacheGet(key);
  if (cached) return { type: MSG_RESULT, id: msg.id, url: msg.url, verdict: cached };

  if (!(await ensureOffscreen())) {
    return classifyCpu(msg);
  }

  const resp = await forwardToOffscreen(msg);
  if (resp?.verdict) cachePut(key, resp.verdict);
  return resp;
}

api.runtime.onMessage.addListener((msg, _sender, sendResponse) => {
  if (!msg || msg.guardiOffscreen) return false;

  if (msg.type === "guardi:ping") {
    sendResponse({ ok: true });
    return false;
  }

  if (msg.type === MSG_CLASSIFY || msg.type === MSG_CLASSIFY_BLOB) {
    classify(msg)
      .then((resp) =>
        sendResponse(
          resp || {
            type: MSG_RESULT,
            id: msg.id,
            verdict: { block: true, reason: "no-response", score: 1 },
          }
        )
      )
      .catch((err) =>
        sendResponse({
          type: MSG_RESULT,
          id: msg.id,
          verdict: { block: true, reason: "error", error: String(err?.message || err), score: 1 },
        })
      );
    return true;
  }

  return false;
});

api.runtime.onInstalled.addListener(() => {
  ensureOffscreen().catch(() => {});
});

api.alarms?.create?.("guardi-keepalive", { periodInMinutes: 1 });
api.alarms?.onAlarm?.addListener?.((alarm) => {
  if (alarm.name === "guardi-keepalive") ensureOffscreen().catch(() => {});
});

ensureOffscreen().catch(() => {});
