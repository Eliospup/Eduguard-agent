#!/usr/bin/env node
/**
 * One-shot: build + AMO sign + GitHub release for the Firefox XPI.
 *
 * Usage (PowerShell — no ExecutionPolicy issues, use npm.cmd):
 *   cd C:\Users\vferr\Projects\EduGuardAgent\extension
 *   $env:WEB_EXT_API_KEY = "user:123456:78"
 *   $env:WEB_EXT_API_SECRET = "ton_secret"
 *   npm.cmd run release:firefox
 *
 * Bump version before release:
 *   npm.cmd run release:firefox -- --version 0.8.6
 *
 * Skip GitHub (build + sign only):
 *   npm.cmd run release:firefox -- --skip-github
 */
import { spawnSync } from "node:child_process";
import { existsSync, readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const args = process.argv.slice(2);
const skipGithub = args.includes("--skip-github");
const skipSign = args.includes("--skip-sign");
const versionIdx = args.indexOf("--version");
const versionArg = versionIdx >= 0 ? args[versionIdx + 1] : null;

const githubRepo = "Eliospup/Eduguard-agent";

function run(cmd, cmdArgs, opts = {}) {
  const bin = process.platform === "win32" && cmd === "npm" ? "npm.cmd" : cmd;
  const result = spawnSync(bin, cmdArgs, {
    stdio: "inherit",
    cwd: opts.cwd ?? root,
    shell: false,
    env: process.env,
  });
  if ((result.status ?? 1) !== 0) process.exit(result.status ?? 1);
}

const version =
  versionArg ??
  JSON.parse(readFileSync(join(root, "public", "manifest.base.json"), "utf8")).version;

const releaseTag = `extension-v${version}`;
const xpiPath = join(root, "releases", "guardi-image-shield.xpi");
const installUrl = `https://github.com/${githubRepo}/releases/download/${releaseTag}/guardi-image-shield.xpi`;

console.log(`\n=== release:firefox v${version} ===\n`);

const publishArgs = ["scripts/publish-firefox.mjs"];
if (versionArg) publishArgs.push("--version", versionArg);
if (skipSign) publishArgs.push("--skip-sign");

run("node", publishArgs);

if (skipGithub) {
  console.log("\n--skip-github: release GitHub ignorée.");
  process.exit(0);
}

if (!existsSync(xpiPath)) {
  console.error(`XPI introuvable: ${xpiPath}`);
  process.exit(1);
}

const gh = process.platform === "win32" ? "gh.exe" : "gh";
const ghCheck = spawnSync(gh, ["--version"], { shell: true, stdio: "pipe" });
if (ghCheck.status !== 0) {
  console.error(`
gh CLI introuvable. Installe-le puis reconnecte-toi:

  winget install GitHub.cli
  gh auth login

Puis relance:
  npm.cmd run release:firefox
`);
  process.exit(1);
}

console.log(`\n--- GitHub release ${releaseTag} ---\n`);

run(gh, [
  "release",
  "create",
  releaseTag,
  xpiPath,
  "--repo",
  githubRepo,
  "--title",
  `Guardi Image Shield extension v${version}`,
  "--notes",
  `Firefox XPI v${version} — Guardi Image Shield`,
]);

console.log(`
=== Terminé ===

URL policy Firefox:
  ${installUrl}

Ensuite:
  1. dotnet build (Guardi fermé)
  2. Relance Guardi en administrateur
  3. Redémarre Firefox
  4. about:addons → version ${version}
`);
