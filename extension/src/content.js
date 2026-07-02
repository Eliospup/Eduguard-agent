// Content script: lightweight visual filtering driven by the Firefox background.
// The page never talks to the Guardi agent directly. It only reacts to the
// background state and classifies visible images through a small queue.

import { MSG_CLASSIFY, MSG_CLASSIFY_BLOB, MSG_GET_STATE, DEFAULTS, shouldCacheVerdict } from "./shared.js";
import { mountStatusBanner, removeStatusBanner, updateStatusBannerMode } from "./status-banner.js";
import { applyManagedTuning, MSG_SHIELD_STATE, STATE_LOCAL_KEY } from "./active-state.js";
import { applyModeToDocument, getModeUi, parseUiMode, shieldOverlaySvg } from "./mode-ui.js";

const api = typeof browser !== "undefined" ? browser : chrome;

const BLUR_CLASS = "guardi-blur";
const BLOCKED_CLASS = "guardi-blocked";
const SAFE_ATTR = "data-guardi-safe";
const VERDICT_ATTR = "data-guardi-verdict";
const JOB_TIMEOUT_MS = 14000;
const MAX_ATTEMPTS = 2;
const VIEWPORT_MARGIN = 900;
const GOOGLE_VIEWPORT_MARGIN = 650;
const GOOGLE_PREHEAT_MARGIN = 1400;

function contentMaxConcurrency() {
  if (isGoogleImages) return 4;
  return typeof browser !== "undefined" ? 6 : 8;
}

function contentRateCap() {
  if (!isGoogleImages) return maxPerSecond;
  return Math.min(maxPerSecond, 10);
}

const REVEAL_REASONS = new Set(["not-image", "decode-failed", "bad-request"]);
const GOOGLE_IMG_ROOTS = ["#islrg", "#islrt", "#Sva75c"];

const canFilterFrame = window === window.top && /^(https?:|file:)/i.test(location.protocol);
const isGoogleImages =
  /[?&](tbm=isch|udm=2)\b/.test(location.search) || location.hostname === "images.google.com";

let shieldActive = false;
let filteringStarted = false;
let generation = 0;
let msgId = 0;
let minSize = DEFAULTS.minSize;
let maxPerSecond = DEFAULTS.maxPerSecond;
let currentUiMode = "sub";
let currentManaged = { shieldActive: false };
let currentShieldSvg = shieldOverlaySvg("#2563EB", "#93C5FD");
let observer = null;
let viewportObserver = null;
let statePollTimer = 0;
let scanTimer = 0;
let jobsThisSecond = 0;
let rateWindowStart = 0;
let runningJobs = 0;

const verdictCache = new Map();
const waiters = new Map();
const queuedKeys = new Set();
const runningKeys = new Set();
const queue = [];
const imageKeys = new WeakMap();
const sourceWatchers = new WeakMap();
const loadWatchers = new WeakSet();
const shields = new WeakMap();
const shieldedImages = new Set();

function isGoogleHost() {
  return location.hostname === "images.google.com" || /(^|\.)google\.[a-z.]{2,}$/i.test(location.hostname);
}

function isGoogleWebSearch() {
  if (!isGoogleHost() || isGoogleImages) return false;
  return location.pathname === "/search" || location.pathname.startsWith("/search/");
}

function shouldFilterThisPage() {
  return canFilterFrame && !isGoogleWebSearch();
}

function shouldShowStatusBanner() {
  return canFilterFrame && (isGoogleImages || isGoogleWebSearch()) && currentManaged?.websiteBadge !== false;
}

function contextValid() {
  try {
    return !!api.runtime?.id;
  } catch {
    return false;
  }
}

function sendMessage(payload) {
  try {
    if (!contextValid()) return Promise.reject(new Error("context-invalidated"));
    return api.runtime.sendMessage(payload);
  } catch (err) {
    return Promise.reject(err);
  }
}

function withTimeout(promise, ms) {
  let timer;
  const timeout = new Promise((_, reject) => {
    timer = setTimeout(() => reject(new Error("timeout")), ms);
  });
  return Promise.race([promise, timeout]).finally(() => clearTimeout(timer));
}

function whenBodyReady(fn) {
  if (document.body) {
    fn();
    return;
  }
  document.addEventListener("DOMContentLoaded", fn, { once: true });
}

