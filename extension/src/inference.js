// Shared on-device NSFW inference (used by Chromium offscreen + Firefox background).

import "@tensorflow/tfjs-backend-cpu";
import "@tensorflow/tfjs-backend-wasm";
import * as tf from "@tensorflow/tfjs";
import { setWasmPaths } from "@tensorflow/tfjs-backend-wasm";
import { load as loadNsfwModel } from "nsfwjs/core";
import {
  MSG_CLASSIFY,
  MSG_CLASSIFY_BLOB,
  DEFAULTS,
  nsfwScore,
  isBlockScore,
  isExplicitImageUrl,
} from "./shared.js";
import { applyManagedTuning, STATE_LOCAL_KEY } from "./active-state.js";

const MODEL_INPUT = 299; // InceptionV3
const THUMB_MAX_SIDE = 256;
const URL_BLOB_CACHE_MAX = 320;

export function createInferenceEngine({ api, backends, logLabel = "[Guardi]" }) {
  let settings = { ...DEFAULTS };
  let modelPromise = null;
  let active = 0;
  const pending = [];
  const urlBlobCache = new Map();

  function cacheUrlBlob(url, blob) {
    if (urlBlobCache.size >= URL_BLOB_CACHE_MAX) {
      const first = urlBlobCache.keys().next().value;
      if (first) urlBlobCache.delete(first);
    }
    urlBlobCache.set(url, blob);
  }

  async function loadSettings() {
    let managed = {};
    try {
      managed = (await api.storage.managed.get(null)) || {};
    } catch {
      // ignore
    }

    // Firefox temp install / agent-first: mirror published by background from /shield-state.
    try {
      const local = await api.storage.local.get(STATE_LOCAL_KEY);
      const published = local?.[STATE_LOCAL_KEY];
      if (published && typeof published === "object") {
        const mirrorManaged =
          published.managed && typeof published.managed === "object" ? published.managed : {};
        managed = { ...managed, ...mirrorManaged, shieldActive: !!published.active };
      }
    } catch {
      // ignore
    }

    if (managed && typeof managed === "object") {
      settings = applyManagedTuning(managed, DEFAULTS, settings);
    }
  }

  function applyManaged(managed) {
    if (managed && typeof managed === "object") {
      settings = applyManagedTuning(managed, DEFAULTS, settings);
    }
  }

  function isShieldActive() {
    return settings.shieldActive === true;
  }

  async function initBackend() {
    if (backends.includes("wasm")) {
      setWasmPaths(api.runtime.getURL("wasm/"));
    }
    for (const backend of backends) {
      try {
        await tf.setBackend(backend);
        await tf.ready();
        if (tf.getBackend() === backend) {
          console.info(`${logLabel} TF.js backend:`, backend);
          return backend;
        }
      } catch (err) {
        console.warn(`${logLabel} backend`, backend, "failed", err);
      }
    }
    return null;
  }

  async function getModel() {
    if (!modelPromise) {
      modelPromise = (async () => {
        await initBackend();
        return loadNsfwModel(api.runtime.getURL("models/model.json"), { size: MODEL_INPUT });
      })().catch((err) => {
        modelPromise = null;
        throw err;
      });
    }
    return modelPromise;
  }

  function isThumbDimensions(width, height) {
    const w = width || 0;
    const h = height || 0;
    return w > 0 && h > 0 && Math.max(w, h) <= THUMB_MAX_SIDE;
  }

  function makeVerdict(score, isThumb, predictions = null, extra = {}) {
    return {
      block: isBlockScore(score, settings, isThumb, predictions),
      score,
      isThumb,
      ...extra,
    };
  }

  async function scoreBitmap(bitmap, isThumb = false) {
    const model = await getModel();
    const srcW = bitmap.width || MODEL_INPUT;
    const srcH = bitmap.height || MODEL_INPUT;
    const maxSide = isThumb ? THUMB_MAX_SIDE : MODEL_INPUT;
    const scale = Math.min(1, maxSide / Math.max(srcW, srcH, 1));
    const drawW = Math.max(1, Math.round(srcW * scale));
    const drawH = Math.max(1, Math.round(srcH * scale));
    const canvas = new OffscreenCanvas(MODEL_INPUT, MODEL_INPUT);
    const ctx = canvas.getContext("2d", { willReadFrequently: true });
    ctx.drawImage(bitmap, 0, 0, drawW, drawH);
    const pixels = tf.browser.fromPixels(canvas);
    try {
      const predictions = await model.classify(pixels);
      return { score: nsfwScore(predictions, settings.sexyWeight), predictions };
    } finally {
      pixels.dispose();
    }
  }

  async function classifyBlob(blob, isThumb) {
    if (!blob.type?.startsWith("image/")) {
      return { block: false, reason: "not-image", score: 0, isThumb };
    }
    let bitmap;
    try {
      bitmap = await createImageBitmap(blob);
    } catch {
      return { block: false, reason: "decode-failed", score: 0, isThumb };
    }
    try {
      const { score, predictions } = await scoreBitmap(bitmap, isThumb);
      return makeVerdict(score, isThumb, predictions);
    } finally {
      bitmap.close?.();
    }
  }

  async function fetchImageBlob(url) {
    const cached = urlBlobCache.get(url);
    if (cached) return cached;

    let blob;
    try {
      const resp = await fetch(url, { credentials: "omit", cache: "force-cache" });
      if (resp.ok) blob = await resp.blob();
    } catch {
      // fall through
    }
    if (!blob) {
      const resp = await fetch(url, { credentials: "omit" });
      if (!resp.ok) throw new Error(`http ${resp.status}`);
      blob = await resp.blob();
    }
    cacheUrlBlob(url, blob);
    return blob;
  }

  async function classifyUrl(url, width, height) {
    const isThumb =
      isThumbDimensions(width, height) ||
      /encrypted-tbn\d\.gstatic\.com/i.test(url) ||
      isExplicitImageUrl(url);
    return classifyBlob(await fetchImageBlob(url), isThumb);
  }

  async function classifyBuffer(buffer, mime, width, height) {
    const isThumb = isThumbDimensions(width, height);
    return classifyBlob(new Blob([buffer], { type: mime || "image/jpeg" }), isThumb);
  }

  async function classifyMessage(msg) {
    if (msg.type === MSG_CLASSIFY && typeof msg.url === "string") {
      return classifyUrl(msg.url, msg.width || 0, msg.height || 0);
    }
    if (msg.type === MSG_CLASSIFY_BLOB && msg.buffer) {
      return classifyBuffer(msg.buffer, msg.mime, msg.width || 0, msg.height || 0);
    }
    return { block: false, reason: "bad-request", score: 0 };
  }

  function runQueued(msg, sendResponse, { maxConcurrency = 2, debug = false } = {}) {
    const task = async () => {
      let verdict;
      let predictions = null;
      try {
        const result = await classifyMessage(msg);
        verdict = result;
        if (result.predictions) predictions = result.predictions;
      } catch (err) {
        verdict = { block: false, reason: "error", error: String(err?.message || err), score: 0 };
      }
      if (debug) {
        const tag = msg.type === MSG_CLASSIFY ? (msg.url || "").slice(0, 60) : `blob ${msg.mime}`;
        const cls = predictions
          ? predictions.map((p) => `${p.className[0]}=${p.probability.toFixed(2)}`).join(" ")
          : "";
        console.info(
          `${logLabel} ${verdict.block ? "BLOCK" : "ok"} score=${(verdict.score ?? 0).toFixed?.(2) ?? verdict.score}` +
            `${verdict.reason ? " reason=" + verdict.reason : ""} ${cls} :: ${tag}`
        );
      }
      try {
        sendResponse(verdict);
      } catch {
        // channel closed
      }
      active--;
      drain(maxConcurrency, debug);
    };
    pending.push(task);
    drain(maxConcurrency, debug);
  }

  function drain(maxConcurrency, debug) {
    while (active < maxConcurrency && pending.length) {
      active++;
      pending.shift()();
    }
  }

  return {
    loadSettings,
    applyManaged,
    isShieldActive,
    warmup: async () => {
      await loadSettings();
      return getModel();
    },
    getBackend: () => tf.getBackend(),
    classifyMessage,
    runQueued,
  };
}
