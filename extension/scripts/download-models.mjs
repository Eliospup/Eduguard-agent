// Downloads the NSFWJS InceptionV3 (299) model into public/models/.
// InceptionV3 is markedly more accurate on nudity than MobileNetV2.
// Safe to re-run — clears and re-fetches.

import { mkdirSync, writeFileSync, createWriteStream, existsSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { pipeline } from "node:stream/promises";
import { Readable } from "node:stream";

const root = dirname(dirname(fileURLToPath(import.meta.url)));
const outDir = join(root, "public", "models");
const base =
  "https://raw.githubusercontent.com/infinitered/nsfwjs/master/models/inception_v3";

const modelJsonPath0 = join(outDir, "model.json");
if (existsSync(modelJsonPath0)) {
  console.log(`Model already present in ${outDir} (delete the folder to re-download).`);
  process.exit(0);
}
mkdirSync(outDir, { recursive: true });

async function download(url, dest) {
  const res = await fetch(url);
  if (!res.ok) throw new Error(`HTTP ${res.status} for ${url}`);
  if (res.body) {
    await pipeline(Readable.fromWeb(res.body), createWriteStream(dest));
  } else {
    writeFileSync(dest, Buffer.from(await res.arrayBuffer()));
  }
}

const modelJsonUrl = `${base}/model.json`;
const modelJsonPath = join(outDir, "model.json");
console.log("Downloading model.json …");
await download(modelJsonUrl, modelJsonPath);

const model = JSON.parse(await import("node:fs").then((fs) => fs.promises.readFile(modelJsonPath, "utf8")));
const paths = new Set();
for (const manifest of model.weightsManifest ?? []) {
  for (const p of manifest.paths ?? []) paths.add(p);
}

for (const shard of paths) {
  const dest = join(outDir, shard);
  if (existsSync(dest)) {
    console.log(`  ${shard} (already present)`);
    continue;
  }
  console.log(`Downloading ${shard} …`);
  await download(`${base}/${shard}`, dest);
}

console.log(`Model ready in ${outDir}`);
