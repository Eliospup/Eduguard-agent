import { build } from "esbuild";
import { cpSync, mkdirSync, rmSync, existsSync, writeFileSync, readFileSync, readdirSync } from "node:fs";
import { createPublicKey } from "node:crypto";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { spawnSync } from "node:child_process";

const root = dirname(fileURLToPath(import.meta.url));
const target = (process.argv[2] || "chromium").toLowerCase();
if (!["chromium", "firefox"].includes(target)) {
  console.error(`Unknown target "${target}". Use "chromium" or "firefox".`);
  process.exit(1);
}

const outdir = join(root, "dist", target);
rmSync(outdir, { recursive: true, force: true });
mkdirSync(outdir, { recursive: true });

const entryPoints = {
  content: join(root, "src", "content.js"),
  "youtube-soft-limit": join(root, "src", "youtube-soft-limit.js"),
  "category-content-guard": join(root, "src", "category-content-guard.js"),
  "guardi-shell": join(root, "src", "guardi-shell-page.js"),
  "guardi-newtab": join(root, "src", "guardi-newtab-page.js"),
};
if (target === "chromium") {
  entryPoints.background = join(root, "src", "background-router.js");
  entryPoints.offscreen = join(root, "src", "offscreen.js");
} else {
  entryPoints.background = join(root, "src", "background-firefox.js");
}
await build({
  absWorkingDir: root,
  entryPoints,
  bundle: true,
  format: "iife",
  target: ["chrome110", "firefox115"],
  legalComments: "none",
  outdir,
  loader: { ".bin": "file" },
  define: {
    "process.env.NODE_ENV": '"production"',
  },
  banner: {
    js: "var global=globalThis;var process={env:{}};",
  },
});

const publicDir = join(root, "public");
for (const name of ["blur.css", "guardi-ui.css", "newtab.html", "popup.html", "blocked-search.html", "youtube-time-blocked.html", "category-blocked.html", "assets", "models"]) {
  const from = join(publicDir, name);
  if (existsSync(from)) cpSync(from, join(outdir, name), { recursive: true });
}
const wasmFrom = join(root, "node_modules", "@tensorflow", "tfjs-backend-wasm", "dist");
if (existsSync(wasmFrom)) {
  const wasmTo = join(outdir, "wasm");
  mkdirSync(wasmTo, { recursive: true });
  for (const f of [
    "tfjs-backend-wasm.wasm",
    "tfjs-backend-wasm-simd.wasm",
    "tfjs-backend-wasm-threaded-simd.wasm",
  ]) {
    const src = join(wasmFrom, f);
    if (existsSync(src)) cpSync(src, join(wasmTo, f));
  }
}

if (target === "chromium") {
  cpSync(join(publicDir, "offscreen.html"), join(outdir, "offscreen.html"));
}

const manifest = JSON.parse(readFileSync(join(publicDir, "manifest.base.json"), "utf8"));

function sanitizeFirefoxManifest(manifest) {
  if (manifest.theme?.colors) {
    const allowed = new Set([
      "frame",
      "frame_inactive",
      "toolbar",
      "toolbar_field",
      "toolbar_field_text",
      "toolbar_field_border",
      "toolbar_text",
      "tab_text",
      "tab_background_text",
      "bookmark_text",
      "button_background_hover",
      "ntp_background",
      "ntp_text",
      "popup",
      "popup_text",
      "popup_border",
    ]);
    const colors = {};
    for (const key of allowed) {
      if (manifest.theme.colors[key] !== undefined) colors[key] = manifest.theme.colors[key];
    }
    manifest.theme.colors = colors;
    if (manifest.theme.properties) {
      manifest.theme.properties = {
        color_scheme: manifest.theme.properties.color_scheme ?? "light",
      };
    }
  }
  return manifest;
}

