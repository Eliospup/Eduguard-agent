import { createWriteStream, existsSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import archiver from "archiver";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const chromiumDir = join(root, "dist", "chromium");
const outPath = join(root, "dist", "guardi-image-shield-chrome.zip");

if (!existsSync(join(chromiumDir, "manifest.json"))) {
  console.error("Run npm run build:chromium first.");
  process.exit(1);
}

await new Promise((resolve, reject) => {
  const out = createWriteStream(outPath);
  const archive = archiver("zip", { zlib: { level: 9 } });
  out.on("close", resolve);
  archive.on("error", reject);
  archive.pipe(out);
  archive.directory(chromiumDir, false);
  archive.finalize();
});

console.log(`Chrome Web Store zip -> ${outPath}`);
