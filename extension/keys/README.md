# Dev signing key (local testing only)

`dev.pem` is generated automatically by `npm run pack:host` if missing.

- **Do not use in production** — publish via Chrome Web Store / Firefox AMO instead.
- The same key always yields the same Chromium extension ID, so `Config.cs` stays stable across repacks on your machine.
- `dev.pem` is gitignored; each developer gets their own ID unless you share the key file.