function convertToFirefoxManifest(manifest, outdir) {
  sanitizeFirefoxManifest(manifest);

  const permissions = new Set(manifest.permissions || []);
  permissions.delete("offscreen");
  for (const host of manifest.host_permissions || []) {
    permissions.add(host);
  }

  const gecko = { id: "image-shield@guardi.app", strict_min_version: "115.0" };
  const icon48 = "assets/icon-48.png";
  const icon96 = "assets/icon-96.png";

  const webAccessible = [];
  for (const dir of ["assets", "models", "wasm"]) {
    const full = join(outdir, dir);
    if (!existsSync(full)) continue;
    for (const name of readdirSync(full)) {
      webAccessible.push(`${dir}/${name}`);
    }
  }

  // Pages reached via a webRequest redirect from web content (e.g. a blocked search typed
  // in the Firefox address bar) are top-level navigations to moz-extension:// URLs, which
  // Firefox only allows if they're web-accessible. Without this the redirect lands on a
  // blank page instead of the block page.
  for (const page of ["blocked-search.html", "youtube-time-blocked.html", "category-blocked.html", "newtab.html"]) {
    if (existsSync(join(outdir, page))) webAccessible.push(page);
  }

  const firefox = {
    manifest_version: 2,
    name: manifest.name,
    version: manifest.version,
    description: manifest.description,
    permissions: [...permissions],
    icons: {
      48: icon48,
      96: icon96,
    },
    browser_action: {
      default_title: manifest.action?.default_title ?? "Guardi Image Shield",
      default_popup: "popup.html",
      default_icon: {
        16: icon48,
        32: icon48,
        48: icon48,
      },
    },
    content_scripts: manifest.content_scripts,
    background: { scripts: ["background.js"] },
    applications: { gecko },
    browser_specific_settings: { gecko },
    web_accessible_resources: webAccessible,
    // Firefox blanks the address bar for a native newtab override (unlike a scripted
    // tabs.update redirect, which always shows the moz-extension:// URL). newtab.html
    // renders a neutral, unbranded page itself when supervision is inactive, since this
    // override can't be toggled off at runtime — see newtab-redirect.js.
    chrome_url_overrides: { newtab: "newtab.html" },
  };

  return firefox;
}

