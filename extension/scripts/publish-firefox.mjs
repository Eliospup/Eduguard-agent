#!/usr/bin/env node
/**
 * Build, sign (AMO), and prepare a Firefox XPI release for Guardi policy install.
 *
 * Usage (PowerShell):
 *   cd extension
 *   $env:WEB_EXT_API_KEY = "user:123456:78"
 *   $env:WEB_EXT_API_SECRET = "your_jwt_secret"
 *   npm.cmd run publish:firefox
 *
 * Optional:
 *   npm.cmd run publish:firefox -- --version 0.8.4
 *   npm.cmd run publish:firefox -- --skip-sign   (build + config only)
 */
import { spawnSync } from "node:child_process";
import {
  copyFileSync,
  existsSync,
  mkdirSync,
  readFileSync,
  readdirSync,
  writeFileSync,
} from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const repoRoot = join(root, "..");
const manifestPath = join(root, "public", "manifest.base.json");
const storeConfigPath = join(root, "store-config.json");
const configCsPath = join(repoRoot, "Config.cs");
const webExtOutput = join(root, "web-ext-output");
const releasesDir = join(root, "releases");

const args = process.argv.slice(2);
const skipSign = args.includes("--skip-sign");
const versionArg = args.find((a, i) => args[i - 1] === "--version");

function readJson(path) {
  return JSON.parse(readFileSync(path, "utf8").replace(/^\uFEFF/, ""));
}

function writeJson(path, data) {
  writeFileSync(path, `${JSON.stringify(data, null, 2)}\n`, "utf8");
}

function run(cmd, cmdArgs, opts = {}) {
  const bin = process.platform === "win32" && cmd === "npm" ? "npm.cmd" : cmd;
  const result = spawnSync(bin, cmdArgs, {
    stdio: "inherit",
    cwd: opts.cwd ?? root,
    shell: process.platform === "win32",
    env: process.env,
  });
  if ((result.status ?? 1) !== 0) process.exit(result.status ?? 1);
}

function syncConfigCs(firefoxInstallUrl, chromiumExtensionId, chromeUpdateUrl) {
  let cs = readFileSync(configCsPath, "utf8");
  const block = `    public const string ImageShieldExtensionId = "${chromiumExtensionId}";
    public const string ImageShieldChromeUpdateUrl =
        "${chromeUpdateUrl}";
    public const string ImageShieldFirefoxAddonId = "image-shield@guardi.app";
    public const string ImageShieldFirefoxInstallUrl =
        "${firefoxInstallUrl}";`;
  const re =
    /    public const string ImageShieldExtensionId[\s\S]*?ImageShieldFirefoxInstallUrl =\s*\r?\n\s*"[^"]*";/;
  if (!re.test(cs)) {
    console.error("Could not find ImageShield* block in Config.cs");
    process.exit(1);
  }
  cs = cs.replace(re, block);
  writeFileSync(configCsPath, cs, "utf8");
}

function findSignedXpi(version) {
  if (!existsSync(webExtOutput)) return null;
  const xpis = readdirSync(webExtOutput).filter((f) => f.endsWith(".xpi"));
  const exact = xpis.filter((f) => f.includes(`-${version}.xpi`) || f.includes(`_${version}.xpi`));
  if (exact.length === 0) return null;
  exact.sort((a, b) => b.localeCompare(a));
  return join(webExtOutput, exact[0]);
}