function syncStatusBanner() {
  if (!shouldShowStatusBanner() || !shieldActive) {
    removeStatusBanner();
    return;
  }
  whenBodyReady(() => {
    if (document.getElementById("guardi-status-banner")) updateStatusBannerMode(currentUiMode);
    else mountStatusBanner(currentUiMode);
  });
}

function applyVisualMode(managed) {
  const nextMode = parseUiMode(managed);
  currentUiMode = nextMode;
  const ui = getModeUi(nextMode);
  applyModeToDocument(document, nextMode);
  currentShieldSvg = shieldOverlaySvg(
    ui.cssVars["--guardi-shield-primary"] || "#2563EB",
    ui.cssVars["--guardi-shield-accent"] || "#93C5FD"
  );
  syncStatusBanner();
}

function renderedSize(img) {
  const rect = img.getBoundingClientRect();
  const width = Math.round(rect.width || img.clientWidth || img.width || 0);
  const height = Math.round(rect.height || img.clientHeight || img.height || 0);
  return { width, height };
}

function naturalSizeKnown(img) {
  return (img.naturalWidth || 0) > 0 && (img.naturalHeight || 0) > 0;
}

function isBigEnough(img) {
  const size = renderedSize(img);
  if (size.width >= minSize && size.height >= minSize) return true;
  if (size.width > 1 && size.height > 1) return false;
  if (!naturalSizeKnown(img)) return null;
  return img.naturalWidth >= minSize && img.naturalHeight >= minSize;
}

function isNearViewport(img) {
  const margin = isGoogleImages ? GOOGLE_VIEWPORT_MARGIN : VIEWPORT_MARGIN;
  const r = img.getBoundingClientRect();
  if (r.width < 2 || r.height < 2) return false;
  return r.bottom > -margin && r.right > -margin && r.top < window.innerHeight + margin && r.left < window.innerWidth + margin;
}

function isNearViewportForPreheat(img) {
  const margin = isGoogleImages ? GOOGLE_PREHEAT_MARGIN : VIEWPORT_MARGIN;
  const r = img.getBoundingClientRect();
  if (r.width < 2 || r.height < 2) return false;
  return r.bottom > -margin && r.right > -margin && r.top < window.innerHeight + margin && r.left < window.innerWidth + margin;
}

function isVisible(img) {
  const r = img.getBoundingClientRect();
  return r.width >= 2 && r.height >= 2 && r.bottom > 0 && r.right > 0 && r.top < window.innerHeight && r.left < window.innerWidth;
}

function pickLargestFromSrcset(srcset) {
  let bestUrl = "";
  let bestW = 0;
  for (const part of String(srcset || "").split(",")) {
    const bits = part.trim().split(/\s+/);
    if (!bits[0]) continue;
    const width = bits[1]?.endsWith("w") ? parseInt(bits[1], 10) : 0;
    if (width >= bestW) {
      bestUrl = bits[0];
      bestW = width;
    }
  }
  return /^https?:/i.test(bestUrl) ? bestUrl : "";
}

function resolveSource(img) {
  const direct = img.currentSrc || img.src || "";
  if (direct.startsWith("data:image/")) return { kind: "data", url: direct };
  if (/^https?:/i.test(direct)) return { kind: "url", url: direct };

  for (const attr of ["data-src", "data-iurl", "data-deferred-src", "data-lazy-src", "data-original"]) {
    const value = img.getAttribute(attr) || "";
    if (value.startsWith("data:image/")) return { kind: "data", url: value };
    if (/^https?:/i.test(value)) return { kind: "url", url: value };
  }

  const srcset = img.getAttribute("srcset");
  if (srcset) {
    const best = pickLargestFromSrcset(srcset);
    if (best) return { kind: "url", url: best };
  }
  return null;
}

function sourceKey(source) {
  if (!source) return "";
  if (source.kind === "url") return `u:${source.url}`;
  return `d:${source.url.length}:${source.url.slice(32, 128)}`;
}

function isOwnUi(node) {
  try {
    return !!node.closest?.(".guardi-shield, #guardi-status-banner");
  } catch {
    return false;
  }
}

function isPageChrome(img) {
  try {
    return !!img.closest?.('[role="banner"], header, footer, nav, #searchform, #gb, .RNNXgb, .o3j99, .gb_d');
  } catch {
    return false;
  }
}

