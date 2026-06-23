import { readFileSync, writeFileSync, existsSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const extRoot = join(dirname(fileURLToPath(import.meta.url)), "..");
const repoRoot = join(extRoot, "..");
const configPath = join(repoRoot, "Config.cs");
const agentConfigPath = join(extRoot, "host", "agent-config.json");

if (!existsSync(agentConfigPath)) {
  console.error("Missing extension/host/agent-config.json — run: npm run pack:host");
  process.exit(1);
}

const cfg = JSON.parse(readFileSync(agentConfigPath, "utf8"));
let cs = readFileSync(configPath, "utf8");

const block = `    public const string ImageShieldExtensionId = "${cfg.chromiumExtensionId}";
    public const string ImageShieldChromeUpdateUrl =
        "${cfg.chromeUpdateUrl}";
    public const string ImageShieldFirefoxAddonId = "image-shield@guardi.app";
    public const string ImageShieldFirefoxInstallUrl =
        "${cfg.firefoxInstallUrl}";`;

const re = /    public const string ImageShieldExtensionId[\s\S]*?ImageShieldFirefoxInstallUrl =\s*\r?\n\s*"[^"]*";/;
if (!re.test(cs)) {
  console.error("Could not find ImageShield* block in Config.cs");
  process.exit(1);
}

cs = cs.replace(re, block);
writeFileSync(configPath, cs, "utf8");
console.log(`Updated ${configPath}`);
console.log(`  ImageShieldExtensionId = ${cfg.chromiumExtensionId}`);
