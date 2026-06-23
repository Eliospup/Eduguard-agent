#!/usr/bin/env node
/**
 * Sign the Firefox XPI via Mozilla AMO (unlisted channel).
 *
 * Prerequisites:
 * 1. Log in at https://addons.mozilla.org — accept the Firefox Add-on Distribution Agreement
 * 2. Create JWT API credentials at https://addons.mozilla.org/developers/addon/api/key/
 *    (NOT legacy keys — choose JWT)
 * 3. Sync Windows clock (Settings → Time → Sync now)
 *
 * Usage (PowerShell — use npm.cmd, not npm):
 *   cd extension
 *   $env:WEB_EXT_API_KEY = "user:123456:78"
 *   $env:WEB_EXT_API_SECRET = "your_jwt_secret_without_quotes"
 *   npm.cmd run sign:firefox
 */
import { spawnSync } from "node:child_process";
import { copyFileSync, existsSync, mkdirSync, readdirSync, readFileSync, rmSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const distFirefox = join(root, "dist", "firefox");
const localCredsPath = join(root, ".amo-credentials.local");

function loadAmoCredentials() {
  let apiKey = process.env.WEB_EXT_API_KEY?.trim();
  let apiSecret = process.env.WEB_EXT_API_SECRET?.trim();
  if (apiKey && apiSecret) return { apiKey, apiSecret };

  if (!existsSync(localCredsPath)) return { apiKey: null, apiSecret: null };

  const lines = readFileSync(localCredsPath, "utf8").split(/\r?\n/);
  for (const line of lines) {
    const trimmed = line.trim();
    if (!trimmed || trimmed.startsWith("#")) continue;
    const eq = trimmed.indexOf("=");
    if (eq <= 0) continue;
    const key = trimmed.slice(0, eq).trim();
    const value = trimmed.slice(eq + 1).trim();
    if (key === "WEB_EXT_API_KEY") apiKey = value;
    if (key === "WEB_EXT_API_SECRET") apiSecret = value;
  }
  return { apiKey: apiKey?.trim() || null, apiSecret: apiSecret?.trim() || null };
}

const { apiKey, apiSecret } = loadAmoCredentials();

if (!apiKey || !apiSecret) {
  console.error(
    "Missing WEB_EXT_API_KEY / WEB_EXT_API_SECRET.\n" +
      "Set env vars or create extension/.amo-credentials.local (see .amo-credentials.local.example).\n" +
      "JWT credentials: https://addons.mozilla.org/developers/addon/api/key/"
  );
  process.exit(1);
}

if (!apiKey.startsWith("user:")) {
  console.error(
    "WEB_EXT_API_KEY must be the JWT issuer (starts with user:), not the legacy key.\n" +
      "Example format: user:19986469:340"
  );
  process.exit(1);
}

if (!existsSync(join(distFirefox, "manifest.json"))) {
  console.error("Run npm.cmd run build:firefox first.");
  process.exit(1);
}

// If dist/firefox was restored from a signed XPI, the old signature directory
// must not be submitted again. web-ext will create a fresh signature.
rmSync(join(distFirefox, "META-INF"), { recursive: true, force: true });

// web-ext < 8.10 uploads a nameless "blob" on Node 24+ -> AMO rejects it.
// Prefer a cached/local web-ext so publishing does not depend on registry.npmjs.org.
const WEB_EXT_VERSION = "10.3.0";
const nodeMajor = Number.parseInt(process.versions.node.split(".")[0], 10);
const webExtExe = process.platform === "win32" ? "web-ext.cmd" : "web-ext";

function parseVersion(text) {
  const match = String(text || "").match(/(\d+)\.(\d+)\.(\d+)/);
  if (!match) return null;
  return match.slice(1).map((n) => Number.parseInt(n, 10));
}

function compareVersions(a, b) {
  for (let i = 0; i < 3; i++) {
    const delta = (a?.[i] ?? 0) - (b?.[i] ?? 0);
    if (delta !== 0) return delta;
  }
  return 0;
}

function versionAtLeast(version, minimum) {
  return compareVersions(version, parseVersion(minimum)) >= 0;
}

function webExtVersion(bin) {
  const result = spawnSync(bin, ["--version"], {
    cwd: root,
    shell: process.platform === "win32",
    encoding: "utf8",
    stdio: ["ignore", "pipe", "pipe"],
  });
  if ((result.status ?? 1) !== 0) return null;
  return parseVersion(result.stdout || result.stderr);
}

function findCachedWebExtBins() {
  const bins = [];
  const envBin = process.env.WEB_EXT_BIN?.trim();
  if (envBin && existsSync(envBin)) bins.push(envBin);

  const localBin = join(root, "node_modules", ".bin", webExtExe);
  if (existsSync(localBin)) bins.push(localBin);

  const localAppData = process.env.LOCALAPPDATA;
  if (localAppData) {
    const npxRoot = join(localAppData, "npm-cache", "_npx");
    if (existsSync(npxRoot)) {
      for (const dir of readdirSync(npxRoot)) {
        const candidate = join(npxRoot, dir, "node_modules", ".bin", webExtExe);
        if (existsSync(candidate)) bins.push(candidate);
      }
    }
  }

  return [...new Set(bins)];
}

function resolveWebExtCommand() {
  const cached = findCachedWebExtBins()
    .map((bin) => ({ bin, version: webExtVersion(bin) }))
    .filter((item) => item.version)
    .sort((a, b) => compareVersions(b.version, a.version));

  const compatible = cached.find((item) => versionAtLeast(item.version, WEB_EXT_VERSION));
  if (compatible) {
    return { cmd: compatible.bin, prefix: [], version: compatible.version.join("."), cached: true };
  }

  if (cached[0]) {
    return { cmd: cached[0].bin, prefix: [], version: cached[0].version.join("."), cached: true };
  }

  const npx = process.platform === "win32" ? "npx.cmd" : "npx";
  return { cmd: npx, prefix: [`web-ext@${WEB_EXT_VERSION}`], version: WEB_EXT_VERSION, cached: false };
}

const webExt = resolveWebExtCommand();
if (nodeMajor >= 24) {
  console.log(`Node ${process.versions.node} detected; using web-ext ${webExt.version} for the Node 24 upload fix.`);
}
console.log(webExt.cached ? `Using cached web-ext ${webExt.version}: ${webExt.cmd}` : `Using npx web-ext@${WEB_EXT_VERSION}`);

const result = spawnSync(
  webExt.cmd,
  [
    ...webExt.prefix,
    "sign",
    "--source-dir",
    distFirefox,
    "--channel",
    "unlisted",
    "--api-key",
    apiKey,
    "--api-secret",
    apiSecret,
    "--artifacts-dir",
    join(root, "web-ext-output"),
  ],
  { stdio: "inherit", cwd: root, shell: process.platform === "win32", env: process.env }
);
if (result.status !== 0) {
  console.error(`
Signing failed.

If you see "Version X already exists" / Conflict:
  → Cette version est déjà sur AMO. Utilise le XPI existant:
     npm.cmd run prepare:firefox-release
  → Ou bump la version dans public/manifest.base.json puis rebuild + resign.

If you see "Unsupported file type" / listed:true:
  → Bug Node 24 + vieux web-ext. Ce script utilise web-ext@${WEB_EXT_VERSION}.
  → Relance: npm.cmd run sign:firefox

If you see "Error decoding signature" / Unauthorized:
  1. Accept the Distribution Agreement on https://addons.mozilla.org
  2. Regenerate JWT API keys (no spaces)
  3. Sync Windows clock (Settings → Time → Sync now)
  4. Use npm.cmd in PowerShell, not npm
`);
  process.exit(result.status ?? 1);
}

function copyToReleases() {
  const outDir = join(root, "web-ext-output");
  const releasesDir = join(root, "releases");
  const version = JSON.parse(readFileSync(join(root, "public", "manifest.base.json"), "utf8")).version;
  const xpis = readdirSync(outDir).filter((f) => f.endsWith(".xpi"));
  const match = xpis.filter((f) => f.includes(`-${version}.xpi`));
  if (match.length === 0) return;
  const picked = match.sort((a, b) => b.localeCompare(a))[0];

  mkdirSync(releasesDir, { recursive: true });
  const source = join(outDir, picked);
  const stable = join(releasesDir, "guardi-image-shield.xpi");
  copyFileSync(source, stable);
  copyFileSync(source, join(releasesDir, `guardi-image-shield-${version}.xpi`));
  console.log(`\nRelease copy ready: ${stable}`);
  console.log(`Next: gh release create extension-v${version} "${stable}" --repo Eliospup/Eduguard-agent --title "Guardi Image Shield extension v${version}"`);
}

copyToReleases();
process.exit(0);