function blur(img) {
  img.classList.add(BLUR_CLASS);
  img.classList.remove(BLOCKED_CLASS);
  img.removeAttribute(SAFE_ATTR);
}

function markSafe(img) {
  img.classList.remove(BLUR_CLASS, BLOCKED_CLASS);
  img.setAttribute(SAFE_ATTR, "1");
  img.setAttribute(VERDICT_ATTR, "1");
  removeShield(img);
}

function markBlocked(img) {
  img.classList.add(BLUR_CLASS, BLOCKED_CLASS);
  img.removeAttribute(SAFE_ATTR);
  img.setAttribute(VERDICT_ATTR, "1");
  ensureShield(img);
}

function markSeen(img) {
  img.classList.remove(BLUR_CLASS, BLOCKED_CLASS);
  img.removeAttribute(SAFE_ATTR);
  if (img.getAttribute(VERDICT_ATTR) !== "1") {
    img.setAttribute(VERDICT_ATTR, "0");
  }
  removeShield(img);
}

function markPending(img) {
  img.classList.remove(BLUR_CLASS, BLOCKED_CLASS);
  img.removeAttribute(SAFE_ATTR);
  if (img.getAttribute(VERDICT_ATTR) !== "1") {
    img.setAttribute(VERDICT_ATTR, "0");
  }
  removeShield(img);
}

function clearImageState(img) {
  img.classList.remove(BLUR_CLASS, BLOCKED_CLASS);
  img.removeAttribute(SAFE_ATTR);
  img.removeAttribute(VERDICT_ATTR);
  removeShield(img);
}

function dispositionOf(verdict) {
  if (verdict?.block) return "block";
  const reason = verdict?.reason;
  if (reason === "inactive") return "inactive";
  if (reason && !REVEAL_REASONS.has(reason)) return "pending";
  return "safe";
}

function applyVerdict(img, verdict) {
  if (!img?.isConnected || !shieldActive) return;
  const disposition = dispositionOf(verdict);
  if (disposition === "inactive") {
    setShieldState(false, { shieldActive: false });
    return;
  }
  if (disposition === "block") markBlocked(img);
  else if (disposition === "pending") markPending(img);
  else markSafe(img);
}

function applyVerdictToWaiters(key, verdict) {
  if (!shieldActive) return;
  if (shouldCacheVerdict(verdict)) verdictCache.set(key, verdict);
  const imgs = waiters.get(key);
  waiters.delete(key);
  if (!imgs) return;
  for (const img of imgs) applyVerdict(img, verdict);
}

function observeSource(img) {
  if (sourceWatchers.has(img)) return;
  let lastKey = sourceKey(resolveSource(img));
  const mo = new MutationObserver(() => {
    const nextKey = sourceKey(resolveSource(img));
    if (!nextKey || nextKey === lastKey) return;
    lastKey = nextKey;
    imageKeys.delete?.(img);
    clearImageState(img);
    processImage(img, { force: true });
  });
  sourceWatchers.set(img, mo);
  mo.observe(img, {
    attributes: true,
    attributeFilter: ["src", "srcset", "data-src", "data-iurl", "data-deferred-src", "data-lazy-src", "data-original"],
  });
}

function observeLoad(img) {
  if (loadWatchers.has(img)) return;
  loadWatchers.add(img);
  img.addEventListener(
    "load",
    () => {
      processImage(img, { force: true });
    },
    { once: true }
  );
}

function observeViewport(img) {
  if (!viewportObserver || !img?.isConnected) return;
  viewportObserver.observe(img);
}

function takeRateSlot() {
  const now = Date.now();
  if (now - rateWindowStart >= 1000) {
    rateWindowStart = now;
    jobsThisSecond = 0;
  }
  if (jobsThisSecond >= contentRateCap()) return false;
  jobsThisSecond++;
  return true;
}

function pumpQueue() {
  if (!shieldActive) return;
  while (runningJobs < contentMaxConcurrency() && queue.length) {
    if (!takeRateSlot()) {
      setTimeout(pumpQueue, 60);
      return;
    }
    const job = queue.shift();
    queuedKeys.delete(job.key);
    if (runningKeys.has(job.key) || verdictCache.has(job.key)) continue;
    runningKeys.add(job.key);
    runningJobs++;
    runJob(job);
  }
}

