// Shared constants between the content script and the background worker.

export const MSG_CLASSIFY = "guardi:classify";
export const MSG_CLASSIFY_BLOB = "guardi:classify-blob";
export const MSG_RESULT = "guardi:result";
export const MSG_WARMUP = "guardi:warmup";
export const MSG_GET_STATE = "guardi:get-state";

export const DEFAULTS = Object.freeze({
  // Minimum rendered side (px) for an image to be blurred/classified.
  // Below this, blurring is pointless (icons, filter chips, sprites).
  minSize: 80,
  // Block when explicit score crosses this. Lower = stricter (safer).
  nsfwThreshold: 0.45,
  // Thumbnails hide detail; be stricter to catch partial nudity.
  thumbThreshold: 0.35,
  // "Sexy" is the class that fires on nudity/partial nudity — weight it fully.
  sexyWeight: 1.0,
  maxPerSecond: 24,
});

// Per-class floors catch borderline cases where the combined score dips below
// the threshold (e.g. sexyWeight < 1 from managed policy).
export const CLASS_FLOORS = Object.freeze({
  porn: 0.4,
  hentai: 0.4,
  sexy: 0.5,
});

export const NSFW_CLASSES = Object.freeze(["Drawing", "Hentai", "Neutral", "Porn", "Sexy"]);

// Known adult CDNs — always classify with the stricter thumb threshold.
const ADULT_HOST_RE =
  /(?:pornhub|phncdn|xvideos|xvcdn|xhamster|xhcdn|xnxx|redtube|youporn|tube8|spankbang|eporner|beeg|porn|xxx)/i;

export function classScores(predictions) {
  let porn = 0;
  let hentai = 0;
  let sexy = 0;
  for (const p of predictions) {
    if (p.className === "Porn") porn = p.probability;
    else if (p.className === "Hentai") hentai = p.probability;
    else if (p.className === "Sexy") sexy = p.probability;
  }
  return { porn, hentai, sexy };
}

export function nsfwScore(predictions, sexyWeight) {
  const { porn, hentai, sexy } = classScores(predictions);
  return Math.max(porn, hentai, sexy * sexyWeight);
}

export function isExplicitImageUrl(url) {
  return typeof url === "string" && ADULT_HOST_RE.test(url);
}

export function isBlockScore(score, settings, isThumb = false, predictions = null) {
  const threshold = isThumb ? settings.thumbThreshold : settings.nsfwThreshold;
  if (score >= threshold) return true;
  if (predictions) {
    const { porn, hentai, sexy } = classScores(predictions);
    if (porn >= CLASS_FLOORS.porn || hentai >= CLASS_FLOORS.hentai || sexy >= CLASS_FLOORS.sexy) {
      return true;
    }
  }
  return false;
}

// Cache confident safe verdicts so duplicate tiles/sources unblur instantly.
export function shouldCacheVerdict(verdict) {
  if (!verdict || verdict.reason) return false;
  if (verdict.block) return true;
  return (verdict.score ?? 1) < 0.28;
}
