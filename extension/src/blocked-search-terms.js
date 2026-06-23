/** Blocked search terms — adult / sexual content (EN + FR). */

const PHRASES = [
  "child porn",
  "pornhub",
  "xvideos",
  "xhamster",
  "youporn",
  "onlyfans",
  "strip club",
  "strip tease",
  "cam girl",
  "camgirl",
  "webcam sex",
  "sex tape",
  "nude photo",
  "nude pic",
  "naked photo",
  "film x",
  "photo nu",
  "photos nues",
  "site porno",
  "video x",
  "contenu adulte",
];

const WORDS = [
  "porn",
  "porno",
  "pornography",
  "xxx",
  "nsfw",
  "hentai",
  "milf",
  "bdsm",
  "fetish",
  "fetishism",
  "orgy",
  "orgie",
  "brothel",
  "prostitute",
  "prostitution",
  "escort",
  "nude",
  "nudes",
  "nudity",
  "naked",
  "nudez",
  "nu",
  "nue",
  "nus",
  "nues",
  "topless",
  "bottomless",
  "sex",
  "sexe",
  "sexual",
  "sexuelle",
  "sexuel",
  "sexting",
  "sexy",
  "erotic",
  "erotique",
  "erotica",
  "xxx",
  "boobs",
  "tits",
  "nipple",
  "nipples",
  "penis",
  "vagina",
  "vulva",
  "clitoris",
  "testicle",
  "testicles",
  "dick",
  "cock",
  "pussy",
  "anal",
  "blowjob",
  "handjob",
  "masturbat",
  "masturbation",
  "orgasm",
  "orgasme",
  "ejacul",
  "cumshot",
  "deepthroat",
  "threesome",
  "gangbang",
  "swinger",
  "swingers",
  "playboy",
  "playmate",
  "lingerie",
  "stripper",
  "striptease",
  "shemale",
  "tranny",
  "sodomie",
  "sodomy",
  "bestiality",
  "zoophil",
  "pedophil",
  "paedophil",
  "incest",
  "inceste",
  "rape",
  "viol",
  "snuff",
  "hardcore",
  "softcore",
  "camsex",
  "camsex",
  "adult",
  "adulte",
  "adultes",
  "mature",
  "hookup",
  "hookups",
  "tinder nude",
  "rule34",
  "rule 34",
  "fap",
  "fapping",
  "slut",
  "whore",
  "salope",
  "pute",
  "bitch",
  "salop",
];

function normalize(text) {
  return (text || "")
    .normalize("NFD")
    .replace(/\p{M}/gu, "")
    .toLowerCase()
    .replace(/[^a-z0-9\s]+/g, " ")
    .replace(/\s+/g, " ")
    .trim();
}

function escapeRegex(s) {
  return s.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

const PHRASE_PATTERNS = PHRASES.map((p) => ({
  label: p,
  re: new RegExp(escapeRegex(normalize(p)).replace(/\s+/g, "\\s+"), "i"),
}));

const WORD_PATTERNS = WORDS.map((w) => ({
  label: w,
  re: new RegExp(`\\b${escapeRegex(normalize(w))}\\b`, "i"),
}));

/** @returns {{ label: string, kind: string } | null} */
export function matchBlockedSearch(rawQuery) {
  const query = normalize(rawQuery);
  if (!query) return null;

  for (const { label, re } of PHRASE_PATTERNS) {
    if (re.test(query)) return { label, kind: "phrase" };
  }
  for (const { label, re } of WORD_PATTERNS) {
    if (re.test(query)) return { label, kind: "word" };
  }
  return null;
}

export function extractSearchQuery(url) {
  try {
    const u = new URL(url);
    const host = u.hostname.replace(/^www\./, "");
    let raw = "";

    if (host.includes("google.")) {
      raw = u.searchParams.get("q") ?? u.searchParams.get("as_q") ?? "";
    } else if (host === "bing.com" || host.endsWith(".bing.com")) {
      raw = u.searchParams.get("q") ?? "";
    } else if (host === "duckduckgo.com") {
      raw = u.searchParams.get("q") ?? "";
    } else if (host === "search.yahoo.com") {
      raw = u.searchParams.get("p") ?? "";
    }

    if (!raw) return "";

    try {
      raw = decodeURIComponent(raw.replace(/\+/g, " "));
    } catch {
      raw = raw.replace(/\+/g, " ");
    }

    return raw.trim();
  } catch {
    // ignore
  }
  return "";
}
