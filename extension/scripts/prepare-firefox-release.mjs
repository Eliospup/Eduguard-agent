#!/usr/bin/env node
/** Copy the latest signed XPI from web-ext-output/ to releases/ for GitHub upload. */
import { copyFileSync, existsSync, mkdirSync, readdirSync, readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const webExtOutput = join(root, "web-ext-output");
const releasesDir = join(root, "releases");
const manifest = JSON.parse(readFileSync(join(root, "public", "manifest.base.json"), "utf8"));
const version = manifest.version;

if (!existsSync(webExtOutput)) {
  console.error("web-ext-output/ missing — run npm.cmd run sign:firefox first.");
  process.exit(1);
}

const xpis = readdirSync(webExtOutput).filter((f) => f.endsWith(".xpi"));
if (xpis.length === 0) {
  console.error("No .xpi in web-ext-output/ — signing did not produce an artifact.");
  process.exit(1);
}

const match = xpis.filter((f) => f.includes(`-${version}.xpi`));
if (match.length === 0) {
  console.error(`No signed XPI for v${version} in web-ext-output/ — run npm.cmd run sign:firefox first.`);
  process.exit(1);
}
const picked = match.sort((a, b) => b.localeCompare(a))[0];
const source = join(webExtOutput, picked);

mkdirSync(releasesDir, { recursive: true });
const stable = join(releasesDir, "guardi-image-shield.xpi");
const versioned = join(releasesDir, `guardi-image-shield-${version}.xpi`);
copyFileSync(source, stable);
copyFileSync(source, versioned);

const tag = `extension-v${version}`;
console.log(`Copied ${picked}`);
console.log(`  -> ${stable}`);
console.log(`  -> ${versioned}`);
console.log(`
GitHub release:

  gh release create ${tag} "${stable}" --repo "Eliospup/Eduguard-agent" --title "Guardi Image Shield extension v${version}" --notes "Firefox XPI v${version}"
`);