function writePackIcons(outdir) {
  // Real, spec-compliant PNGs (Guardi teal rounded square with a white "G").
  // These MUST be valid PNGs with correct chunk CRCs: Chrome's SandboxedUnpacker
  // decodes every manifest-declared icon and ABORTS the whole install if one fails
  // to decode ("Could not decode image: icon-48.png"). The previous hand-assembled
  // 193-byte blob had bad CRCs — GDI+/Firefox tolerated it but Chrome rejected it,
  // which silently blocked every Chrome Web Store force-install. Do not replace these
  // with a hand-crafted minimal PNG; regenerate real ones if they ever need changing.
  const png48 = Buffer.from(
    "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAJqSURBVGhD7ZmvTyQxHMWR5yDXbjh3SCQSicSQIJHIkyfB4fgTcKxg20kwGDLtKhCQk2DIGRJCEOAIQSBLvgNDZl/b3c7s/GjIvuSj5tvue22ns2nn5maaqV0xLZaZStZ6arDNlNiri4/+krXecLCCvzm15s/6C3wo97kWd1xL0zRMi0euZb93evwLvZRWT8v1zw6tH2qBZ5oZ9BSkbNS17Ds6bR2mhCo9G7GYL/APPXpFy8bRQeewVOygV0u0dDpc82NhSr7RLoieR8R1coANo2IoT9DziGId/RyaBVol6DsTfUSwQYwwLTbReyZ6gMVl2dXSGPNgXDp8NYbr1GpTgb/oPRM9cBQHMc44atog9NcDvWeiB1gcwvk1mS+naULUGuDwgsy/oL8gbdy+WP2FUGsA83SJvpyj6wp6bqju0upzErUFcJni1w9WXY7rPRlX76O2APe36YiZ3ScaUbuuSP6+hNT6qC1Acfnc02he3Fg1TVBLgA0K8HrzFaDqeq5CqwFwmaGq7ESzADm4hbp2lKgDoLmQnQW30k4DuL4DkwxFFYDAZURyfYl9tZ0HcM1CGXUegKgaoop5ovYABK7tcfJtuaE0EiAHvw9FhexSITQaoA2+cYBU7GBxjHgD8FRsYXGMeI8Y6YIBi6MkFVvoPdPvs/4POvmyGkTGokqW0PuX6OwRG8QE0/IKPY+IjhejngXf8ikq1u2UaZmgV6eyd0HLK+ygS+jUvNQ1E10kcCX/Y0cd8Uy3RuhxomgmPq5XrQ5bo9IFH+pnerRKl2zYebOIu8pXrD7R/svU4A+95E1BR/wT78FmikjvHJRfCI91tQUAAAAASUVORK5CYII=",
    "base64"
  );
  const png96 = Buffer.from(
    "iVBORw0KGgoAAAANSUhEUgAAAGAAAABgCAYAAADimHc4AAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAARaSURBVHhe7Z0rbBtBEIYDy1r1bLWshYWFhYElkQoLCwsLG1ZWWBgWg/r2pJKQyndGDkhU2JAoJJIVBTTMigIKrxrbJ7mzd/vy3O6eb37pg96159+dfVia3dtjsVgsFovFYrWkp5Pvb5I82x/m4w9Jnn6JndX3zPYB/Fui1/Dnj+fwAwZTcZLk4u+gEOUukORpnuTjj8/y7CX+zVEIRsugEL/wF99JcnGVFOk7HIMgSor0FYx26Uv2gxmkVxwTb0om6eeaL9VDsqMXs9EjHJ/WBJ0lhcjkL9JrZo9noyc4VuSCTnqT661J58Pp+DWOGZlg5HPw1SRF+qe1nRKnHTOSQvwmXxPggII7YhRMxQmOobMgr0kdMFrIzgo93udvSTrfOhWBi3LDjAWfcEytBAtKTaOMIbArcp4FsJ3CDTL2ON+qwvTBjTH2JNP0G46tkeB4jRtjXEjnOLZG2qX7/NDA/yQ4vkpx/qfFeh2Ae27cSGjKu/PSRId3pfTZ4EzS9zjGSsWw/z+AoD9c4vha6bQEM86ltgNgdx4IvQMyHe2mOn4AIyZSP76AuzQcY6VCXb4dn4myLO9x/Eh0A7Ph7FLq0wedMOD0AoLfrkKZEL0BPoJfKYQJURtgk3ZUudzGRFU7bRCtAYew4Ja3OD6SbLaWpkYMLm6lz7ZFtAaY7HZcAmVirM9ZEKUBJqnHJfim7ftcC6I0QDf6bdJOE7o+Dq7vpc+0QXQG6FIE1QkWzwKfaWeT6AzQLZS+RqYvojNAlRp85mZfRGWA7pKNKv3ERFQG4LyMFSpPt0mnDKDY/cRGVAb0bQEG2IDARGXAzfUEx/w/2Riga0snXws+G9CgXhpAmYLYAAfYAAO1aQDlNpQNcEB3EWdzEGMDHNBdRVDfBanunXppAKAbudv8EYNhA2rQLcQ264AONqAG3TpAmYbYgAZ0achmMVbBBjSgmwUgmzNBHboFv9cGALpZAHI1QXfeAPXeAN0IrWSTjkzbBPXeAMBkpFZSBUy3s6qTqj1KojYAcAkehdiADXyb4Cv4QCcMAEx2RhSiPGmb0BkDKkx2Ry7yHfiKzhlQQWEE5bWGK501YBPT3ZLP3G7KThjQZdiAwLABgWEDAmNvAJclJsXaACgugRth3IEBjWOs1LocvdQQ4wa8qYBjrBTXC6LFul7Quk601BDjBryzgGOsFZesJGOBY2sk3oqSMcKxNVKMZcs6iW25sk1B5VepQcYYqDy51csaoUuXdR3noq2V1u/F8GLsAIx+63qhdYqhgmJHsauUqBK8JFfTAdNELq6cq6bXafl6Ui6upI6YOhatPOSzvp5Y1HTIbDAsxFscOzKtL+nYhAasL91ctHrUJ53jznvOotWRj8Uv6m2wel3V/rJtW61vTOGg1suUBPt8uC/b6qRLoeVsmIqv/XrwITsiOWRRamnE6q/M0a7NiuVoL0QGi2x0gW9S9ZY8/B8KU7WbdPRteRaLxWKxWGr9A6Q75TNKagjWAAAAAElFTkSuQmCC",
    "base64"
  );
  const assetsDir = join(outdir, "assets");
  mkdirSync(assetsDir, { recursive: true });
  writeFileSync(join(assetsDir, "icon-48.png"), png48);
  writeFileSync(join(assetsDir, "icon-96.png"), png96);
}

