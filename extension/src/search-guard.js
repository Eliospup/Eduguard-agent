import { matchBlockedSearch, extractSearchQuery } from "./blocked-search-terms.js";

const NATIVE_HOST = "com.guardi.eduguard";
const INFRACTION_HTTP = "http://127.0.0.1:38473/blocked-search";
const MSG_CHECK_SEARCH = "guardi:check-search";
const MSG_BLOCKED_SEARCH = "guardi:blocked-search";

const SEARCH_URL_PATTERNS = [
  "*://*.google.com/search*",
  "*://google.com/search*",
  "*://*.google.fr/search*",
  "*://*.google.co.uk/search*",
  "*://*.bing.com/search*",
  "*://bing.com/search*",
  "*://duckduckgo.com/*",
  "*://www.duckduckgo.com/*",
  "*://search.yahoo.com/search*",
];

let lastReportAt = 0;
const REPORT_COOLDOWN_MS = 4000;

export { MSG_CHECK_SEARCH, MSG_BLOCKED_SEARCH };

export function blockedPageUrl(api, label) {
  const params = new URLSearchParams();
  if (label) params.set("r", label.slice(0, 40));
  const qs = params.toString();
  return api.runtime.getURL(`blocked-search.html${qs ? `?${qs}` : ""}`);
}

export function checkSearchQuery(query) {
  const match = matchBlockedSearch(query);
  return match ? { blocked: true, label: match.label, kind: match.kind } : { blocked: false };
}

function postNative(api, payload) {
  return new Promise((resolve) => {
    try {
      const port = api.runtime.connectNative(NATIVE_HOST);
      let settled = false;
      const finish = (ok) => {
        if (settled) return;
        settled = true;
        try {
          port.disconnect();
        } catch {
          // ignore
        }
        resolve(ok);
      };
      port.onMessage.addListener(() => finish(true));
      port.onDisconnect.addListener(() => finish(!api.runtime.lastError));
      port.postMessage(payload);
      setTimeout(() => finish(false), 2500);
    } catch {
      resolve(false);
    }
  });
}

function postHttp(payload) {
  return fetch(INFRACTION_HTTP, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload),
  })
    .then((r) => r.ok)
    .catch(() => false);
}

export function reportBlockedSearch(api, query, match) {
  const now = Date.now();
  if (now - lastReportAt < REPORT_COOLDOWN_MS) return;
  lastReportAt = now;

  const payload = {
    type: "blocked_search",
    query: String(query || "").slice(0, 160),
    match: match?.label || match || "",
  };

  postHttp(payload).then((ok) => {
    if (!ok) postNative(api, payload);
  });
}

function evaluateSearchUrl(url, isActive) {
  if (!isActive()) return null;
  const query = extractSearchQuery(url);
  if (!query) return null;
  const match = matchBlockedSearch(query);
  if (!match) return null;
  return { query, match };
}

/**
 * @param {typeof chrome} api
 * @param {() => boolean} isActive
 */
export function installSearchGuard(api, isActive) {
  if (api.webRequest?.onBeforeRequest) {
    api.webRequest.onBeforeRequest.addListener(
      (details) => {
        if (!isActive()) return {};
        try {
          const hit = evaluateSearchUrl(details.url, isActive);
          if (!hit) return {};

          reportBlockedSearch(api, hit.query, hit.match);
          return { redirectUrl: blockedPageUrl(api, hit.match.label) };
        } catch {
          return {};
        }
      },
      {
        urls: SEARCH_URL_PATTERNS,
        types: ["main_frame"],
      },
      ["blocking"]
    );
  } else if (api.tabs?.onUpdated) {
    api.tabs.onUpdated.addListener((tabId, changeInfo, tab) => {
      if (!isActive()) return;
      const url = changeInfo.url || tab.url;
      if (!url || !/^https?:/i.test(url)) return;
      const hit = evaluateSearchUrl(url, isActive);
      if (!hit) return;
      reportBlockedSearch(api, hit.query, hit.match);
      api.tabs.update(tabId, { url: blockedPageUrl(api, hit.match.label) }).catch(() => {});
    });
  }

  return {
    handleMessage(msg, sendResponse) {
      if (msg?.type === MSG_CHECK_SEARCH) {
        sendResponse(checkSearchQuery(msg.query || ""));
        return true;
      }
      if (msg?.type === MSG_BLOCKED_SEARCH) {
        const match =
          matchBlockedSearch(msg.query || "") || { label: msg.match || "naughty" };
        reportBlockedSearch(api, msg.query, match);
        sendResponse({ ok: true, url: blockedPageUrl(api, match.label) });
        return false;
      }
      return false;
    },
  };
}