async function blobFromImageElement(img) {
  try {
    if (!(img instanceof HTMLImageElement) || !img.complete) return null;
    const nw = img.naturalWidth || 0;
    const nh = img.naturalHeight || 0;
    if (nw < 1 || nh < 1) return null;
    const scale = Math.min(1, 256 / Math.max(nw, nh));
    const w = Math.max(1, Math.round(nw * scale));
    const h = Math.max(1, Math.round(nh * scale));
    const canvas = document.createElement("canvas");
    canvas.width = w;
    canvas.height = h;
    const ctx = canvas.getContext("2d", { willReadFrequently: true });
    ctx.drawImage(img, 0, 0, w, h);
    return await new Promise((resolve, reject) => {
      canvas.toBlob((blob) => (blob ? resolve(blob) : reject(new Error("toBlob"))), "image/jpeg", 0.85);
    });
  } catch {
    return null;
  }
}

async function classifySource(job) {
  const { source, width, height, img } = job;
  const url = source.kind === "url" ? source.url : undefined;

  if (source.kind === "data") {
    const blob = await (await fetch(source.url)).blob();
    const buffer = await blob.arrayBuffer();
    return sendMessage({
      type: MSG_CLASSIFY_BLOB,
      id: ++msgId,
      mime: blob.type || "image/jpeg",
      buffer,
      width,
      height,
    });
  }

  if (img instanceof HTMLImageElement && img.complete) {
    const blob = await blobFromImageElement(img);
    if (blob) {
      const buffer = await blob.arrayBuffer();
      return sendMessage({
        type: MSG_CLASSIFY_BLOB,
        id: ++msgId,
        url,
        mime: blob.type || "image/jpeg",
        buffer,
        width,
        height,
      });
    }
  }

  return sendMessage({
    type: MSG_CLASSIFY,
    id: ++msgId,
    url: source.url,
    width,
    height,
  });
}

async function runJob(job) {
  let verdict = null;
  try {
    if (job.generation !== generation || !shieldActive) return;
    const response = await withTimeout(classifySource(job), JOB_TIMEOUT_MS);
    verdict = response?.verdict || { block: false, reason: "bad-response", score: 0 };
  } catch (err) {
    if (/context invalidated|context-invalidated/i.test(String(err?.message || err))) {
      deactivateFiltering({ reveal: true });
      return;
    }
    verdict = { block: false, reason: "error", error: String(err?.message || err), score: 0 };
  } finally {
    runningKeys.delete(job.key);
    runningJobs = Math.max(0, runningJobs - 1);
  }

  if (job.generation !== generation || !shieldActive) return;

  if (dispositionOf(verdict) === "pending" && job.attempt < MAX_ATTEMPTS) {
    queue.push({ ...job, attempt: job.attempt + 1 });
    queuedKeys.add(job.key);
  } else {
    applyVerdictToWaiters(job.key, verdict);
  }
  pumpQueue();
}

function enqueueClassification(img, source, key) {
  if (!waiters.has(key)) waiters.set(key, new Set());
  waiters.get(key).add(img);

  if (verdictCache.has(key)) {
    applyVerdict(img, verdictCache.get(key));
    return;
  }
  if (queuedKeys.has(key) || runningKeys.has(key)) return;

  const size = renderedSize(img);
  queuedKeys.add(key);
  queue.push({
    key,
    source,
    img,
    width: size.width || img.naturalWidth || 0,
    height: size.height || img.naturalHeight || 0,
    generation,
    attempt: 0,
    priority: isVisible(img) ? 0 : isNearViewport(img) ? 1 : 2,
  });
  if (queue.length > 1 && queue[queue.length - 1].priority < queue[queue.length - 2].priority) {
    queue.sort((a, b) => a.priority - b.priority);
  }
  pumpQueue();
}

