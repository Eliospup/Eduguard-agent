import { spawnSync } from "node:child_process";
import {
  createWriteStream,
  existsSync,
  mkdirSync,
  readFileSync,
  statSync,
  writeFileSync,
} from "node:fs";
import { createRequire } from "node:module";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { generateKeyPairSync } from "node:crypto";
import archiver from "archiver";
import { extensionIdFromPrivateKeyPem } from "./extension-id.mjs";

const require = createRequire(import.meta.url);
const crx3 = require("crx3");

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const keysDir = join(root, "keys");
const hostDir = join(root, "host");
const keyPath = join(keysDir, "dev.pem");
const chromiumDir = join(root, "dist", "chromium");
const firefoxDir = join(root, "dist", "firefox");

const HOST_PORT = 8765;
const HOST_BASE = `http://127.0.0.1:${HOST_PORT}`;
const CRX_NAME = "guardi-image-shield.crx";
const XPI_NAME = "guardi-image-shield.xpi";

function ensureKey() {
  mkdirSync(keysDir, { recursive: true });
  if (existsSync(keyPath)) return;

  const { privateKey } = generateKeyPairSync("rsa", {
    modulusLength: 2048,
    privateKeyEncoding: { type: "pkcs8", format: "pem" },
    publicKeyEncoding: { type: "spki", format: "pem" },
  });
  writeFileSync(keyPath, privateKey, "utf8");
  console.log(`Created dev signing key -> ${keyPath}`);
}

async function zipDir(sourceDir, outPath) {
  await new Promise((resolve, reject) => {
    const out = createWriteStream(outPath);
    const archive = archiver("zip", { zlib: { level: 9 } });
    out.on("close", resolve);
    archive.on("error", reject);
    archive.pipe(out);
    archive.directory(sourceDir, false);
    archive.finalize();
  });
}

function crxNeedsRebuild(version) {
  const crxPath = join(hostDir, CRX_NAME);
  const xmlPath = join(hostDir, "updates.xml");
  const manifestPath = join(chromiumDir, "manifest.json");

  if (!existsSync(crxPath) || !existsSync(xmlPath) || !existsSync(manifestPath))
    return true;

  const manifest = JSON.parse(readFileSync(manifestPath, "utf8"));
  if (!manifest.update_url)
    return true;

  const xml = readFileSync(xmlPath, "utf8");
  if (!xml.includes(`version="${version}"`))
    return true;

  const manifestMtime = statSync(manifestPath).mtimeMs;
  const crxMtime = statSync(crxPath).mtimeMs;
  return manifestMtime > crxMtime;
}

async function packCrx(version) {
  const crxPath = join(hostDir, CRX_NAME);
  if (!crxNeedsRebuild(version)) {
    const xml = readFileSync(join(hostDir, "updates.xml"), "utf8");
    const match = xml.match(/appid="([^"]+)"/);
    if (match) {
      console.log("Reusing existing CRX.");
      return match[1];
    }
  }

  const codebase = `${HOST_BASE}/${CRX_NAME}`;
  const xmlPath = join(hostDir, "updates.xml");

  const info = await crx3([chromiumDir], {
    keyPath,
    crxPath,
    xmlPath,
    crxURL: codebase,
    appVersion: version,
  });

  return info?.appId ?? extensionIdFromPrivateKeyPem(keyPath);
}

function writeAgentConfig(extensionId, version) {
  const config = {
    chromiumExtensionId: extensionId,
    chromeUpdateUrl: `${HOST_BASE}/updates.xml`,
    firefoxInstallUrl: `${HOST_BASE}/${XPI_NAME}`,
    version,
    hostBase: HOST_BASE,
  };
  writeFileSync(join(hostDir, "agent-config.json"), JSON.stringify(config, null, 2), "utf8");
}

async function main() {
  const chromiumReady = existsSync(join(chromiumDir, "manifest.json"));
  const firefoxReady = existsSync(join(firefoxDir, "manifest.json"));

  if (!chromiumReady || !firefoxReady) {
    console.log("Building extension (chromium + firefox)…");
    const npm = process.platform === "win32" ? "npm.cmd" : "npm";
    const r = spawnSync(npm, ["run", "build"], {
      stdio: "inherit",
      cwd: root,
      shell: true,
    });
    if (r.status !== 0) {
      console.error(`npm run build failed (exit ${r.status ?? "unknown"}).`);
      process.exit(r.status ?? 1);
    }
  } else {
    console.log("Using existing extension/dist/ (delete dist/ to force a rebuild).");
  }

  if (!existsSync(chromiumDir) || !existsSync(firefoxDir)) {
    console.error("dist/chromium or dist/firefox missing after build.");
    process.exit(1);
  }

  const manifest = JSON.parse(readFileSync(join(chromiumDir, "manifest.json"), "utf8"));
  const version = manifest.version;

  ensureKey();
  mkdirSync(hostDir, { recursive: true });

  const extensionId = await packCrx(version);
  console.log(`Chromium extension ID: ${extensionId}`);

  const xpiPath = join(hostDir, XPI_NAME);
  if (!existsSync(xpiPath)) {
    console.log("Packing Firefox XPI…");
    await zipDir(firefoxDir, xpiPath);
  } else {
    console.log("Reusing existing XPI.");
  }

  writeAgentConfig(extensionId, version);
  console.log("Wrote agent-config.json");

  console.log(`
Pack complete -> ${hostDir}

  ${CRX_NAME}
  ${XPI_NAME}
  updates.xml
  agent-config.json

Guardi will pick these up automatically on dotnet run.
`);
}

main().catch((err) => {
  console.error("pack:host failed:", err?.message ?? err);
  process.exit(1);
});
