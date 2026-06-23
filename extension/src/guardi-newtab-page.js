import { getModeUi, parseUiMode, personalizeGreetings } from "./mode-ui.js";
import { matchBlockedSearch } from "./blocked-search-terms.js";

const api = typeof browser !== "undefined" ? browser : chrome;
const MSG_BLOCKED_SEARCH = "guardi:blocked-search";
const STATE_LOCAL_KEY = "guardiShieldState";

let greetings = getModeUi("sub").copy.greetings;
let idx = 0;
let rotateTimer = 0;

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
  greetings = personalizeGreetings(
    ui.copy.greetings,
    managed?.displayName,
    ui.copy.nameFallback || "sweetie"
  );
  const bubble = document.getElementById("guardi-greeting");
  startGreetingRotation(bubble);
}

function goSafeSearch(query) {
  const url = `https://www.google.com/search?q=${encodeURIComponent(query)}&safe=active`;
  window.location.assign(url);
}

function goBlockedPage(label) {
  const params = new URLSearchParams();
  if (label) params.set("r", label.slice(0, 40));
  const qs = params.toString();
  window.location.assign(api.runtime.getURL(`blocked-search.html${qs ? `?${qs}` : ""}`));
}

const searchForm = document.getElementById("guardi-search");
const searchInput = document.getElementById("guardi-search-input");

if (searchForm && searchInput) {
  searchForm.addEventListener("submit", (event) => {
    event.preventDefault();
    const query = searchInput.value.trim();
    if (!query) return;

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

applyModeFromManaged({});
api.storage.local.get(STATE_LOCAL_KEY).then((data) => {
  if (data[STATE_LOCAL_KEY]?.managed) applyModeFromManaged(data[STATE_LOCAL_KEY].managed);
});
api.storage.onChanged.addListener((changes, area) => {
  if (area === "local" && changes[STATE_LOCAL_KEY]) {
    applyModeFromManaged(changes[STATE_LOCAL_KEY].newValue?.managed);
  }
});
