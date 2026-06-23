// Status chip — Guardi mascot + mode-aware copy.

import { applyModeToDocument, getModeUi, mascotSvg, parseUiMode } from "./mode-ui.js";

const BANNER_ID = "guardi-status-banner";

let bannerEl = null;
let resizeObserver = null;
let headerObserver = null;
let scheduled = 0;
let currentMode = "sub";

function findSearchForm() {
  return (
    document.querySelector('form[role="search"]') ||
    document.querySelector("#searchform") ||
    document.querySelector("form#tsf")
  );
}

function findHeaderRightEdge() {
  const anchors = [
    'a[aria-label*="Google Account"]',
    'a[aria-label*="Compte Google"]',
    'a[aria-label="Sign in"]',
    'a[aria-label="Connexion"]',
    'a[aria-label*="Settings"]',
    'a[aria-label*="Paramètres"]',
    '[aria-label="Google apps"]',
    '[aria-label*="Applications Google"]',
    "#gbwa",
  ];
  let leftmost = window.innerWidth - 72;

  for (const sel of anchors) {
    const el = document.querySelector(sel);
    if (!el) continue;
    const r = el.getBoundingClientRect();
    if (r.width > 0 && r.height > 0 && r.left < leftmost) {
      leftmost = r.left;
    }
  }

  return leftmost;
}

function layoutBanner() {
  if (!bannerEl) return;

  const search = findSearchForm();
  bannerEl.classList.remove("guardi-status-banner--compact");

  if (search) {
    const sr = search.getBoundingClientRect();
    const rightEdge = findHeaderRightEdge();
    const gapLeft = sr.right + 10;
    const gapRight = rightEdge - 10;
    const gapWidth = gapRight - gapLeft;
    const bannerWidth = bannerEl.offsetWidth || 280;

    if (gapWidth < 40) {
      bannerEl.style.display = "none";
      return;
    }

    if (gapWidth < bannerWidth + 12) {
      bannerEl.classList.add("guardi-status-banner--compact");
    }

    const measured = bannerEl.offsetWidth || 120;
    const left = gapLeft + Math.max(0, (gapWidth - measured) / 2);
    const top = sr.top + Math.max(0, (sr.height - bannerEl.offsetHeight) / 2);

    bannerEl.style.display = "flex";
    bannerEl.style.top = `${Math.max(6, top)}px`;
    bannerEl.style.left = `${left}px`;
    bannerEl.style.right = "auto";
    bannerEl.style.transform = "none";
    return;
  }

  bannerEl.style.display = "flex";
  bannerEl.style.top = "12px";
  bannerEl.style.left = "auto";
  bannerEl.style.right = `${Math.max(16, Math.min(window.innerWidth * 0.08, 140))}px`;
  bannerEl.style.transform = "none";
}

function scheduleLayout() {
  if (scheduled) return;
  scheduled = requestAnimationFrame(() => {
    scheduled = 0;
    layoutBanner();
  });
}

function renderBannerContent(ui) {
  const primary = ui.cssVars["--guardi-shield-primary"]?.replace(/"/g, "") || "#0EA5E9";
  const accent = ui.cssVars["--guardi-shield-accent"]?.replace(/"/g, "") || "#7DD3FC";
  return (
    mascotSvg(primary, accent) +
    `<span class="guardi-status-banner__text guardi-status-banner__text--full">${ui.copy.bannerFull}</span>` +
    `<span class="guardi-status-banner__text guardi-status-banner__text--compact">${ui.copy.bannerCompact}</span>`
  );
}

function applyBannerMode(modeOrManaged) {
  currentMode = parseUiMode(typeof modeOrManaged === "string" ? { uiMode: modeOrManaged } : modeOrManaged);
  const ui = getModeUi(currentMode);
  applyModeToDocument(document, currentMode);

  if (!bannerEl) return;
  bannerEl.innerHTML = renderBannerContent(ui);
  scheduleLayout();
}

export function mountStatusBanner(modeOrManaged) {
  if (modeOrManaged) currentMode = parseUiMode(typeof modeOrManaged === "string" ? { uiMode: modeOrManaged } : modeOrManaged);

  if (bannerEl || !document.documentElement) {
    if (modeOrManaged) applyBannerMode(modeOrManaged);
    return;
  }

  const ui = getModeUi(currentMode);
  applyModeToDocument(document, currentMode);

  bannerEl = document.createElement("div");
  bannerEl.id = BANNER_ID;
  bannerEl.className = "guardi-status-banner";
  bannerEl.setAttribute("role", "status");
  bannerEl.setAttribute("aria-live", "polite");
  bannerEl.innerHTML = renderBannerContent(ui);

  document.documentElement.appendChild(bannerEl);

  scheduleLayout();
  window.addEventListener("resize", scheduleLayout, { passive: true });
  window.addEventListener("scroll", scheduleLayout, { passive: true });

  resizeObserver = new ResizeObserver(scheduleLayout);
  const search = findSearchForm();
  if (search) resizeObserver.observe(search);
  resizeObserver.observe(document.documentElement);

  const headerObs = new MutationObserver(scheduleLayout);
  headerObs.observe(document.documentElement, { childList: true, subtree: true, attributes: true });
  headerObserver = headerObs;
}

export function updateStatusBannerMode(modeOrManaged) {
  applyBannerMode(modeOrManaged);
}

export function removeStatusBanner() {
  window.removeEventListener("resize", scheduleLayout);
  window.removeEventListener("scroll", scheduleLayout);
  resizeObserver?.disconnect();
  resizeObserver = null;
  headerObserver?.disconnect();
  headerObserver = null;
  bannerEl?.remove();
  bannerEl = null;
  if (scheduled) {
    cancelAnimationFrame(scheduled);
    scheduled = 0;
  }
}
