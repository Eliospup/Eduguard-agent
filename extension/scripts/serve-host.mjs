import { createServer } from "node:http";
import { readFileSync, existsSync, statSync } from "node:fs";
import { dirname, join, extname } from "node:path";
import { fileURLToPath } from "node:url";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const hostDir = join(root, "host");
const PORT = 8765;

const MIME = {
  ".crx": "application/x-chrome-extension",
  ".xpi": "application/x-xpinstall",
  ".xml": "text/xml; charset=utf-8",
  ".json": "application/json; charset=utf-8",
};

if (!existsSync(hostDir)) {
  console.error("host/ folder missing. Run: npm run pack:host");
  process.exit(1);
}

const server = createServer((req, res) => {
  const path = (req.url || "/").split("?")[0];
  const file = path === "/" ? "updates.xml" : path.replace(/^\//, "");
  const full = join(hostDir, file);

  if (!full.startsWith(hostDir) || !existsSync(full) || !statSync(full).isFile()) {
    res.writeHead(404);
    res.end("Not found");
    return;
  }

  const body = readFileSync(full);
  const type = MIME[extname(full).toLowerCase()] || "application/octet-stream";
  res.writeHead(200, {
    "Content-Type": type,
    "Content-Length": body.length,
    "Cache-Control": "no-cache",
  });
  res.end(body);
});

server.listen(PORT, "127.0.0.1", () => {
  console.log(`Guardi extension host -> http://127.0.0.1:${PORT}/`);
  console.log("  updates.xml");
  console.log("  guardi-image-shield.crx");
  console.log("  guardi-image-shield.xpi");
  console.log("Leave this running while testing force-install.");
});
