const YOUTUBE_URL_PATTERNS = [
  "*://*.youtube.com/*",
  "*://youtube.com/*",
  "*://youtu.be/*",
  "*://*.youtu.be/*",
  "*://music.youtube.com/*",
];

function readBool(value) {
  if (typeof value === "boolean") return value;
  if (typeof value === "number") return value !== 0;
  if (typeof value === "string") {
    const s = value.trim().toLowerCase();
    if (s === "1" || s === "true" || s === "yes" || s === "on") return true;
    if (s === "0" || s === "false" || s === "no" || s === "off") return false;
  }
  return false;
}

function isYoutubeContentUrl(url) {
  if (!url || typeof url !== "string") return false;
  try {
    const parsed = new URL(url);
    const host = parsed.hostname.replace(/^www\./i, "").toLowerCase();
    if (host === "youtu.be") return true;
    if (host === "youtube.com" || host.endsWith(".youtube.com")) return true;
    return false;
  } catch {
    return false;
  }
}

/**
 * @param {typeof chrome} api
 * @param {() => object} getManaged
 */
export function installYoutubeTimeGuard(api, getManaged) {
  let blockedPageBase = "";
  let lastBlocked = false;
  let lastReason = "";

  function refreshBlockedPageBase() {
    try {
      blockedPageBase = api.runtime.getURL("youtube-time-blocked.html");
    } catch {
      blockedPageBase = "";
    }
  }

  refreshBlockedPageBase();

  function currentBlockState() {
    const managed = getManaged() || {};
    return {
      blocked: readBool(managed.youtubeBlocked),
      reason: typeof managed.youtubeBlockReason === "string" ? managed.youtubeBlockReason : "limit",
    };
  }

  function blockedPageUrl(reason) {
    const base = blockedPageBase || api.runtime.getURL("youtube-time-blocked.html");
    if (!reason || reason === "limit") return base;
    const join = base.includes("?") ? "&" : "?";
    return `${base}${join}reason=${encodeURIComponent(reason)}`;
  }

  function redirectYoutubeTabs(reason) {
    if (!api.tabs?.query) return;
    const target = blockedPageUrl(reason);
    api.tabs
      .query({ url: YOUTUBE_URL_PATTERNS })
      .then((tabs) => {
        for (const tab of tabs) {
          if (!tab?.id || !tab.url) continue;
          if (tab.url.startsWith(target.split("?")[0])) continue;
          api.tabs.update(tab.id, { url: target }).catch(() => {});
        }
      })
      .catch(() => {});
  }

  function sync(force = false) {
    const { blocked, reason } = currentBlockState();
    if (blocked && (force || !lastBlocked || reason !== lastReason)) {
      redirectYoutubeTabs(reason);
    }
    lastBlocked = blocked;
    lastReason = reason;
  }

  if (api.webRequest?.onBeforeRequest) {
    api.webRequest.onBeforeRequest.addListener(
      (details) => {
        const { blocked, reason } = currentBlockState();
        if (!blocked) return {};
        if (!isYoutubeContentUrl(details.url)) return {};
        const target = blockedPageUrl(reason);
        if (details.url.startsWith(target.split("?")[0])) return {};
        return { redirectUrl: target };
      },
      { urls: YOUTUBE_URL_PATTERNS, types: ["main_frame"] },
      ["blocking"]
    );
  } else if (api.tabs?.onUpdated) {
    api.tabs.onUpdated.addListener((tabId, changeInfo, tab) => {
      const { blocked, reason } = currentBlockState();
      if (!blocked) return;
      const url = changeInfo.url || tab.url;
      if (!isYoutubeContentUrl(url)) return;
      const target = blockedPageUrl(reason);
      if (url.startsWith(target.split("?")[0])) return;
      api.tabs.update(tabId, { url: target }).catch(() => {});
    });
  }

  return {
    sync,
    onManagedChange() {
      sync(true);
    },
  };
}
