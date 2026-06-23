// Runs inside an offscreen document — WebGL/WASM backends (not available in SW).

import "@tensorflow/tfjs-backend-webgl";
import "@tensorflow/tfjs-backend-wasm";
import { setWasmPaths } from "@tensorflow/tfjs-backend-wasm";
import * as tf from "@tensorflow/tfjs";
import { load as loadNsfwModel } from "nsfwjs/core";
import {
  MSG_CLASSIFY,
  MSG_CLASSIFY_BLOB,
  MSG_RESULT,
  MSG_WARMUP,
  DEFAULTS,
  nsfwScore,
  isBlockScore,
  isExplicitImageUrl,
} from "./shared.js";
import { applyManagedTuning, STATE_LOCAL_KEY } from "./active-state.js";

const api = typeof browser !== "undefined" ? browser : chrome;
const MODEL_INPUT = 299; // InceptionV3 input size.
const THUMB_MAX_SIDE = 256;
const MAX_CONCURRENCY = 3;

let settings = { ...DEFAULTS };
let modelPromise = null;

let active = 0;
const pending = [];

async function loadSettings() {
  let managed = {};
  try {
    managed = (await api.storage.managed.get(null)) || {};
  } catch {
    // ignore
  }

  try {
    const local = await api.storage.local.get(STATE_LOCAL_KEY);
    const published = local?.[STATE_LOCAL_KEY]?.managed;
    if (published && typeof published === "object") {
      managed = { ...published, ...managed };
    }
  } catch {
    // ignore
  }

  if (managed && typeof managed === "object") {
    settings = applyManagedTuning(managed, DEFAULTS, settings);
  }
}

async function initBackend() {
  setWasmPaths(api.runtime.getURL("wasm/"));
  for (const backend of ["webgl", "wasm", "cpu"]) {
    try {
      await tf.setBackend(backend);
      await tf.ready();
      if (tf.getBackend() === backend) {
        console.info("[Guardi offscreen] TF.js backend:", backend);
        document.title = `Guardi Offscreen — ${backend}`;
        return backend;
      }
    } catch (err) {
      console.warn("[Guardi offscreen] backend", backend, "failed", err);
    }
  }
  document.title = "Guardi Offscreen — init failed";
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

// Decode without a shared canvas — each job owns its own bitmap.
async function decodeBitmap(blob) {
  return createImageBitmap(blob);
}

async function scoreBitmap(bitmap) {
  const model = await getModel();
  // Proven path: draw onto a per-job 224x224 canvas, then read pixels.
  const canvas = new OffscreenCanvas(MODEL_INPUT, MODEL_INPUT);
  const ctx = canvas.getContext("2d", { willReadFrequently: true });
  ctx.drawImage(bitmap, 0, 0, MODEL_INPUT, MODEL_INPUT);
  const pixels = tf.browser.fromPixels(canvas);
  try {
    const predictions = await model.classify(pixels);
    return { score: nsfwScore(predictions, settings.sexyWeight), predictions };
  } finally {
    pixels.dispose();
  }
}

async function classifyBlob(blob, isThumb) {
  // Fail OPEN on anything that isn't a decodable image (placeholders, HTML
  // error pages, truncated lazy-load stubs) — never block on technical issues.
  if (!blob.type?.startsWith("image/")) {
    return { block: false, reason: "not-image", score: 0, isThumb };
  }
  let bitmap;
  try {
    bitmap = await decodeBitmap(blob);
  } catch {
    return { block: false, reason: "decode-failed", score: 0, isThumb };
  }
  try {
    const { score, predictions } = await scoreBitmap(bitmap);
    return makeVerdict(score, isThumb, predictions);
  } finally {
    bitmap.close?.();
  }
}

async function fetchImageBlob(url) {
  // Prefer the browser cache (image is usually already downloaded), then
  // fall back to a normal network fetch if the cache misses or is stale.
  try {
    const cached = await fetch(url, { credentials: "omit", cache: "force-cache" });
    if (cached.ok) return cached.blob();
  } catch {
    // fall through to network
  }
  const resp = await fetch(url, { credentials: "omit" });
  if (!resp.ok) throw new Error(`http ${resp.status}`);
  return resp.blob();
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
  await loadSettings();
  if (msg.type === MSG_CLASSIFY && typeof msg.url === "string") {
    return classifyUrl(msg.url, msg.width || 0, msg.height || 0);
  }
  if (msg.type === MSG_CLASSIFY_BLOB && msg.buffer) {
    return classifyBuffer(msg.buffer, msg.mime, msg.width || 0, msg.height || 0);
  }
  // Unknown / malformed payload — fail open so the image is never stuck blurred.
  return { block: false, reason: "bad-request", score: 0 };
}

const DEBUG = false;

// Limit concurrent WebGL work; always resolve the message channel.
function runQueued(msg, sendResponse) {
  const task = async () => {
    let verdict;
    try {
      verdict = await classifyMessage(msg);
    } catch (err) {
      // Fail OPEN: a technical error must not leave a safe image blurred.
      verdict = { block: false, reason: "error", error: String(err?.message || err), score: 0 };
    }
    if (DEBUG) {
      const tag = msg.type === MSG_CLASSIFY ? (msg.url || "").slice(0, 60) : `blob ${msg.mime}`;
      const cls = verdict.predictions
        ? verdict.predictions
            .map((p) => `${p.className[0]}=${p.probability.toFixed(2)}`)
            .join(" ")
        : "";
      console.info(
        `[Guardi offscreen] ${verdict.block ? "BLOCK" : "ok"} score=${(verdict.score ?? 0).toFixed?.(2) ?? verdict.score}` +
          `${verdict.reason ? " reason=" + verdict.reason : ""} ${cls} :: ${tag}`
      );
    }
    try {
      sendResponse({ type: MSG_RESULT, id: msg.id, url: msg.url, verdict });
    } catch {
      // channel already closed; ignore
    }
    active--;
    drain();
  };

  pending.push(task);
  drain();
}

function drain() {
  while (active < MAX_CONCURRENCY && pending.length) {
    active++;
    pending.shift()();
  }
}

api.runtime.onMessage.addListener((msg, _sender, sendResponse) => {
  if (!msg?.guardiOffscreen) return false;

  // Warmup bypasses the classify queue — must not wait behind 100 grid jobs.
  if (msg.type === MSG_WARMUP) {
    getModel()
      .then(() => {
        const backend = tf.getBackend();
        document.title = `Guardi Offscreen — ready (${backend})`;
        sendResponse({ ready: true, backend });
      })
      .catch((err) => {
        document.title = "Guardi Offscreen — warmup failed";
        sendResponse({ ready: false, error: String(err?.message || err) });
      });
    return true;
  }

  runQueued(msg, sendResponse);
  return true;
});

loadSettings();
console.info("[Guardi offscreen] document loaded, warming model…");
getModel()
  .then(() => {
    document.title = `Guardi Offscreen — ready (${tf.getBackend()})`;
    console.info("[Guardi offscreen] model ready, backend:", tf.getBackend());
  })
  .catch((err) => console.error("[Guardi offscreen] warmup failed", err));
