/** Guardi extension UI — copy + palette per agent mode (mirrors C# ModeTheme / ModeCopySet tone). */

export const MODE_SLUGS = {
  TRUSTED: "trusted_sub",
  SUB: "sub",
  RESTRICTED: "restricted_sub",
};

const MODE_CLASS_PREFIX = "guardi-mode-";

function hexToRgb(hex) {
  const h = hex.replace("#", "");
  return [
    parseInt(h.slice(0, 2), 16),
    parseInt(h.slice(2, 4), 16),
    parseInt(h.slice(4, 6), 16),
  ];
}

/** @typedef {typeof MODES.trusted_sub} ModeUiPack */

const MODES = {
  trusted_sub: {
    slug: MODE_SLUGS.TRUSTED,
    cssVars: {
      "--guardi-sky": "#ecfdf5",
      "--guardi-sky-deep": "#a7f3d0",
      "--guardi-pink": "#fef3c7",
      "--guardi-pink-hot": "#fcd34d",
      "--guardi-blue": "#34d399",
      "--guardi-blue-deep": "#047857",
      "--guardi-lilac": "#d1fae5",
      "--guardi-text": "#14532d",
      "--guardi-text-soft": "#059669",
      "--guardi-shadow": "rgba(5, 150, 105, 0.28)",
      "--guardi-shield-primary": "#059669",
      "--guardi-shield-accent": "#6ee7b7",
      "--guardi-banner-bg": "linear-gradient(135deg, #ecfdf5 0%, #fef3c7 55%, #d1fae5 100%)",
      "--guardi-banner-border": "#a7f3d0",
      "--guardi-banner-text": "#047857",
    },
    browserTheme: {
      colors: {
        frame: hexToRgb("#f0fdf4"),
        frame_inactive: hexToRgb("#ecfdf5"),
        toolbar: hexToRgb("#a7f3d0"),
        toolbar_field: hexToRgb("#ffffff"),
        toolbar_field_text: hexToRgb("#14532d"),
        toolbar_field_border: hexToRgb("#fcd34d"),
        toolbar_text: hexToRgb("#047857"),
        tab_text: hexToRgb("#059669"),
        tab_background_text: hexToRgb("#34d399"),
        bookmark_text: hexToRgb("#047857"),
        button_background_hover: hexToRgb("#fef3c7"),
        ntp_background: hexToRgb("#ecfdf5"),
        ntp_text: hexToRgb("#14532d"),
        popup: hexToRgb("#f0fdf4"),
        popup_text: hexToRgb("#14532d"),
        popup_border: hexToRgb("#a7f3d0"),
        icons: {
          toolbar: hexToRgb("#059669"),
          tab: hexToRgb("#34d399"),
          bookmark: hexToRgb("#d97706"),
        },
      },
      properties: { color_scheme: "light", ntp_alignment: "bottom" },
    },
    copy: {
      actionTitle: "Guardi — your study browser is protected",
      bannerFull: "Focus time! Guardi keeps your study browser safe and tidy.",
      bannerCompact: "Study mode on!",
      newTabTitle: "Guardi — your study browser",
      pageTitle: "Your study browser",
      pageSubtitle: "Guardi keeps distractions away so you can learn and do your best.",
      searchPlaceholder: "Search for homework help or safe topics…",
      searchButton: "Go!",
      nameFallback: "there",
      chips: [
        ["📚", "Homework first"],
        ["🔍", "Safe searches only"],
        ["✏️", "Focus mode on"],
      ],
      greetings: [
        "Hi {0}! Ready to learn? Guardi's here to keep your study computer safe.",
        "Good focus today — Guardi is watching your tabs for you.",
        "Study time! Guardi tucked the distracting stuff away.",
        "You're on your study browser — Guardi keeps it neat and safe.",
        "Let's learn something cool! Guardi blocked the grown-up stuff already.",
      ],
      popupTitle: "Hi {0}!",
      popupSubtitle: "Guardi is protecting your study browser.",
      popupBody:
        "Distractions and yucky pictures stay hidden so you can focus on homework and approved fun. Stay on nice sites and let Guardi handle the rest.",
      popupBadge: "Study shield on!",
      inactiveNote: "Guardi is resting — study protection resumes when supervision is on.",
      blockedTitle: "That search isn't for study time",
      blockedSubtitle: "Your Dom wants you focused on safe, school-friendly topics.",
      blockedBody:
        "Guardi blocked a grown-up search you are not allowed to make on your study computer. Pick something else — homework, hobbies, or approved fun.",
      blockedBodyReason:
        'Guardi blocked a search about "{0}" — that is not allowed during study time. Your Dom wants you on safe topics only. Guardi told your Dom, too.',
      blockedFooter: "This counts as breaking the study rules. Choose a safer search.",
      blockedButton: "Back to my study browser",
      blockedPageTitle: "Guardi — blocked search",
      youtubeLimitTitle: "YouTube time's up for today",
      youtubeLimitSubtitle: "You've used today's YouTube allowance on your study computer.",
      youtubeLimitBody:
        "Guardi replaced this YouTube page so you can keep using your browser. Finish homework first, or ask your Dom if you need a little more watch time.",
      youtubeLimitFooter: "This tab stays here until your daily YouTube allowance resets.",
      youtubeLimitButton: "Back to my study browser",
      youtubeLimitPageTitle: "Guardi — YouTube time's up",
      youtubeStudyTitle: "YouTube waits during study time",
      youtubeStudySubtitle: "Focus first — videos can come later.",
      youtubeStudyBody:
        "It's study time right now. Guardi tucked YouTube away so you can concentrate. Your browser stays open — pick something school-friendly instead.",
      youtubeStudyFooter: "Study rules stay on until your focus window ends.",
    },
  },

  sub: {
    slug: MODE_SLUGS.SUB,
    cssVars: {
      "--guardi-sky": "#e0f2fe",
      "--guardi-sky-deep": "#bae6fd",
      "--guardi-pink": "#e0f2fe",
      "--guardi-pink-hot": "#38bdf8",
      "--guardi-blue": "#38bdf8",
      "--guardi-blue-deep": "#0284c7",
      "--guardi-lilac": "#bae6fd",
      "--guardi-text": "#0c4a6e",
      "--guardi-text-soft": "#0369a1",
      "--guardi-shadow": "rgba(14, 165, 233, 0.28)",
      "--guardi-shield-primary": "#0ea5e9",
      "--guardi-shield-accent": "#7dd3fc",
      "--guardi-banner-bg": "linear-gradient(135deg, #e0f2fe 0%, #bae6fd 55%, #f0f9ff 100%)",
      "--guardi-banner-border": "#7dd3fc",
      "--guardi-banner-text": "#0369a1",
    },
    browserTheme: {
      colors: {
        frame: hexToRgb("#e0f2fe"),
        frame_inactive: hexToRgb("#f1f5f9"),
        toolbar: hexToRgb("#bae6fd"),
        toolbar_field: hexToRgb("#ffffff"),
        toolbar_field_text: hexToRgb("#0c4a6e"),
        toolbar_field_border: hexToRgb("#7dd3fc"),
        toolbar_text: hexToRgb("#0369a1"),
        tab_text: hexToRgb("#0284c7"),
        tab_background_text: hexToRgb("#0ea5e9"),
        bookmark_text: hexToRgb("#0369a1"),
        button_background_hover: hexToRgb("#bae6fd"),
        ntp_background: hexToRgb("#e0f2fe"),
        ntp_text: hexToRgb("#0c4a6e"),
        popup: hexToRgb("#e0f2fe"),
        popup_text: hexToRgb("#0c4a6e"),
        popup_border: hexToRgb("#7dd3fc"),
        icons: {
          toolbar: hexToRgb("#0ea5e9"),
          tab: hexToRgb("#38bdf8"),
          bookmark: hexToRgb("#0284c7"),
        },
      },
      properties: { color_scheme: "light", ntp_alignment: "bottom" },
    },
    copy: {
      actionTitle: "Guardi is watching over you, sweetie!",
      bannerFull: "Shhh… Guardi's hiding the yucky pics for you, sweetie!",
      bannerCompact: "Guardi's watching!",
      newTabTitle: "Guardi — your cozy safe browser, sweetie",
      pageTitle: "Your cozy safe browser",
      pageSubtitle: "Guardi hides the yucky stuff — you just browse like a good little one.",
      searchPlaceholder: "Where shall we go, sweetie?",
      searchButton: "Go!",
      nameFallback: "sweetie",
      chips: [
        ["🛡️", "Guardi's watching!"],
        ["🌸", "Icky pics stay hidden"],
        ["🧸", "Supervised & cozy"],
      ],
      greetings: [
        "Hi {0}! Guardi's got your back today.",
        "Good little browser — Guardi's watching every page for you!",
        "No yucky pictures on my watch, cutie pie!",
        "You're so safe here. Guardi hid the icky stuff already!",
        "Shhh… Guardi's on duty. Just browse like a good little one.",
      ],
      popupTitle: "Hi {0}!",
      popupSubtitle: "Guardi's cuddling your browser right now.",
      popupBody:
        "Yucky pictures get tucked away before you even see them. Stay on nice sites and let Guardi do the grown-up worrying for you.",
      popupBadge: "Shield snuggled on!",
      inactiveNote: "Guardi is resting — protection resumes when supervision is on.",
      blockedTitle: "Nope, little one!",
      blockedSubtitle: "That search is a big no-no.",
      blockedBody:
        "Guardi caught a grown-up search you are not allowed to make. Your Dom said those words stay off-limits — so Guardi blocked it before anything yucky could show up. Nice try, sweetie, but Guardi is smarter than that!",
      blockedBodyReason:
        'Guardi blocked a search about "{0}" — that is grown-up stuff and it is not allowed on this computer. Your Dom wants you browsing safe and cozy things only. Guardi told your Dom, too!',
      blockedFooter: "This counts as breaking the safety rules. Be a good little one and pick something else.",
      blockedButton: "Take me somewhere safe",
      blockedPageTitle: "Guardi — nope, little one!",
      youtubeLimitTitle: "YouTube time's up!",
      youtubeLimitSubtitle: "You've used all your YouTube time for today, sweetie.",
      youtubeLimitBody:
        "Guardi replaced this YouTube page so your browser can stay open. Ask your Dom nicely for more — or come back tomorrow when Guardi resets your watch clock.",
      youtubeLimitFooter: "This tab stays on Guardi's page until tomorrow's allowance.",
      youtubeLimitButton: "Take me somewhere safe",
      youtubeLimitPageTitle: "Guardi — YouTube time's up!",
      youtubeStudyTitle: "No YouTube during study time!",
      youtubeStudySubtitle: "Focus first, little one — videos wait.",
      youtubeStudyBody:
        "It's study time right now. Guardi tucked YouTube away so you can concentrate. Your browser stays open — pick something safe instead.",
      youtubeStudyFooter: "Study rules stay locked until your focus window ends.",
    },
  },

  restricted_sub: {
    slug: MODE_SLUGS.RESTRICTED,
    cssVars: {
      "--guardi-sky": "#eff6ff",
      "--guardi-sky-deep": "#bfdbfe",
      "--guardi-pink": "#fef3c7",
      "--guardi-pink-hot": "#f59e0b",
      "--guardi-blue": "#3b82f6",
      "--guardi-blue-deep": "#1e3a8a",
      "--guardi-lilac": "#dbeafe",
      "--guardi-text": "#1e3a8a",
      "--guardi-text-soft": "#1d4ed8",
      "--guardi-shadow": "rgba(29, 78, 216, 0.32)",
      "--guardi-shield-primary": "#1d4ed8",
      "--guardi-shield-accent": "#f59e0b",
      "--guardi-banner-bg": "linear-gradient(135deg, #eff6ff 0%, #fef3c7 45%, #dbeafe 100%)",
      "--guardi-banner-border": "#f59e0b",
      "--guardi-banner-text": "#1e3a8a",
    },
    browserTheme: {
      colors: {
        frame: hexToRgb("#eff6ff"),
        frame_inactive: hexToRgb("#dbeafe"),
        toolbar: hexToRgb("#bfdbfe"),
        toolbar_field: hexToRgb("#ffffff"),
        toolbar_field_text: hexToRgb("#1e3a8a"),
        toolbar_field_border: hexToRgb("#f59e0b"),
        toolbar_text: hexToRgb("#1d4ed8"),
        tab_text: hexToRgb("#1e40af"),
        tab_background_text: hexToRgb("#3b82f6"),
        bookmark_text: hexToRgb("#1d4ed8"),
        button_background_hover: hexToRgb("#fef3c7"),
        ntp_background: hexToRgb("#eff6ff"),
        ntp_text: hexToRgb("#1e3a8a"),
        popup: hexToRgb("#eff6ff"),
        popup_text: hexToRgb("#1e3a8a"),
        popup_border: hexToRgb("#f59e0b"),
        icons: {
          toolbar: hexToRgb("#1d4ed8"),
          tab: hexToRgb("#3b82f6"),
          bookmark: hexToRgb("#f59e0b"),
        },
      },
      properties: { color_scheme: "light", ntp_alignment: "bottom" },
    },
    copy: {
      actionTitle: "🔒 Guardi locked your browser tight, sweetie!",
      bannerFull: "🔒 Locked tight! Guardi's watching every click, little one!",
      bannerCompact: "🔒 All locked!",
      newTabTitle: "Guardi — maximum security browser",
      pageTitle: "🔒 Your locked-safe browser",
      pageSubtitle:
        "Sweetie, every door is bolted shut. Guardi watches everything and keeps the bad stuff sealed away for your Dom.",
      searchPlaceholder: "Safe searches only — Guardi is watching!",
      searchButton: "Go safely!",
      nameFallback: "sweetie",
      chips: [
        ["🔒", "Everything locked"],
        ["🛡️", "Guardi is watching"],
        ["🔐", "No sneaking past"],
      ],
      greetings: [
        "Hi {0}! Guardi locked everything tight for you — stay in the safe zone!",
        "🔒 Maximum security! Guardi sealed every door shut for your Dom.",
        "No sneaking, little one — Guardi is watching every single click!",
        "All locked up cozy and safe. Guardi won't let anything yucky through!",
        "Sweetie, Guardi bolted the browser shut — browse like a good little one!",
      ],
      popupTitle: "🔒 Hi {0}!",
      popupSubtitle: "Guardi bolted your browser shut — maximum security!",
      popupBody:
        "Every rule is locked on, sweetie. Yucky pictures stay sealed away and forbidden searches get blocked before you see them. Guardi is watching everything for your Dom.",
      popupBadge: "🔒 Shield locked on!",
      inactiveNote: "Guardi is resting — all locks resume when supervision is on.",
      blockedTitle: "🔒 Nope — locked search!",
      blockedSubtitle: "That word is forbidden. Guardi sealed it shut!",
      blockedBody:
        "Guardi caught a forbidden grown-up search and locked it before anything could load. Your Dom sealed those words off-limits — nice try, sweetie, but Guardi is smarter and told your Dom!",
      blockedBodyReason:
        'Guardi locked a search about "{0}" — forbidden stuff! Your Dom sealed that word shut. Guardi blocked it and told your Dom, little one!',
      blockedFooter: "🔒 That breaks the safety rules. Pick something locked-safe instead.",
      blockedButton: "🔒 Back to my safe browser",
      blockedPageTitle: "Guardi — locked search!",
      youtubeLimitTitle: "🔒 YouTube time's up!",
      youtubeLimitSubtitle: "You've used all your YouTube allowance for today, little one.",
      youtubeLimitBody:
        "Guardi replaced this YouTube page so your browser stays open. Ask your Dom nicely if you need more — or wait until tomorrow when Guardi resets your clock.",
      youtubeLimitFooter: "🔒 This tab stays sealed on Guardi's page until your allowance resets.",
      youtubeLimitButton: "🔒 Back to my safe browser",
      youtubeLimitPageTitle: "Guardi — YouTube locked!",
      youtubeStudyTitle: "🔒 No YouTube during study lock!",
      youtubeStudySubtitle: "Study first — videos are sealed away.",
      youtubeStudyBody:
        "Study lock is active. Guardi bolted YouTube shut so you can focus. Your browser stays open — pick something locked-safe instead.",
      youtubeStudyFooter: "🔒 Study rules stay on until your focus window ends.",
    },
  },
};

