# Image Shield — tests locaux (Firefox + Chrome)

## Chrome / Edge / Brave — deux modes (PC personnel)

Chrome **refuse** les CRX locaux via `ExtensionInstallForcelist` sur Windows non joint à un domaine (`[BLOCKLIST]` dans `chrome://policy`). Guardi n'utilise plus `http://127.0.0.1:8765`.

### Mode A — Chrome Web Store (prod, défaut)

Fonctionne sur **tout** PC Windows avec Guardi en admin.

1. Extension publiée (ID `pooilkajkfmogajdafmaphmjecofpbbk` dans `extension/store-config.json`)
2. `ExtensionGuardChromiumUnpackedMode = false` dans `Config.cs` (défaut)
3. Guardi en admin → Image shield ON + Chrome coché
4. Fermer **toutes** les fenêtres Chrome, relancer
5. `chrome://policy` → `ExtensionInstallForcelist` = `…;https://clients2.google.com/service/update2/crx` (**sans** erreur)
6. `chrome://extensions` → Guardi installée (si le store sert déjà le paquet — Guardi vérifie au démarrage)

> Si l'extension n'est **pas encore répertoriée** sur le CWS, Guardi affiche *« pas encore sur le store »* et **ne ferme pas Chrome** (pré-vol `clients2.google.com`).

Si l'ancienne policy localhost traîne encore :

```powershell
reg add "HKLM\SOFTWARE\Policies\Google\Chrome\ExtensionInstallForcelist" /v 1 /t REG_SZ /d "pooilkajkfmogajdafmaphmjecofpbbk;https://clients2.google.com/service/update2/crx" /f
```

### Mode B — code local (`dist/chromium`, comme Firefox Dev)

1. `ExtensionGuardChromiumUnpackedMode = true` dans `Config.cs`
2. `cd extension && npm.cmd run build:chromium`
3. Guardi en admin → Image shield ON + Chrome
4. Guardi copie le build vers `%ProgramData%\EduGuard\chromium-unpacked` et **redémarre Chrome** avec `--load-extension=…`
5. Pas de forcelist localhost — l'extension se charge au lancement piloté par Guardi

> Chrome doit être **relancé par Guardi** (ou après un changement de build) pour charger la version locale. Ouvrir Chrome à la main sans `--load-extension` n'aura pas le shield dev.

---

## Firefox Developer Edition — mode local Guardi

Guardi installe l'extension sur **Firefox Developer Edition** et **ferme Mozilla Firefox Release** s'il est ouvert.

### Prérequis

1. **Firefox Developer Edition** installé
2. Extension buildée :
   ```powershell
   cd C:\Users\vferr\Projects\EduGuardAgent\extension
   npm.cmd run build:firefox
   ```
3. Guardi lancé en **administrateur**

### Test

1. `dotnet run` (CMD admin) — **garder Guardi ouvert**
2. Carte Image shield : active + Firefox Developer Edition
3. Ouvre **Firefox Developer Edition** (icône bleue)
4. `about:addons` → Guardi Image Shield

Si tu ouvres Firefox Release par erreur, Guardi le ferme et affiche un petit popup.