function processImage(img, { force = false } = {}) {
  if (!shieldActive || !shouldFilterThisPage()) return;
  if (!(img instanceof HTMLImageElement)) return;
  if (!img.isConnected || isOwnUi(img) || isPageChrome(img)) return;

  const bigEnough = isBigEnough(img);
  if (bigEnough === false) {
    clearImageState(img);
    return;
  }

  if (!force && !isNearViewport(img)) {
    observeViewport(img);
    return;
  }

  if (bigEnough === null) observeLoad(img);

  const source = resolveSource(img);
  if (!source) {
    markSeen(img);
    observeSource(img);
    observeLoad(img);
    return;
  }

  const key = sourceKey(source);
  if (!key) return;
  const previousKey = imageKeys.get(img);
  if (previousKey === key && img.getAttribute(VERDICT_ATTR) === "1") return;
  if (previousKey && previousKey !== key) clearImageState(img);
  imageKeys.set(img, key);

  if (verdictCache.has(key)) {
    applyVerdict(img, verdictCache.get(key));
    return;
  }

  markSeen(img);
  enqueueClassification(img, source, key);
}

function scan(root = document) {
  if (!shieldActive || !shouldFilterThisPage()) return;
  const imgs = [];
  if (root instanceof HTMLImageElement) imgs.push(root);
  else if (root?.querySelectorAll) imgs.push(...root.querySelectorAll("img"));
  for (const img of imgs) {
    observeViewport(img);
    if (isNearViewportForPreheat(img)) processImage(img);
  }
}

function scheduleScan(root = document) {
  if (scanTimer) return;
  scanTimer = setTimeout(() => {
    scanTimer = 0;
    scan(root);
  }, 80);
}

function createObservers() {
  if (!viewportObserver) {
    viewportObserver = new IntersectionObserver(
      (entries) => {
        for (const entry of entries) {
          if (!entry.isIntersecting) continue;
          viewportObserver.unobserve(entry.target);
          processImage(entry.target, { force: true });
        }
      },
      { rootMargin: `${isGoogleImages ? GOOGLE_PREHEAT_MARGIN : VIEWPORT_MARGIN}px 0px`, threshold: 0.01 }
    );
  }

  if (!observer) {
    observer = new MutationObserver((mutations) => {
      for (const mutation of mutations) {
        for (const node of mutation.addedNodes) {
          if (node.nodeType !== 1) continue;
          if (node instanceof HTMLImageElement) processImage(node);
          else scheduleScan(node);
        }
        // Drop the shield the moment a blocked image is recycled out of the DOM (infinite
        // scroll, virtualized lists) instead of waiting for the next scroll/resize tick —
        // see positionShield's isConnected check for why this matters.
        for (const node of mutation.removedNodes) {
          if (node.nodeType !== 1) continue;
          if (node instanceof HTMLImageElement && shieldedImages.has(node)) removeShield(node);
          else if (node.querySelectorAll) {
            for (const img of node.querySelectorAll("img")) {
              if (shieldedImages.has(img)) removeShield(img);
            }
          }
        }
      }
    });
  }
}

function rootForObservation() {
  if (!isGoogleImages) return document.documentElement;
  for (const selector of GOOGLE_IMG_ROOTS) {
    const el = document.querySelector(selector);
    if (el) return el;
  }
  return document.documentElement;
}

function startFiltering() {
  if (!shouldFilterThisPage()) {
    syncStatusBanner();
    return;
  }
  if (filteringStarted) {
    syncStatusBanner();
    scheduleScan(document);
    return;
  }
  filteringStarted = true;
  createObservers();
  observer.observe(rootForObservation(), { childList: true, subtree: true });
  syncStatusBanner();
  scan(document);
}

function revealAll() {
  try {
    for (const img of document.querySelectorAll(`.${BLUR_CLASS}, .${BLOCKED_CLASS}`)) clearImageState(img);
    for (const shield of document.querySelectorAll(".guardi-shield")) shield.remove();
  } catch {
    // ignore detached documents
  }
}

function deactivateFiltering({ reveal = true } = {}) {
  generation++;
  filteringStarted = false;
  queue.length = 0;
  waiters.clear();
  queuedKeys.clear();
  runningKeys.clear();
  verdictCache.clear();
  clearTimeout(scanTimer);
  scanTimer = 0;
  removeStatusBanner();

  try {
    observer?.disconnect();
    viewportObserver?.disconnect();
  } catch {
    // Individual per-image observers become harmless while shieldActive is false.
  }
  shieldedImages.clear();
  runningJobs = 0;
  if (reveal) revealAll();
}

