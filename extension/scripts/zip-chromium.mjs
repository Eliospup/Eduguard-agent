import { createWriteStream, existsSync, readFileSync, writeFileSync, cpSync, rmSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import archiver from "archiver";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const chromiumDir = join(root, "dist", "chromium");
const stagingDir = join(root, "dist", "chromium-store-staging");
const outPath = join(root, "dist", "guardi-image-shield-chrome.zip");

if (!existsSync(join(chromiumDir, "manifest.json"))) {
  console.error("Run npm run build:chromium first.");
  process.exit(1);
}

// The build injects a "key" (derived from keys/dev.pem) so --load-extension sideloads get a
// stable id during local dev. The Chrome Web Store rejects any upload whose manifest "key"
// doesn't match the key already on file for that listing — which for an existing/updated item
// is never our dev key. Store uploads must ship WITHOUT "key"; Chrome/the Store manages that
// item's identity itself. Strip it from a staging copy so dist/chromium (used by
// ChromiumUnpackedDeployer for local sideload) is untouched.
rmSync(stagingDir, { recursive: true, force: true });
cpSync(chromiumDir, stagingDir, { recursive: true });

const manifest = JSON.parse(readFileSync(join(stagingDir, "manifest.json"), "utf8"));
if ("key" in manifest) {
  delete manifest.key;
  writeFileSync(join(stagingDir, "manifest.json"), JSON.stringify(manifest, null, 2));
  console.log("Stripped manifest.key for Web Store upload (dev key would conflict with the published item's identity).");
}

await new Promise((resolve, reject) => {
  const out = createWriteStream(outPath);
  const archive = archiver("zip", { zlib: { level: 9 } });
  out.on("close", resolve);
  archive.on("error", reject);
  archive.pipe(out);
  archive.directory(stagingDir, false);
  archive.finalize();
});

rmSync(stagingDir, { recursive: true, force: true });

console.log(`Chrome Web Store zip -> ${outPath}`);
