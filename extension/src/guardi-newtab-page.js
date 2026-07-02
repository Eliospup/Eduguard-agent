import { getModeUi, parseUiMode, personalizeGreetings, applyModeToDocument } from "./mode-ui.js";
import { matchBlockedSearch } from "./blocked-search-terms.js";
import { createContentStateWatcher } from "./active-state.js";

const api = typeof browser !== "undefined" ? browser : chrome;
const MSG_BLOCKED_SEARCH = "guardi:blocked-search";

let greetings = getModeUi("sub").copy.greetings;
let idx = 0;
let rotateTimer = 0;
// This page can load via the native newtab override before supervision state is
// known, so default to neutral (no Guardi chrome) until proven active.
let supervisionActive = false;

function startGreetingRotation(bubble) {
  if (!bubble || !greetings.length) return;
  clearInterval(rotateTimer);
  idx = 0;
  bubble.textContent = greetings[0];
  rotateTimer = setInterval(() => {
    idx = (idx + 1) % greetings.length;
    bubble.classList.add("guardi-speech-bubble__text--fade");
    setTimeout(() => {
      bubble.textContent = greetings[idx];
      bubble.classList.remove("guardi-speech-bubble__text--fade");
    }, 220);
  }, 5200);
}

function applyModeFromManaged(managed) {
  const ui = getModeUi(managed);
  // This page now owns mode application end-to-end (it used to share the document with
  // guardi-shell-page.js, and the two independently-timed watchers applying mode at
  // slightly different moments is what caused the old/new mode to flash on a mode change).
  applyModeToDocument(document, managed);

  greetings = personalizeGreetings(
    ui.copy.greetings,
    managed?.displayName,
    ui.copy.nameFallback || "sweetie"
  );
  const bubble = document.getElementById("guardi-greeting");
  startGreetingRotation(bubble);

  document.querySelectorAll("[data-guardi='title']").forEach((el) => {
    el.textContent = ui.copy.pageTitle;
  });
  document.querySelectorAll("[data-guardi='subtitle']").forEach((el) => {
    el.textContent = ui.copy.pageSubtitle;
  });
  const chips = document.getElementById("guardi-chips");
  if (chips && ui.copy.chips) {
    chips.innerHTML = ui.copy.chips
      .map(
        ([icon, label]) =>
          `<span class="guardi-chip"><span aria-hidden="true">${icon}</span> ${label}</span>`
      )
      .join("");
  }
  if (searchInput) searchInput.placeholder = ui.copy.searchPlaceholder;
  const searchBtn = document.querySelector("#guardi-search button[type='submit']");
  if (searchBtn && ui.copy.searchButton) searchBtn.textContent = ui.copy.searchButton;
}

function goSafeSearch(query) {
  const url = `https://www.google.com/search?q=${encodeURIComponent(query)}&safe=active`;
  window.location.assign(url);
}

/** No supervision active — search like a normal, unfiltered browser. */
function goPlainSearch(query) {
  if (api.search?.search) {
    try {
      api.search.search({ query, disposition: "CURRENT_TAB" });
      return;
    } catch {
      // fall through to the URL-based search below
    }
  }
  window.location.assign(`https://www.google.com/search?q=${encodeURIComponent(query)}`);
}

function goBlockedPage(label) {
  const params = new URLSearchParams();
  if (label) params.set("r", label.slice(0, 40));
  const qs = params.toString();
  window.location.assign(api.runtime.getURL(`blocked-search.html${qs ? `?${qs}` : ""}`));
}

const searchForm = document.getElementById("guardi-search");
const searchInput = document.getElementById("guardi-search-input");
// Firefox's own new-tab placeholder, so the neutral page reads as the real thing.
const NEUTRAL_PLACEHOLDER = "Search with Google or enter address";

function applyActiveState(isActive, managed) {
  // Enforcement (blocked-search) stays tied to supervision; the styling is a separate,
  // user-hideable layer. styledNewTab === false → render the neutral, unbranded page even
  // while supervised, so search is still checked but no Guardi chrome shows.
  supervisionActive = isActive;
  const styled = isActive && managed?.styledNewTab !== false;
  document.body.classList.toggle("guardi-newtab--neutral", !styled);
  if (styled) applyModeFromManaged(managed);
  else if (searchInput) searchInput.placeholder = NEUTRAL_PLACEHOLDER;
}

if (searchForm && searchInput) {
  searchForm.addEventListener("submit", (event) => {
    event.preventDefault();
    const query = searchInput.value.trim();
    if (!query) return;

    if (!supervisionActive) {
      goPlainSearch(query);
      return;
    }

    const blocked = matchBlockedSearch(query);
    if (blocked) {
      try {
        api.runtime.sendMessage({
          type: MSG_BLOCKED_SEARCH,
          query,
          match: blocked.label,
        });
      } catch {
        // ignore
      }
      goBlockedPage(blocked.label);
      return;
    }

    goSafeSearch(query);
  });
}

document.body.classList.add("guardi-newtab--neutral");
createContentStateWatcher(api, { onChange: applyActiveState }).start();