function setShieldState(active, managed = {}) {
  const normalizedActive = !!active && managed?.shieldActive !== false;
  currentManaged = managed || { shieldActive: normalizedActive };

  const tuned = applyManagedTuning(currentManaged, DEFAULTS, { minSize, maxPerSecond });
  if (Number.isFinite(tuned.minSize)) minSize = tuned.minSize;
  if (Number.isFinite(tuned.maxPerSecond)) maxPerSecond = tuned.maxPerSecond;
  applyVisualMode(currentManaged);

  if (normalizedActive === shieldActive) {
    if (shieldActive) startFiltering();
    else deactivateFiltering({ reveal: true });
    return;
  }

  shieldActive = normalizedActive;
  if (shieldActive) startFiltering();
  else deactivateFiltering({ reveal: true });
}

async function requestState() {
  try {
    const response = await sendMessage({ type: MSG_GET_STATE });
    if (response && typeof response.active === "boolean") {
      setShieldState(response.active, response.managed || { shieldActive: response.active });
      return;
    }
  } catch {
    // Fall through to the local mirror below.
  }

  try {
    const local = await api.storage.local.get(STATE_LOCAL_KEY);
    const snapshot = local?.[STATE_LOCAL_KEY];
    const fresh = snapshot?.ts && Date.now() - snapshot.ts < 6000;
    setShieldState(!!snapshot?.active && fresh, snapshot?.managed || { shieldActive: false });
  } catch {
    setShieldState(false, { shieldActive: false });
  }
}

function scheduleStatePoll() {
  if (statePollTimer) clearInterval(statePollTimer);
  statePollTimer = setInterval(() => {
    if (document.visibilityState === "visible") requestState();
  }, 10000);
}

function ensureShield(img) {
  if (!img.isConnected || !img.classList.contains(BLOCKED_CLASS)) return;
  let shield = shields.get(img);
  if (!shield) {
    shield = document.createElement("div");
    shield.className = "guardi-shield";
    shield.innerHTML = currentShieldSvg;
    document.documentElement.appendChild(shield);
    shields.set(img, shield);
    shieldedImages.add(img);
  }
  positionShield(img);
}

function positionShield(img) {
  const shield = shields.get(img);
  if (!shield) return;

  // Images recycled out of the DOM (infinite scroll, virtualization) must drop their
  // shield instead of just hiding it forever — otherwise shieldedImages only grows for
  // the rest of the session, keeping the (still-referenced) elements un-GC'd and making
  // every scroll/resize tick re-layout an ever-larger set of dead nodes.
  if (!img.isConnected) {
    removeShield(img);
    return;
  }

  const r = img.getBoundingClientRect();
  if (r.width < 2 || r.height < 2 || r.bottom < 0 || r.top > window.innerHeight) {
    shield.style.display = "none";
    return;
  }
  shield.style.display = "flex";
  shield.style.top = `${r.top + window.scrollY}px`;
  shield.style.left = `${r.left + window.scrollX}px`;
  shield.style.width = `${r.width}px`;
  shield.style.height = `${r.height}px`;
}

function removeShield(img) {
  const shield = shields.get(img);
  if (shield) shield.remove();
  shields.delete(img);
  shieldedImages.delete(img);
}

let shieldSyncScheduled = false;

function syncShields() {
  // scroll listens in capture phase (to catch nested scroll containers too) and can fire
  // many times per frame during a wheel/trackpad gesture; coalesce to one layout pass.
  if (shieldSyncScheduled) return;
  shieldSyncScheduled = true;
  requestAnimationFrame(() => {
    shieldSyncScheduled = false;
    for (const img of [...shieldedImages]) positionShield(img);
  });
}

requestState();
scheduleStatePoll();
document.addEventListener("visibilitychange", () => {
  if (document.visibilityState === "visible") requestState();
});
window.addEventListener("focus", requestState);
document.addEventListener("scroll", syncShields, { passive: true, capture: true });
window.addEventListener("resize", syncShields, { passive: true });

api.storage?.onChanged?.addListener?.((changes, area) => {
  if (area !== "local" || !changes[STATE_LOCAL_KEY]) return;
  const next = changes[STATE_LOCAL_KEY].newValue;
  if (!next) return;
  setShieldState(!!next.active, next.managed || { shieldActive: !!next.active });
});

api.runtime?.onMessage?.addListener?.((msg) => {
  if (msg?.type !== MSG_SHIELD_STATE) return;
  setShieldState(!!msg.active, msg.managed || { shieldActive: !!msg.active });
});