function lintFirefoxBuild(outdir) {
  const result = spawnSync(
    process.platform === "win32" ? "npx.cmd" : "npx",
    ["--yes", "web-ext", "lint", "--source-dir", outdir],
    { encoding: "utf8", shell: true }
  );
  const output = `${result.stdout || ""}\n${result.stderr || ""}`;
  const manifestErrors = output
    .split(/\r?\n/)
    .filter((line) => /manifest\.json|JSON_INVALID|MANIFEST_/i.test(line));
  if (result.status !== 0 && manifestErrors.length > 0) {
    console.error(output);
    throw new Error("Firefox manifest failed web-ext lint â€” fix manifest.json before deploying.");
  }
  if (result.status !== 0) {
    console.warn("web-ext lint warnings (non-fatal):\n", output.slice(0, 2000));
  }
}

function patchChromiumManifestKey(manifest) {
  const keyPath = join(root, "keys", "dev.pem");
  if (!existsSync(keyPath)) return manifest;
  try {
    const pub = createPublicKey(readFileSync(keyPath, "utf8"));
    manifest.key = pub.export({ type: "spki", format: "der" }).toString("base64");
  } catch {
    // optional â€” unpacked load still works without a fixed ID
  }
  return manifest;
}

function patchChromiumIcons(manifest) {
  const icon48 = "assets/icon-48.png";
  const icon96 = "assets/icon-96.png";
  manifest.icons = { 48: icon48, 96: icon96, 128: icon96 };
  if (manifest.action) {
    manifest.action.default_icon = { 16: icon48, 32: icon48, 48: icon48 };
  }
  return manifest;
}

if (target === "chromium") {
  writePackIcons(outdir);
  patchChromiumIcons(manifest);
  patchChromiumManifestKey(manifest);
  manifest.background = { service_worker: "background.js" };
  // Native new-tab override, same as Firefox. The old scripted redirect relied on
  // setTimeout inside the MV3 service worker, which Chrome suspends before the timers
  // fire — so the Guardi new tab often never appeared. A manifest override is handled by
  // the browser itself (no awake service worker needed) and newtab.html renders neutral
  // on its own when supervision is inactive (see guardi-newtab-page.js).
  manifest.chrome_url_overrides = { newtab: "newtab.html" };
} else {
  writePackIcons(outdir);
  const firefoxManifest = convertToFirefoxManifest(manifest, outdir);
  writeFileSync(join(outdir, "manifest.json"), JSON.stringify(firefoxManifest, null, 2));
  lintFirefoxBuild(outdir);
  console.log(`Built ${target} extension -> ${outdir} (manifest v2, unpacked sideload)`);
  process.exit(0);
}

writeFileSync(join(outdir, "manifest.json"), JSON.stringify(manifest, null, 2));

console.log(`Built ${target} extension -> ${outdir}`);







