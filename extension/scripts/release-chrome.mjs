#!/usr/bin/env node
/**
 * One-shot: build + pack CRX3 + GitHub release for the Chromium extension.
 *
 * Mirrors release-firefox.mjs, but Chrome has no AMO: we self-host a CRX3 signed
 * with extension/keys/dev.pem (the same key that fixes the extension ID) plus an
 * updates.xml manifest, both uploaded to the GitHub release. Guardi's registry
 * policy (ExtensionInstallForcelist) then force-installs it from that update URL —
 * the standard Windows managed-policy self-hosting path, no Web Store required.
 *
 * Usage (PowerShell — use npm.cmd):
 *   cd extension
 *   npm.cmd run release:chrome -- --version 0.8.43
 *
 * The GitHub release tag (extension-v<version>) is shared with the Firefox XPI, so
 * run the Firefox release first (it creates the tag + sets firefoxInstallUrl); this
 * script uploads the CRX + updates.xml to the same tag with --clobber.
 *
 * Skip GitHub (build + pack only):
 *   npm.cmd run release:chrome -- --skip-github
 */
import { spawnSync } from "node:child_process";
import { createRequire } from "node:module";
import { copyFileSync, existsSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { extensionIdFromPrivateKeyPem } from "./extension-id.mjs";

const require = createRequire(import.meta.url);
const crx3 = require("crx3");

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const repoRoot = join(root, "..");
const keyPath = join(root, "keys", "dev.pem");
const chromiumDir = join(root, "dist", "chromium");
const releasesDir = join(root, "releases");
const manifestPath = join(root, "public", "manifest.base.json");
const storeConfigPath = join(root, "store-config.json");
const configCsPath = join(repoRoot, "Config.cs");

const githubRepo = "Eliospup/Eduguard-agent";
const CRX_NAME = "guardi-image-shield.crx";
const XML_NAME = "updates.xml";

const args = process.argv.slice(2);
const skipGithub = args.includes("--skip-github");
const versionIdx = args.indexOf("--version");
const versionArg = versionIdx >= 0 ? args[versionIdx + 1] : null;

function readJson(path) {
  return JSON.parse(readFileSync(path, "utf8").replace(/^﻿/, ""));
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

/** Rewrite the four ImageShield* constants in Config.cs so the running agent matches store-config. */
function syncConfigCs(chromiumExtensionId, chromeUpdateUrl, firefoxInstallUrl) {
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

const manifest = readJson(manifestPath);
const version = versionArg ?? manifest.version;
if (!/^\d+\.\d+\.\d+$/.test(version)) {
  console.error(`Invalid version "${version}". Use semver like 0.8.43`);
  process.exit(1);
}

if (!existsSync(keyPath)) {
  console.error(`Missing signing key: ${keyPath}\nThe CRX must be signed with the key that fixes the extension ID.`);
  process.exit(1);
}

const releaseTag = `extension-v${version}`;
const crxDownloadUrl = `https://github.com/${githubRepo}/releases/download/${releaseTag}/${CRX_NAME}`;
const xmlDownloadUrl = `https://github.com/${githubRepo}/releases/download/${releaseTag}/${XML_NAME}`;

console.log(`\n=== release:chrome v${version} ===\n`);

// Pin the manifest version so the CRX carries the intended version.
if (manifest.version !== version) {
  manifest.version = version;
  writeJson(manifestPath, manifest);
  console.log(`Updated ${manifestPath} -> ${version}`);
}

console.log("\n--- build:chromium ---\n");
run("npm", ["run", "build:chromium"]);

if (!existsSync(join(chromiumDir, "manifest.json"))) {
  console.error("dist/chromium missing after build.");
  process.exit(1);
}

mkdirSync(releasesDir, { recursive: true });
const crxPath = join(releasesDir, CRX_NAME);
const xmlPath = join(releasesDir, XML_NAME);

console.log("\n--- pack CRX3 + updates.xml ---\n");
const info = await crx3([chromiumDir], {
  keyPath,
  crxPath,
  xmlPath,
  crxURL: crxDownloadUrl,
  appVersion: version,
});

const extensionId = info?.appId ?? extensionIdFromPrivateKeyPem(keyPath);
console.log(`Chromium extension ID: ${extensionId}`);
console.log(`  CRX      -> ${crxPath}`);
console.log(`  updates  -> ${xmlPath}`);
console.log(`  codebase -> ${crxDownloadUrl}`);

// Keep a versioned copy alongside the stable one (parity with the XPI releases).
copyFileSync(crxPath, join(releasesDir, `guardi-image-shield-${version}.crx`));

// --- Update store-config.json + Config.cs so the agent force-installs from GitHub ---
const storeConfig = readJson(storeConfigPath);
storeConfig.version = version;
storeConfig.chromiumExtensionId = extensionId;
storeConfig.chromeUpdateUrl = xmlDownloadUrl;
writeJson(storeConfigPath, storeConfig);
console.log(`\nUpdated ${storeConfigPath}`);
console.log(`  chromiumExtensionId = ${extensionId}`);
console.log(`  chromeUpdateUrl     = ${xmlDownloadUrl}`);

syncConfigCs(extensionId, xmlDownloadUrl, storeConfig.firefoxInstallUrl);
console.log(`Updated ${configCsPath}`);

if (skipGithub) {
  console.log("\n--skip-github: GitHub upload skipped.");
  process.exit(0);
}

const gh = process.platform === "win32" ? "gh.exe" : "gh";
const ghCheck = spawnSync(gh, ["--version"], { shell: true, stdio: "pipe" });
if (ghCheck.status !== 0) {
  console.error(`
gh CLI introuvable. Installe-le puis reconnecte-toi:
  winget install GitHub.cli
  gh auth login
Puis relance: npm.cmd run release:chrome -- --version ${version}
`);
  process.exit(1);
}

// The Firefox release usually creates the tag first; if it already exists we upload
// (clobbering any prior CRX/xml), otherwise we create it now.
const exists = spawnSync(gh, ["release", "view", releaseTag, "--repo", githubRepo], {
  shell: true,
  stdio: "pipe",
});

if (exists.status === 0) {
  console.log(`\n--- gh release upload ${releaseTag} (CRX + updates.xml) ---\n`);
  run(gh, ["release", "upload", releaseTag, crxPath, xmlPath, "--clobber", "--repo", githubRepo]);
} else {
  console.log(`\n--- gh release create ${releaseTag} (CRX + updates.xml) ---\n`);
  run(gh, [
    "release",
    "create",
    releaseTag,
    crxPath,
    xmlPath,
    "--repo",
    githubRepo,
    "--title",
    `Guardi Image Shield extension v${version}`,
    "--notes",
    `Chromium CRX v${version} — self-hosted force-install`,
  ]);
}

console.log(`
=== Terminé ===

Chrome update URL (policy):
  ${xmlDownloadUrl}
CRX codebase:
  ${crxDownloadUrl}

Ensuite:
  1. dotnet build (Guardi fermé)
  2. Relance Guardi en administrateur (ré-applique ExtensionInstallForcelist)
  3. Ferme puis rouvre Chrome complètement
  4. chrome://extensions → Guardi Image Shield (installée par policy)
`);
