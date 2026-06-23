import {
  applyModeToDocument,
  clearModeFromDocument,
  parseUiMode,
  formatCopy,
  personalizeCopy,
} from "./mode-ui.js";
import { fetchAgentShieldState } from "./agent-bridge.js";

const api = typeof browser !== "undefined" ? browser : chrome;
const STATE_LOCAL_KEY = "guardiShieldState";
const MSG_GET_STATE = "guardi:get-state";
const MSG_SHIELD_STATE = "guardi:shield-state";

function applyMode(managed) {
  const ui = applyModeToDocument(document, managed);
  applyPageCopy(ui, managed);
  return ui;
}

function applyPageCopy(ui, managed) {
  const copy = ui.copy;
  const fallback = copy.nameFallback || "sweetie";
  const displayName = managed?.displayName;
  document.querySelectorAll("[data-guardi='title']").forEach((el) => {
    el.textContent = copy.pageTitle;
  });
  document.querySelectorAll("[data-guardi='subtitle']").forEach((el) => {
    el.textContent = copy.pageSubtitle;
  });
  document.querySelectorAll("[data-guardi='popup-title']").forEach((el) => {
    el.textContent = personalizeCopy(copy.popupTitle, displayName, fallback);
  });
  document.querySelectorAll("[data-guardi='popup-subtitle']").forEach((el) => {
    el.textContent = copy.popupSubtitle;
  });
  document.querySelectorAll("[data-guardi='popup-body']").forEach((el) => {
    el.textContent = copy.popupBody;
  });
  document.querySelectorAll("[data-guardi='popup-badge']").forEach((el) => {
    el.textContent = copy.popupBadge;
  });
  document.querySelectorAll("[data-guardi='blocked-title']").forEach((el) => {
    el.textContent = copy.blockedTitle;
  });
  document.querySelectorAll("[data-guardi='blocked-subtitle']").forEach((el) => {
    el.textContent = copy.blockedSubtitle;
  });
  document.querySelectorAll("[data-guardi='blocked-body']").forEach((el) => {
    if (!el.dataset.guardiReason) el.textContent = copy.blockedBody;
  });
  document.querySelectorAll("[data-guardi='blocked-footer']").forEach((el) => {
    el.textContent = copy.blockedFooter;
  });
  document.querySelectorAll("[data-guardi='blocked-button']").forEach((el) => {
    el.textContent = copy.blockedButton;
  });
  applyYoutubeLimitCopy(ui, managed);
  const searchInput = document.getElementById("guardi-search-input");
  if (searchInput) searchInput.placeholder = copy.searchPlaceholder;
  const searchBtn = document.querySelector("#guardi-search button[type='submit']");
  if (searchBtn) searchBtn.textContent = copy.searchButton;
  const chips = document.getElementById("guardi-chips");
  if (chips && copy.chips) {
    chips.innerHTML = copy.chips
      .map(
        ([icon, label]) =>
          `<span class="guardi-chip"><span aria-hidden="true">${icon}</span> ${label}</span>`
      )
      .join("");
  }
}

function applyYoutubeLimitCopy(ui, managed, reason) {
  if (!document.body.classList.contains("guardi-youtube-limit-page")) return;

  const copy = ui.copy;
  const isStudy = reason === "study";
  document.title = copy.youtubeLimitPageTitle;
  document.querySelectorAll("[data-guardi='youtube-limit-title']").forEach((el) => {
    el.textContent = isStudy ? copy.youtubeStudyTitle : copy.youtubeLimitTitle;
  });
  document.querySelectorAll("[data-guardi='youtube-limit-subtitle']").forEach((el) => {
    el.textContent = isStudy ? copy.youtubeStudySubtitle : copy.youtubeLimitSubtitle;
  });
  document.querySelectorAll("[data-guardi='youtube-limit-body']").forEach((el) => {
    el.textContent = isStudy ? copy.youtubeStudyBody : copy.youtubeLimitBody;
  });
  document.querySelectorAll("[data-guardi='youtube-limit-footer']").forEach((el) => {
    el.textContent = isStudy ? copy.youtubeStudyFooter : copy.youtubeLimitFooter;
  });
  document.querySelectorAll("[data-guardi='youtube-limit-button']").forEach((el) => {
    el.textContent = copy.youtubeLimitButton;
  });
}

function applyBlockedReason(ui, reason) {
  const body = document.querySelector("[data-guardi='blocked-body']");
  if (!body || !reason) return;
  body.dataset.guardiReason = "1";
  body.textContent = formatCopy(ui.copy.blockedBodyReason, reason);
}

function getNativeNewTabUrl() {
  if (typeof browser !== "undefined") return "about:newtab";
  return "chrome://new-tab-page/";
}

function isGuardiNewTabPage() {
  return (
    document.body.classList.contains("guardi-newtab") &&
    !document.body.classList.contains("guardi-blocked-page")
  );
}

function wireBlockedBackButton() {
  const back = document.getElementById("guardi-back");
  if (!back) return;
  back.href = getNativeNewTabUrl();
  back.addEventListener("click", (event) => {
    event.preventDefault();
    releaseToNativeNewTab();
  });
}