export function parseUiMode(managed) {
  const raw = managed?.uiMode;
  if (typeof raw === "string" && MODES[raw]) return raw;
  return MODE_SLUGS.SUB;
}

export function getModeUi(modeOrManaged) {
  const slug =
    typeof modeOrManaged === "string"
      ? parseUiMode({ uiMode: modeOrManaged })
      : parseUiMode(modeOrManaged);
  return MODES[slug] || MODES.sub;
}

export function formatCopy(template, value) {
  return template.replace("{0}", value ?? "");
}

export function personalizeCopy(template, displayName, fallback = "sweetie") {
  const name = (displayName || "").trim() || fallback;
  return formatCopy(template, name);
}

export function personalizeGreetings(greetings, displayName, fallback = "sweetie") {
  return greetings.map((line) => personalizeCopy(line, displayName, fallback));
}

const MODE_CLASSES = Object.values(MODES).map((m) => `${MODE_CLASS_PREFIX}${m.slug}`);

export function applyModeToDocument(doc, modeOrManaged) {
  const ui = getModeUi(modeOrManaged);
  const root = doc?.documentElement;
  if (!root) return ui;

  root.classList.remove(...MODE_CLASSES);
  root.classList.add(`${MODE_CLASS_PREFIX}${ui.slug}`);

  for (const [key, value] of Object.entries(ui.cssVars)) {
    root.style.setProperty(key, value);
  }

  if (doc.body) {
    const isBlocked = doc.body.classList.contains("guardi-blocked-page");
    const isPopup = doc.body.classList.contains("guardi-popup");
    if (isBlocked) doc.title = ui.copy.blockedPageTitle;
    else if (doc.body.classList.contains("guardi-youtube-limit-page")) doc.title = ui.copy.youtubeLimitPageTitle;
    else if (isPopup) doc.title = "Guardi";
    else if (doc.body.classList.contains("guardi-newtab")) doc.title = ui.copy.newTabTitle;
  }

  return ui;
}

