import { createHash, createPublicKey } from "node:crypto";
import { readFileSync } from "node:fs";

/** Chromium extension ID from an RSA private-key PEM (same algorithm as Chrome). */
export function extensionIdFromPrivateKeyPem(pemPath) {
  const pem = readFileSync(pemPath, "utf8");
  const pub = createPublicKey(pem).export({ type: "spki", format: "der" });
  const hash = createHash("sha256").update(pub).digest();
  let id = "";
  for (let i = 0; i < 16; i++) {
    id += String.fromCharCode(97 + (hash[i] >> 4));
    id += String.fromCharCode(97 + (hash[i] & 0x0f));
  }
  return id;
}