async function getCurrentTab() {
  try {
    const result = api.tabs.getCurrent();
    if (result && typeof result.then === "function") return await result;
  } catch {
    // fall back to callback-style API below
  }

  return new Promise((resolve) => {
    try {
      api.tabs.getCurrent((tab) => {
        if (api.runtime.lastError) resolve(null);
        else resolve(tab || null);
      });
    } catch {
      resolve(null);
    }
  });
}

async function updateTabUrl(tabId, url) {
  try {
    const result = api.tabs.update(tabId, { url });
    if (result && typeof result.then === "function") await result;
  } catch {
    // ignore; location.replace below is enough for most extension pages
  }
}

async function releaseToNativeNewTab() {
  const defaultUrl = getNativeNewTabUrl();
  try {
    location.replace(defaultUrl);
  } catch {
    // ignore
  }

  const tab = await getCurrentTab();
  if (tab?.id) await updateTabUrl(tab.id, defaultUrl);
}

/** Supervision off: hide Guardi chrome until the tab is released/replaced. */
function applySilentInactiveShell() {
  clearModeFromDocument(document);
  document.documentElement.classList.remove("guardi-supervision-on");
  document.documentElement.classList.add("guardi-inactive");
  document.querySelector(".guardi-inactive-note")?.remove();

  const sky = document.querySelector(".guardi-sky");
  if (sky) sky.hidden = true;

  for (const el of document.querySelectorAll(".guardi-page, .guardi-popup, .guardi-card")) {
    el.hidden = true;
  }

  document.body.style.background = "#fff";
  document.documentElement.style.background = "#fff";
}

function applyActiveShell() {
  document.documentElement.classList.add("guardi-supervision-on");
  document.documentElement.classList.remove("guardi-inactive");
  document.querySelector(".guardi-inactive-note")?.remove();

  const sky = document.querySelector(".guardi-sky");
  if (sky) sky.hidden = false;

  for (const el of document.querySelectorAll(".guardi-page, .guardi-popup, .guardi-card")) {
    el.hidden = false;
  }

  document.body.style.background = "";
  document.documentElement.style.background = "";
}

function isYoutubeLimitPage() {
  return document.body.classList.contains("guardi-youtube-limit-page");
}

function readYoutubeBlocked(managed) {
  const value = managed?.youtubeBlocked;
  if (typeof value === "boolean") return value;
  if (typeof value === "number") return value !== 0;
  if (typeof value === "string") {
    const s = value.trim().toLowerCase();
    return s === "1" || s === "true" || s === "yes" || s === "on";
  }
  return false;
}

function applySnapshot(snapshot) {
  if (!snapshot) return;
  const params = new URLSearchParams(location.search);
  const reason = params.get("reason") || snapshot.managed?.youtubeBlockReason || "limit";
  const active = !!snapshot.active;
  const youtubeBlocked = readYoutubeBlocked(snapshot.managed);

  if (!active) {
    if (isGuardiNewTabPage()) {
      releaseToNativeNewTab();
      return;
    }
    if (isYoutubeLimitPage() && youtubeBlocked) {
      const ui = applyMode(snapshot.managed);
      applyYoutubeLimitCopy(ui, snapshot.managed, reason);
      applyActiveShell();
      return;
    }
    applySilentInactiveShell();
    return;
  }

  const ui = applyMode(snapshot.managed);
  if (params.get("r")) applyBlockedReason(ui, params.get("r"));
  if (isYoutubeLimitPage()) applyYoutubeLimitCopy(ui, snapshot.managed, reason);
  applyActiveShell();
}

async function resolveAgentSnapshot() {
  const agent = await fetchAgentShieldState();
  const active = agent.agentRunning === true && agent.active === true;
  const managed =
    agent.managed && typeof agent.managed === "object"
      ? { ...agent.managed, shieldActive: active ? agent.managed.shieldActive !== false : false }
      : { shieldActive: false };
  return { active, managed };
}

function requestState() {
  try {
    const request = api.runtime.sendMessage({ type: MSG_GET_STATE });
    if (request && typeof request.then === "function") {
      request
        .then((resp) => {
          if (resp && typeof resp.active === "boolean") applySnapshot(resp);
          else resolveAgentSnapshot().then(applySnapshot);
        })
        .catch(() => resolveAgentSnapshot().then(applySnapshot));
      return;
    }

    api.runtime.sendMessage({ type: MSG_GET_STATE }, (resp) => {
      if (!api.runtime.lastError && resp && typeof resp.active === "boolean") {
        applySnapshot({ active: resp.active, managed: resp.managed });
        return;
      }
      resolveAgentSnapshot().then(applySnapshot);
    });
  } catch {
    resolveAgentSnapshot().then(applySnapshot);
  }
}

wireBlockedBackButton();
requestState();
setInterval(requestState, 3000);

api.storage.onChanged.addListener((changes, area) => {
  if (area !== "local" || !changes[STATE_LOCAL_KEY]) return;
  const next = changes[STATE_LOCAL_KEY].newValue;
  if (!next?.active) applySnapshot(next);
  else requestState();
});

try {
  api.runtime.onMessage.addListener((msg) => {
    if (msg?.type !== MSG_SHIELD_STATE) return;
    applySnapshot({ active: msg.active, managed: msg.managed });
  });
} catch {
  // ignore
}
