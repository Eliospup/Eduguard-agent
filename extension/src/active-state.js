// Runtime on/off flag pushed by Guardi via /shield-state.
// Firefox content scripts cannot read storage.managed reliably, so the background
// mirrors the live state to storage.local for content scripts and extension pages.
// The running Guardi agent is the source of truth: stale local/policy state must
// never reactivate filtering by itself after Guardi has stopped.

import { fetchAgentShieldState } from "./agent-bridge.js";
import { sendExtensionHeartbeat } from "./heartbeat.js";

export const MSG_GET_STATE = "guardi:get-state";
export const MSG_SHIELD_STATE = "guardi:shield-state";
export const STATE_LOCAL_KEY = "guardiShieldState";

const ACTIVE_SNAPSHOT_MAX_AGE_MS = 6000;

function runtimeBrowserKey() {
  if (typeof browser !== "undefined") return "firefox";
  const ua = navigator.userAgent || "";
  if (/\bEdg\//i.test(ua)) return "edge";
  return "chrome";
}

function readBool(value, fallback = false) {
  if (typeof value === "boolean") return value;
  if (typeof value === "number") return value !== 0;
  if (typeof value === "string") {
    const s = value.trim().toLowerCase();
    if (s === "1" || s === "true" || s === "yes" || s === "on") return true;
    if (s === "0" || s === "false" || s === "no" || s === "off") return false;
  }
  return fallback;
}

export function parseShieldActive(managed) {
  if (!managed || typeof managed !== "object") return false;
  return readBool(managed.shieldActive ?? managed.enabled, false);
}

function browserEnabledByManaged(managed) {
  if (!managed || typeof managed !== "object") return true;

  const key = runtimeBrowserKey();
  const map = managed.browserActive || managed.browser_active || managed.browsers;
  if (map && typeof map === "object") {
    const value = map[key] ?? (key === "chrome" ? map.chromium : undefined);
    if (typeof value === "object" && value !== null) {
      if ("active" in value) return readBool(value.active, true);
      if ("enabled" in value) return readBool(value.enabled, true);
    }
    if (value !== undefined) return readBool(value, true);
  }

  const direct =
    key === "firefox"
      ? managed.firefoxActive ?? managed.firefox_active
      : key === "edge"
      ? managed.edgeActive ?? managed.edge_active
      : managed.chromeActive ?? managed.chrome_active ?? managed.chromiumActive ?? managed.chromium_active;
  if (direct !== undefined) return readBool(direct, true);

  return true;
}

export function normalizeManagedForRuntime(managed, agentActive) {
  const source = managed && typeof managed === "object" ? managed : {};
  const sourceHasToggle = "shieldActive" in source || "enabled" in source;
  const wantsShield = sourceHasToggle ? parseShieldActive(source) : !!agentActive;
  const browserAllowed = browserEnabledByManaged(source);
  return {
    ...source,
    runtimeBrowser: runtimeBrowserKey(),
    shieldActive: !!agentActive && wantsShield && browserAllowed,
  };
}

function isFreshActiveSnapshot(snapshot) {
  if (!snapshot?.active) return true;
  if (!Number.isFinite(snapshot.ts)) return false;
  return Date.now() - snapshot.ts <= ACTIVE_SNAPSHOT_MAX_AGE_MS;
}

export function applyManagedTuning(managed, defaults, target) {
  if (!managed || typeof managed !== "object") return target;
  const num = (v) => (typeof v === "string" ? parseFloat(v) : v);
  const out = { ...target };
  if (Number.isFinite(num(managed.minSize))) out.minSize = num(managed.minSize);
  if (Number.isFinite(num(managed.nsfwThreshold))) out.nsfwThreshold = num(managed.nsfwThreshold);
  if (Number.isFinite(num(managed.thumbThreshold))) out.thumbThreshold = num(managed.thumbThreshold);
  if (Number.isFinite(num(managed.sexyWeight))) out.sexyWeight = num(managed.sexyWeight);
  if (Number.isFinite(num(managed.maxPerSecond))) out.maxPerSecond = num(managed.maxPerSecond);
  out.shieldActive = parseShieldActive(managed);
  if (typeof managed.uiMode === "string" && managed.uiMode.trim()) {
    out.uiMode = managed.uiMode.trim();
  }
  return out;
}

export async function publishShieldState(api, active, managed) {
  const normalizedManaged = normalizeManagedForRuntime(managed, active);
  const normalizedActive = !!active && normalizedManaged.shieldActive === true;
  await api.storage.local.set({
    [STATE_LOCAL_KEY]: {
      active: normalizedActive,
      managed: normalizedManaged,
      ts: Date.now(),
    },
  });
}

function broadcastShieldState(api, active, managed) {
  if (!api.tabs?.query) return;
  const normalizedManaged = normalizeManagedForRuntime(managed, active);
  const normalizedActive = !!active && normalizedManaged.shieldActive === true;
  const payload = { type: MSG_SHIELD_STATE, active: normalizedActive, managed: normalizedManaged };
  api.tabs
    .query({})
    .then((tabs) => {
      for (const tab of tabs) {
        if (!tab?.id || tab.id < 0) continue;
        try {
          api.tabs.sendMessage(tab.id, payload).catch(() => {});
        } catch {
          // ignore tabs that do not have the content script loaded
        }
      }
    })
    .catch(() => {});
}

/** Background only: Guardi /shield-state is the sole source of truth. */
export function createBackgroundStateWatcher(api, { onChange, pollMs = 2500, getHeartbeatStatus } = {}) {
  let active = false;
  let pollTimer = 0;
  let bootstrapped = false;

  async function refresh() {
    const agent = await fetchAgentShieldState();
    const agentActive = agent.agentRunning === true && agent.active === true;

    if (!agentActive) {
      const managed = normalizeManagedForRuntime({ shieldActive: false }, false);
      const wasActive = active;
      active = false;
      await publishShieldState(api, false, managed);
      broadcastShieldState(api, false, managed);
      if (!bootstrapped || wasActive) onChange?.(false, managed);
      bootstrapped = true;
      return { active: false, managed };
    }

    const managed = normalizeManagedForRuntime(agent.managed || { shieldActive: true }, true);
    const next = managed.shieldActive === true;
    const changed = next !== active;
    active = next;

    await publishShieldState(api, active, managed);
    broadcastShieldState(api, active, managed);

    if (active) {
      sendExtensionHeartbeat(api, getHeartbeatStatus?.() ?? { shieldActive: active });
    }

    if (!bootstrapped || changed) onChange?.(active, managed);
    bootstrapped = true;
    return { active, managed };
  }

  function start() {
    if (pollTimer) return;
    refresh();
    if (api.storage?.onChanged) {
      api.storage.onChanged.addListener((changes, area) => {
        if (area === "managed") refresh();
      });
    }
    pollTimer = setInterval(refresh, pollMs);
  }

  return { start, refresh, isActive: () => active };
}

/** Content / extension pages: read the mirrored state, but never trust stale active snapshots. */
export function createContentStateWatcher(api, { onChange, pollMs = 3000 } = {}) {
  let pollTimer = 0;

  function applySnapshot(snapshot, { requireFresh = false } = {}) {
    if (!snapshot || typeof snapshot !== "object") {
      onChange?.(false, { shieldActive: false });
      return;
    }

    const freshEnough = !requireFresh || isFreshActiveSnapshot(snapshot);
    const requestedActive = !!snapshot.active && freshEnough;
    const managed = normalizeManagedForRuntime(snapshot.managed, requestedActive);
    const isActive = requestedActive && managed.shieldActive === true;
    onChange?.(isActive, { ...managed, shieldActive: isActive });
  }

  function requestBackgroundRefresh() {
    try {
      const req = api.runtime.sendMessage({ type: MSG_GET_STATE });
      const apply = (resp) => {
        if (resp && typeof resp.active === "boolean") {
          applySnapshot({ active: resp.active, managed: resp.managed }, { requireFresh: false });
        }
      };
      if (req && typeof req.then === "function") {
        req.then(apply).catch(() => {});
        return;
      }
      api.runtime.sendMessage({ type: MSG_GET_STATE }, (resp) => {
        if (api.runtime.lastError || !resp) return;
        apply(resp);
      });
    } catch {
      // ignore transient extension startup failures
    }
  }

  function start() {
    api.storage.local.get(STATE_LOCAL_KEY).then((data) => {
      applySnapshot(data[STATE_LOCAL_KEY], { requireFresh: true });
      requestBackgroundRefresh();
    });
    if (api.storage?.onChanged) {
      api.storage.onChanged.addListener((changes, area) => {
        if (area === "local" && changes[STATE_LOCAL_KEY]) {
          applySnapshot(changes[STATE_LOCAL_KEY].newValue, { requireFresh: true });
        }
      });
    }
    if (!pollTimer) pollTimer = setInterval(requestBackgroundRefresh, pollMs);
  }

  return { start };
}

/** @deprecated Use createBackgroundStateWatcher or createContentStateWatcher. */
export function createActiveStateWatcher(api, opts) {
  return createBackgroundStateWatcher(api, opts);
}
