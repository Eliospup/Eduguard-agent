/** Pings the running Guardi agent so it knows this extension background is alive. */

export const HEARTBEAT_URL = "http://127.0.0.1:38473/extension-heartbeat";
const MIN_INTERVAL_MS = 10000;

let lastSentAt = 0;

export function detectBrowserKind() {
  const ua = navigator.userAgent;
  if (ua.includes("Edg/")) return "edge";
  if (/Brave/i.test(ua)) return "brave";
  if (typeof browser !== "undefined") return "firefox";
  return "chrome";
}

export async function sendExtensionHeartbeat(api, status = {}) {
  const now = Date.now();
  if (now - lastSentAt < MIN_INTERVAL_MS) return;
  lastSentAt = now;

  let extensionId = "";
  let version = "";
  try {
    extensionId = api.runtime.id || "";
    version = api.runtime.getManifest()?.version || "";
  } catch {
    // ignore
  }

  const body = {
    type: "heartbeat",
    browser: detectBrowserKind(),
    extensionId,
    version,
    shieldActive: !!status.shieldActive,
    modelReady: !!status.modelReady,
  };

  try {
    await fetch(HEARTBEAT_URL, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
      cache: "no-store",
    });
  } catch {
    // Agent offline — HTTP infraction channel only runs while Guardi is up.
  }
}

export function installExtensionHeartbeat(api, getStatus) {
  const tick = () => sendExtensionHeartbeat(api, getStatus());

  tick();
  setInterval(tick, MIN_INTERVAL_MS);

  if (api.alarms?.create) {
    api.alarms.create("guardi-heartbeat", { periodInMinutes: 1 });
    api.alarms.onAlarm?.addListener?.((alarm) => {
      if (alarm.name === "guardi-heartbeat") tick();
    });
  }

  api.runtime?.onStartup?.addListener?.(() => tick());
  api.runtime?.onInstalled?.addListener?.(() => tick());
  api.tabs?.onActivated?.addListener?.(() => tick());
  api.windows?.onFocusChanged?.addListener?.((windowId) => {
    if (windowId !== api.windows?.WINDOW_ID_NONE) tick();
  });

  return { tick };
}