function loadAmoCredentials() {
  let apiKey = process.env.WEB_EXT_API_KEY?.trim();
  let apiSecret = process.env.WEB_EXT_API_SECRET?.trim();
  if (apiKey && apiSecret) return { apiKey, apiSecret };

  const localCredsPath = join(root, ".amo-credentials.local");
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

const manifest = readJson(manifestPath);
const storeConfig = readJson(storeConfigPath);
const version = versionArg ?? manifest.version ?? storeConfig.version;

if (!/^\d+\.\d+\.\d+$/.test(version)) {
  console.error(`Invalid version "${version}". Use semver like 0.8.4`);
  process.exit(1);
}

console.log(`\n=== Guardi Image Shield Firefox publish v${version} ===\n`);

manifest.version = version;
writeJson(manifestPath, manifest);
console.log(`Updated ${manifestPath}`);

const releaseTag = `extension-v${version}`;
const githubRepo = "Eliospup/Eduguard-agent";
const firefoxInstallUrl = `https://github.com/${githubRepo}/releases/download/${releaseTag}/guardi-image-shield.xpi`;

storeConfig.version = version;
storeConfig.firefoxInstallUrl = firefoxInstallUrl;
writeJson(storeConfigPath, storeConfig);
console.log(`Updated ${storeConfigPath}`);
console.log(`  firefoxInstallUrl = ${firefoxInstallUrl}`);

syncConfigCs(
  firefoxInstallUrl,
  storeConfig.chromiumExtensionId,
  storeConfig.chromeUpdateUrl
);
console.log(`Updated ${configCsPath}`);

console.log("\n--- build:firefox ---\n");
run("npm", ["run", "build:firefox"]);

if (!skipSign) {
  const { apiKey, apiSecret } = loadAmoCredentials();
  if (!apiKey || !apiSecret) {
    console.error(`
Missing AMO JWT credentials.

  $env:WEB_EXT_API_KEY = "user:123456:78"
  $env:WEB_EXT_API_SECRET = "your_secret"
  npm.cmd run publish:firefox

Or create extension/.amo-credentials.local (see .amo-credentials.local.example).
Or run with --skip-sign to only build and update config.
`);
    process.exit(1);
  }

  process.env.WEB_EXT_API_KEY = apiKey;
  process.env.WEB_EXT_API_SECRET = apiSecret;

  console.log("\n--- sign:firefox (AMO unlisted) ---\n");
  run("npm", ["run", "sign:firefox"]);
}

mkdirSync(releasesDir, { recursive: true });
const releaseXpiVersioned = join(releasesDir, `guardi-image-shield-${version}.xpi`);
const releaseXpiStable = join(releasesDir, "guardi-image-shield.xpi");
const signedSource = findSignedXpi(version);

if (signedSource) {
  copyFileSync(signedSource, releaseXpiVersioned);
  copyFileSync(signedSource, releaseXpiStable);
  console.log(`\nRelease artifacts:`);
  console.log(`  ${releaseXpiVersioned}`);
  console.log(`  ${releaseXpiStable}`);
} else if (skipSign) {
  console.log("\n--skip-sign: no signed XPI copied (exact version required in web-ext-output/).");
} else {
  console.error("\nSigning finished but no .xpi found in web-ext-output/");
  process.exit(1);
}

const ghAsset = signedSource ? releaseXpiStable : null;

console.log(`
=== Next: GitHub Release ===

Upload the XPI so Firefox policy install_url stays HTTPS-stable.

1. Create release (needs gh CLI + GitHub auth):

   gh release create ${releaseTag} ^
     "${ghAsset ?? `extension/releases/guardi-image-shield.xpi`}" ^
     --repo ${githubRepo} ^
     --title "Guardi Image Shield extension v${version}" ^
     --notes "Firefox XPI — shield inactive when Guardi off or closed."

   PowerShell (one line):

   gh release create ${releaseTag} "${ghAsset ?? releaseXpiStable}" --repo ${githubRepo} --title "Guardi Image Shield extension v${version}" --notes "Firefox XPI v${version}"

2. Verify URL downloads the file:
   ${firefoxInstallUrl}

3. Relaunch Guardi as Administrator (re-applies Firefox policies).

4. Restart Firefox completely (about:policies → ExtensionSettings → image-shield@guardi.app).

5. Test:
   - Guardi ON + shield ON  → blur active
   - Shield OFF in settings → no blur within ~3s
   - Guardi quit (PIN)      → no blur within ~3s

=== Done ===
`);
