// Category web filter — auto-categorises and blocks sites by hostname keyword.
// Curated category domains are already blocked at DNS level by the agent's hosts
// file; this guard catches the long tail — look-alike / uncatalogued sites whose
// hostname contains a high-signal category token (porn, casino, gore, ...). The
// token list is pushed live via the agent shield-state (managed.blockedCategoryKeywords).

const URL_PATTERNS = ["http://*/*", "https://*/*"];

function normalizeKeywords(managed) {
  const raw = managed && managed.blockedCategoryKeywords;
  if (!Array.isArray(raw)) return [];
  const out = [];
  for (const token of raw) {
    if (typeof token !== "string") continue;
    const t = token.trim().toLowerCase();
    if (t.length >= 3) out.push(t);
  }
  return out;
}

function hostnameOf(url) {
  if (!url || typeof url !== "string") return "";
  try {
    const parsed = new URL(url);
    if (parsed.protocol !== "http:" && parsed.protocol !== "https:") return "";
    return parsed.hostname.replace(/^www\./i, "").toLowerCase();
  } catch {
    return "";
  }
}

/** First matching keyword for a hostname, or null. */
function matchKeyword(host, keywords) {
  if (!host) return null;
  for (const kw of keywords) {
    if (host.includes(kw)) return kw;
  }
  return null;
}

/**
 * @param {typeof chrome} api
 * @param {() => object} getManaged
 */
export function installCategoryGuard(api, getManaged) {
  let blockedPageBase = "";

  function refreshBase() {
    try {
      blockedPageBase = api.runtime.getURL("category-blocked.html");
    } catch {
      blockedPageBase = "";
    }
  }
  refreshBase();

  function blockedUrlFor(keyword) {
    const base = blockedPageBase || api.runtime.getURL("category-blocked.html");
    if (!keyword) return base;
    const join = base.includes("?") ? "&" : "?";
    return `${base}${join}category=${encodeURIComponent(keyword)}`;
  }

  function currentKeywords() {
    return normalizeKeywords(getManaged() || {});
  }

  function redirectMatchingTabs() {
    const keywords = currentKeywords();
    if (keywords.length === 0 || !api.tabs?.query) return;
    api.tabs
      .query({ url: URL_PATTERNS })
      .then((tabs) => {
        for (const tab of tabs) {
          if (!tab?.id || !tab.url) continue;
          const host = hostnameOf(tab.url);
          const hit = matchKeyword(host, keywords);
          if (!hit) continue;
          api.tabs.update(tab.id, { url: blockedUrlFor(hit) }).catch(() => {});
        }
      })
      .catch(() => {});
  }

  if (api.webRequest?.onBeforeRequest) {
    api.webRequest.onBeforeRequest.addListener(
      (details) => {
        const keywords = currentKeywords();
        if (keywords.length === 0) return {};
        const host = hostnameOf(details.url);
        const hit = matchKeyword(host, keywords);
        if (!hit) return {};
        return { redirectUrl: blockedUrlFor(hit) };
      },
      { urls: URL_PATTERNS, types: ["main_frame"] },
      ["blocking"]
    );
  } else if (api.tabs?.onUpdated) {
    api.tabs.onUpdated.addListener((tabId, changeInfo, tab) => {
      const keywords = currentKeywords();
      if (keywords.length === 0) return;
      const url = changeInfo.url || tab.url;
      const host = hostnameOf(url);
      const hit = matchKeyword(host, keywords);
      if (!hit) return;
      api.tabs.update(tabId, { url: blockedUrlFor(hit) }).catch(() => {});
    });
  }

  return {
    onManagedChange() {
      redirectMatchingTabs();
    },
    sync() {
      redirectMatchingTabs();
    },
  };
}
