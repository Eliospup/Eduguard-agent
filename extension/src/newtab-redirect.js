/** Redirect blank tabs to Guardi new-tab only while supervision is active (Chromium).
 *  Firefox instead uses the native chrome_url_overrides.newtab (see esbuild.config.mjs)
 *  so the address bar stays blank like a normal new tab — moz-extension:// URLs there
 *  read as suspicious. That override can't be toggled at runtime, so newtab.html
 *  renders a neutral, unbranded page itself whenever supervision is inactive.
 */

import { isAgentSupervisionActive } from "./agent-bridge.js";

const BLANK_NEWTAB_URLS = new Set(["about:newtab", "about:home", "about:blank"]);

/**
 * Grace window before treating a blank tab as "genuinely idle, take it over". A real
 * navigation (link click, JS/auth redirect chain) can sit at about:blank for a few hundred
 * ms before its target URL lands — redirecting too eagerly hijacked those clicks to
 * Guardi's new tab instead of letting them complete. A deliberately-opened blank tab just
 * takes a little longer to receive the Guardi page, which is barely noticeable.
 */
const RedirectGraceDelaysMs = [300, 800, 1500, 2500];

/** Tabs navigating to a real site — never hijack. */
const allowedNavigations = new Set();
/** Prevent double-redirect on the same tab. */
const redirectInFlight = new Set();

function normalizeUrl(url) {
  if (!url) return "";
  return url.split("#")[0].split("?")[0];
}

function isBlankNewTab(url) {
  return BLANK_NEWTAB_URLS.has(normalizeUrl(url));
}

function isHttpUrl(url) {
  return /^https?:/i.test(url || "");
}

function isGuardiNewTab(url, guardiUrl) {
  if (!url || typeof url !== "string") return false;
  if (url === guardiUrl) return true;
  return (
    (url.startsWith("moz-extension:") || url.startsWith("chrome-extension:")) &&
    url.includes("/newtab.html")
  );
}

function getNativeNewTabUrl(api) {
  if (typeof browser !== "undefined") return "about:newtab";
  if (api.runtime.getURL("").startsWith("chrome-extension://")) return "chrome://new-tab-page/";
  return "about:newtab";
}

function markAllowedNavigation(tabId, ms = 8000) {
  if (!tabId) return;
  allowedNavigations.add(tabId);
  setTimeout(() => allowedNavigations.delete(tabId), ms);
}

/**
 * @param {typeof chrome} api
 * @param {{ confirmActive?: () => boolean | Promise<boolean>, enabled?: boolean }} [opts]
 */
export function installNewTabRedirect(
  api,
  { confirmActive = isAgentSupervisionActive, enabled = true } = {}
) {
  const guardiUrl = api.runtime.getURL("newtab.html");
  const nativeNewTabUrl = getNativeNewTabUrl(api);

  async function releaseGuardiNewTabs() {
    if (!api.tabs?.query) return;
    let tabs = [];
    try {
      tabs = await api.tabs.query({});
    } catch {
      return;
    }
    for (const tab of tabs) {
      if (!tab.id) continue;
      if (!isGuardiNewTab(tab.url, guardiUrl) && !isGuardiNewTab(tab.pendingUrl, guardiUrl)) continue;
      try {
        await api.tabs.update(tab.id, { url: nativeNewTabUrl });
      } catch {
        // ignore
      }
    }
  }

  if (!enabled || !api.tabs?.query) {
    return {
      rescanAllBlankTabs: async () => {},
      releaseGuardiNewTabs,
      syncTabsForCurrentState: releaseGuardiNewTabs,
    };
  }

  async function isConfirmedActive() {
    try {
      const result = confirmActive();
      return !!(typeof result?.then === "function" ? await result : result);
    } catch {
      return false;
    }
  }

  async function shouldRedirectTab(tabId) {
    if (allowedNavigations.has(tabId) || redirectInFlight.has(tabId)) return false;
    if (!(await isConfirmedActive())) return false;

    let tab;
    try {
      tab = await api.tabs.get(tabId);
    } catch {
      return false;
    }

    if (isHttpUrl(tab.pendingUrl) || isHttpUrl(tab.url)) return false;
    if (isGuardiNewTab(tab.url, guardiUrl) || isGuardiNewTab(tab.pendingUrl, guardiUrl)) return false;

    const current = normalizeUrl(tab.url || tab.pendingUrl);
    return isBlankNewTab(current);
  }

  async function redirectIfBlank(tabId) {
    if (!(await shouldRedirectTab(tabId))) return;
    redirectInFlight.add(tabId);
    try {
      await api.tabs.update(tabId, { url: guardiUrl });
    } catch {
      // ignore
    } finally {
      setTimeout(() => redirectInFlight.delete(tabId), 800);
    }
  }

  async function rescanAllBlankTabs() {
    if (!(await isConfirmedActive())) return;
    let tabs = [];
    try {
      tabs = await api.tabs.query({});
    } catch {
      return;
    }
    for (const tab of tabs) {
      if (tab.id) await redirectIfBlank(tab.id);
    }
  }

  async function syncTabsForCurrentState() {
    if (await isConfirmedActive()) await rescanAllBlankTabs();
    else await releaseGuardiNewTabs();
  }

  api.tabs.onUpdated.addListener((tabId, changeInfo) => {
    if (!changeInfo.url) return;

    if (isHttpUrl(changeInfo.url)) {
      markAllowedNavigation(tabId);
      return;
    }
    if (isGuardiNewTab(changeInfo.url, guardiUrl)) return;
    if (isBlankNewTab(changeInfo.url)) {
      // Don't hijack immediately — about:blank is a normal transient state mid-navigation
      // (window.open, client-side/auth redirects) before the real URL lands. redirectIfBlank
      // re-checks freshness itself, so a real navigation that lands in the meantime cancels
      // these via markAllowedNavigation.
      for (const delay of RedirectGraceDelaysMs) {
        setTimeout(() => redirectIfBlank(tabId), delay);
      }
    }
  });

  api.tabs.onCreated.addListener((tab) => {
    if (!tab.id) return;
    if (isHttpUrl(tab.pendingUrl) || isHttpUrl(tab.url)) {
      markAllowedNavigation(tab.id);
      return;
    }
    for (const delay of RedirectGraceDelaysMs) {
      setTimeout(() => redirectIfBlank(tab.id), delay);
    }
  });

  api.runtime.onStartup?.addListener?.(() => {
    for (const delay of [0, 400, 1200, 2500]) {
      setTimeout(syncTabsForCurrentState, delay);
    }
  });

  api.runtime.onInstalled?.addListener?.(() => {
    for (const delay of [0, 400, 1200]) {
      setTimeout(syncTabsForCurrentState, delay);
    }
  });

  return { rescanAllBlankTabs, releaseGuardiNewTabs, syncTabsForCurrentState };
}
