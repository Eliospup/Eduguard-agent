// Content script: youtube.com only.
// When the agent signals youtubeSoftLimit=true, pauses the active video and shows an
// in-page overlay. "Continue watching" POSTs /youtube-soft-ack, resumes playback, and
// hides the overlay. The rest of the PC stays fully usable while the overlay is up.

const AGENT_PORT = 38473;
const ACK_URL = `http://127.0.0.1:${AGENT_PORT}/youtube-soft-ack`;
const POLL_INTERVAL_MS = 5000;
const OVERLAY_ID = "guardi-yt-soft-overlay";

let overlayVisible = false;
let pollTimer = null;

function isWatchPage() {
  return location.pathname === "/watch";
}

function getVideoElement() {
  return (
    document.querySelector("video.html5-main-video") ||
    document.querySelector("video")
  );
}

function getPlayerContainer() {
  return (
    document.querySelector("#movie_player") ||
    document.querySelector(".html5-video-player") ||
    document.querySelector("#player-container-inner") ||
    document.querySelector("#player")
  );
}

function buildOverlay() {
  const existing = document.getElementById(OVERLAY_ID);
  if (existing) return existing;

  const overlay = document.createElement("div");
  overlay.id = OVERLAY_ID;
  overlay.style.cssText = [
    "position:absolute",
    "inset:0",
    "z-index:9999999",
    "display:flex",
    "align-items:center",
    "justify-content:center",
    "background:rgba(0,0,0,0.72)",
    "backdrop-filter:blur(6px)",
    "-webkit-backdrop-filter:blur(6px)",
    "font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',sans-serif",
  ].join(";");

  const card = document.createElement("div");
  card.style.cssText = [
    "background:#ffffff",
    "border-radius:18px",
    "padding:32px 40px",
    "max-width:420px",
    "width:90%",
    "text-align:center",
    "box-shadow:0 24px 64px rgba(0,0,0,0.45)",
    "box-sizing:border-box",
  ].join(";");

  const icon = document.createElement("div");
  icon.textContent = "⏰"; // ⏰
  icon.style.cssText = "font-size:48px;margin-bottom:14px;line-height:1";

  const title = document.createElement("h2");
  title.textContent = "YouTube time’s up!";
  title.style.cssText = [
    "margin:0 0 10px",
    "font-size:20px",
    "font-weight:700",
    "color:#1a1a2e",
    "line-height:1.2",
  ].join(";");

  const body = document.createElement("p");
  body.textContent =
    "You’ve reached your daily YouTube limit. Guardi trusts you to wrap up — " +
    "continuing will cost you a little trust.";
  body.style.cssText = [
    "margin:0 0 24px",
    "font-size:14px",
    "color:#555",
    "line-height:1.6",
  ].join(";");

  const btn = document.createElement("button");
  btn.textContent = "Continue watching";
  btn.style.cssText = [
    "background:#56b4d3",
    "color:#fff",
    "border:none",
    "border-radius:10px",
    "padding:12px 28px",
    "font-size:15px",
    "font-weight:600",
    "cursor:pointer",
    "transition:background 0.15s",
    "outline:none",
  ].join(";");
  btn.addEventListener("mouseenter", () => {
    btn.style.background = "#3a9ab8";
  });
  btn.addEventListener("mouseleave", () => {
    btn.style.background = "#56b4d3";
  });
  btn.addEventListener("click", ackAndContinue);

  card.append(icon, title, body, btn);
  overlay.appendChild(card);
  return overlay;
}

function showOverlay() {
  if (overlayVisible) return;
  if (!isWatchPage()) return;

  const video = getVideoElement();
  if (video && !video.paused) video.pause();

  const container = getPlayerContainer();
  if (!container) return;

  const pos = window.getComputedStyle(container).position;
  if (pos === "static") container.style.position = "relative";

  container.appendChild(buildOverlay());
  overlayVisible = true;
}

function hideOverlay() {
  const el = document.getElementById(OVERLAY_ID);
  if (el) el.remove();
  overlayVisible = false;
}

async function ackAndContinue() {
  hideOverlay();

  const video = getVideoElement();
  if (video && video.paused) video.play().catch(() => {});

  try {
    await fetch(ACK_URL, { method: "POST" });
  } catch {
    // Agent not running — still let the user continue
  }
}

function readBool(value) {
  if (typeof value === "boolean") return value;
  if (typeof value === "number") return value !== 0;
  if (typeof value === "string") {
    const s = value.trim().toLowerCase();
    return s === "true" || s === "1" || s === "yes" || s === "on";
  }
  return false;
}

function applyState(managed) {
  const softLimit = readBool(managed && managed.youtubeSoftLimit);
  if (softLimit && !overlayVisible) {
    showOverlay();
  } else if (!softLimit && overlayVisible) {
    hideOverlay();
  }
}

// Pushed updates from background SW
if (typeof chrome !== "undefined" && chrome.runtime && chrome.runtime.onMessage) {
  chrome.runtime.onMessage.addListener((msg) => {
    if (msg && msg.type === "guardi:shield-state") {
      applyState(msg.managed || {});
    }
  });
}

function poll() {
  if (typeof chrome === "undefined" || !chrome.runtime || !chrome.runtime.sendMessage)
    return;
  chrome.runtime.sendMessage({ type: "guardi:get-state" }, (resp) => {
    if (chrome.runtime.lastError) return;
    if (resp && resp.managed) applyState(resp.managed);
  });
}

function startPolling() {
  stopPolling();
  poll();
  pollTimer = setInterval(poll, POLL_INTERVAL_MS);
}

function stopPolling() {
  if (pollTimer !== null) {
    clearInterval(pollTimer);
    pollTimer = null;
  }
}

function onNavigation() {
  if (!isWatchPage()) {
    hideOverlay();
    stopPolling();
  } else {
    startPolling();
  }
}

if (isWatchPage()) {
  startPolling();
}

// YouTube is a SPA — intercept pushState for navigation events
const _origPushState = history.pushState.bind(history);
history.pushState = (...args) => {
  _origPushState(...args);
  setTimeout(onNavigation, 0);
};
window.addEventListener("popstate", () => setTimeout(onNavigation, 0));
