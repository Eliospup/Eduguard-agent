// Content script: on-device page-text scoring for category web filtering.
//
// The curated domain lists and the family DNS resolver cover known sites; this guard
// covers the exotic long tail by scoring the page's own words. The agent pushes weighted
// vocabularies per enabled category via shield-state (managed.blockedCategoryContent =
// { adult: { strong: [...], weak: [...] }, ... }).
//
// Scoring is deliberately conservative to avoid blocking innocent pages:
//  - terms are matched on word boundaries (no "analyse" ≠ "anal" false hits);
//  - a DISTINCT strong term in the head zone (title/meta/h1-h2) scores 3, body-only 2;
//  - weak terms add 1 each, capped at 3 points total;
//  - a page is only blocked at score >= 7 — i.e. several distinct category-defining
//    words, not a stray mention in an article.
// Search engines and reference sites are skipped entirely (their result pages quote
// matching words without being the content itself; SafeSearch already sanitises them).

const GET_STATE = "guardi:get-state";
const PUSH_STATE = "guardi:shield-state";
const BLOCK_THRESHOLD = 7;
const WEAK_CAP = 3;
const MAX_SCANS = 4;
const RESCAN_DELAY_MS = 2500;

// Same convention as youtube-soft-limit.js: the `chrome` namespace with callback-style
// messaging works in both Chromium and Firefox content scripts (the `browser` namespace
// rejects trailing callbacks, so it is deliberately NOT used here).
const api = chrome;

const SKIP_HOST_PATTERNS = [
  /(^|\.)google\.[a-z.]{2,6}$/,
  /(^|\.)bing\.com$/,
  /(^|\.)duckduckgo\.com$/,
  /(^|\.)qwant\.com$/,
  /(^|\.)ecosia\.org$/,
  /(^|\.)startpage\.com$/,
  /(^|\.)search\.brave\.com$/,
  /(^|\.)wikipedia\.org$/,
  /(^|\.)wiktionary\.org$/,
  /(^|\.)youtube\.com$/, // has its own guards
  /(^|\.)localhost$/,
];

let categories = null; // { key: { strong: RegExp[], weak: RegExp[] } }
let scansDone = 0;
let blocked = false;

function hostSkipped() {
  const host = location.hostname.replace(/^www\./i, "").toLowerCase();
  if (!host || host === "127.0.0.1") return true;
  return SKIP_HOST_PATTERNS.some((re) => re.test(host));
}

function escapeRegex(term) {
  return term.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

/** Word-boundary matcher that also works for accented terms (JS \b is ASCII-only). */
function termToRegex(term) {
  try {
    return new RegExp(`(?<![\\p{L}\\p{N}])${escapeRegex(term)}(?![\\p{L}\\p{N}])`, "iu");
  } catch {
    return new RegExp(`\\b${escapeRegex(term)}\\b`, "i");
  }
}

function compileCategories(managed) {
  const raw = managed && managed.blockedCategoryContent;
  if (!raw || typeof raw !== "object") return null;

  const compiled = {};
  for (const [key, value] of Object.entries(raw)) {
    if (!value || typeof value !== "object") continue;
    const strong = Array.isArray(value.strong) ? value.strong : [];
    const weak = Array.isArray(value.weak) ? value.weak : [];
    if (strong.length === 0) continue;
    compiled[key] = {
      strong: strong.filter((t) => typeof t === "string" && t.length >= 3).map(termToRegex),
      weak: weak.filter((t) => typeof t === "string" && t.length >= 3).map(termToRegex),
    };
  }

  return Object.keys(compiled).length > 0 ? compiled : null;
}

function headText() {
  const parts = [document.title || ""];
  const meta = document.querySelector('meta[name="description"]');
  if (meta?.content) parts.push(meta.content);
  const kw = document.querySelector('meta[name="keywords"]');
  if (kw?.content) parts.push(kw.content);
  for (const h of document.querySelectorAll("h1, h2")) {
    parts.push(h.textContent || "");
    if (parts.length > 24) break;
  }
  return parts.join(" \n ");
}

function bodySample() {
  try {
    return (document.body?.innerText || "").slice(0, 30000);
  } catch {
    return "";
  }
}

function scoreCategory(cat, head, body) {
  let score = 0;
  for (const re of cat.strong) {
    if (re.test(head)) score += 3;
    else if (re.test(body)) score += 2;
  }

  let weakPoints = 0;
  for (const re of cat.weak) {
    if (weakPoints >= WEAK_CAP) break;
    if (re.test(head) || re.test(body)) weakPoints += 1;
  }

  return score + weakPoints;
}

function scan() {
  if (blocked || !categories || scansDone >= MAX_SCANS) return;
  scansDone += 1;

  const head = headText();
  const body = bodySample();
  if (!head && !body) return;

  for (const [key, cat] of Object.entries(categories)) {
    if (scoreCategory(cat, head, body) >= BLOCK_THRESHOLD) {
      blocked = true;
      const target = api.runtime.getURL(`category-blocked.html?category=${encodeURIComponent(key)}`);
      window.location.replace(target);
      return;
    }
  }
}

function onState(managed) {
  const next = compileCategories(managed);
  const hadNone = !categories;
  categories = next;
  if (next && hadNone) {
    scansDone = 0;
    scan();
    setTimeout(scan, RESCAN_DELAY_MS);
  }
}

function requestState(retry) {
  if (!api?.runtime?.sendMessage) return;
  try {
    api.runtime.sendMessage({ type: GET_STATE }, (resp) => {
      // Swallow "receiving end does not exist" while the worker spins up.
      if (api.runtime.lastError || !resp || !resp.managed) {
        if (retry > 0) setTimeout(() => requestState(retry - 1), 1500);
        return;
      }
      onState(resp.managed);
    });
  } catch {
    if (retry > 0) setTimeout(() => requestState(retry - 1), 1500);
  }
}

if (window.top === window && !hostSkipped()) {
  requestState(2);

  api.runtime.onMessage?.addListener?.((msg) => {
    if (msg && msg.type === PUSH_STATE && msg.managed) onState(msg.managed);
  });

  // SPA navigations and late-rendered content: rescan when the title changes
  // (budgeted by MAX_SCANS so heavy pages are never scanned in a loop).
  const title = document.querySelector("title");
  if (title) {
    new MutationObserver(() => {
      scansDone = Math.min(scansDone, MAX_SCANS - 1);
      scan();
    }).observe(title, { childList: true });
  }
}