export function clearModeFromDocument(doc) {
  const root = doc?.documentElement;
  if (!root) return;
  root.classList.remove(...MODE_CLASSES);
  const keys = new Set();
  for (const mode of Object.values(MODES)) {
    for (const key of Object.keys(mode.cssVars)) keys.add(key);
  }
  for (const key of keys) root.style.removeProperty(key);
}

export function getBrowserTheme(modeOrManaged) {
  return getModeUi(modeOrManaged).browserTheme;
}

export function mascotSvg(primary, accent) {
  const fill = primary || "#0EA5E9";
  const inner = accent || "#7DD3FC";
  return (
    '<svg class="guardi-status-banner__mascot" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 120 136" aria-hidden="true">' +
    `<path fill="${fill}" d="M60 8 104 30v48q0 40-44 54Q16 118 16 78V30L60 8Z"/>` +
    `<path fill="${inner}" opacity=".35" d="M60 14l36 18v44q0 32-36 44Q24 108 24 76V32L60 14Z"/>` +
    '<circle cx="48" cy="61" r="8" fill="#fff"/><circle cx="72" cy="61" r="8" fill="#fff"/>' +
    '<circle cx="49.5" cy="63" r="3.5" fill="#0C4A6E"/><circle cx="73.5" cy="63" r="3.5" fill="#0C4A6E"/>' +
    '<path stroke="#fff" stroke-width="3.5" stroke-linecap="round" d="M42 79q18 18 36 0"/>' +
    "</svg>"
  );
}

export function shieldOverlaySvg(primary, accent) {
  const fill = primary || "#0EA5E9";
  const inner = accent || "#7DD3FC";
  return (
    '<svg class="guardi-shield-icon" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 120 136" fill="none" aria-hidden="true">' +
    `<path fill="${fill}" d="M60 8 104 30v48q0 40-44 54Q16 118 16 78V30L60 8Z"/>` +
    `<path fill="${inner}" opacity=".35" d="M60 14l36 18v44q0 32-36 44Q24 108 24 76V32L60 14Z"/>` +
    '<circle cx="48" cy="61" r="8" fill="#fff"/><circle cx="72" cy="61" r="8" fill="#fff"/>' +
    '<circle cx="49.5" cy="63" r="3.5" fill="#0C4A6E"/><circle cx="73.5" cy="63" r="3.5" fill="#0C4A6E"/>' +
    '<path stroke="#fff" stroke-width="3.5" stroke-linecap="round" d="M42 79q18 18 36 0"/>' +
    "</svg>"
  );
}
