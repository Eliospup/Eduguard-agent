# Publier l’extension Guardi Image Shield (Chrome + Firefox)

Guardi force-installe l’extension via les **stores officiels**. Sans publication, `store-config.json` reste en `REPLACE_*` et l’extension ne peut pas s’installer.

**Temps estimé** : 2–4 h la première fois (surtout l’attente de review Chrome).

---

## Vue d’ensemble

| Étape | Toi (manuel) | Guardi (automatique) |
|-------|----------------|----------------------|
| 1 | Builder l’extension (`npm run build`) | — |
| 2 | Publier sur **Chrome Web Store** (unlisted) | — |
| 3 | Signer sur **Firefox AMO** (unlisted) | — |
| 4 | Héberger le XPI signé en HTTPS | — |
| 5 | Remplir `extension/store-config.json` | — |
| 6 | Lancer Guardi en admin | Écrit les policies registry |
| 7 | Ouvrir Chrome | Chrome télécharge depuis le Web Store |
| 8 | — | Guardi vérifie la présence et bloque si absent |

---

## Prérequis

- **Node.js** 18+ (`node -v`)
- **Compte Google** (5 $ USD unique pour développeur Chrome Web Store)
- **Compte Firefox** (gratuit) sur [addons.mozilla.org](https://addons.mozilla.org)
- Un hébergement **HTTPS** pour le XPI Firefox (ton site, GitHub Releases, S3, etc.)
- **Guardi lancé en administrateur** (obligatoire pour les policies)

---

## Partie A — Builder l’extension

Ouvre PowerShell :

```powershell
cd C:\Users\vferr\Projects\EduGuardAgent\extension
npm install
npm run build
```

Vérifie que ces dossiers existent :
- `extension/dist/chromium/` (pour Chrome)
- `extension/dist/firefox/` (pour Firefox)

Pour créer le zip Chrome Web Store :

```powershell
npm run zip:chromium
```

Le fichier `extension/dist/guardi-image-shield-chrome.zip` est prêt à uploader.

---

## Partie B — Chrome Web Store (obligatoire)

### B1. Compte développeur

1. Va sur [Chrome Web Store Developer Dashboard](https://chrome.google.com/webstore/devconsole)
2. Paie les **5 $** d’inscription (une seule fois)
3. Accepte les conditions

### B2. Créer l’extension

1. Clique **New item**
2. Upload `extension/dist/guardi-image-shield-chrome.zip`
3. Remplis les champs obligatoires :
   - **Nom** : Guardi Image Shield
   - **Description** : Blurs inappropriate images on-device
   - **Catégorie** : Productivity ou Family
   - **Langue** : Français + Anglais
4. **Visibilité** : choisis **Unlisted** (pas visible dans le store public, installable seulement via policy ou lien direct)
5. **Permissions** : justifie `<all_urls>`, `storage`, `offscreen` (détection d’images NSFW locale)

### B3. Soumettre et récupérer l’ID

1. Clique **Submit for review**
2. Attends la review (souvent **1–3 jours**, parfois quelques heures)
3. Une fois **Published**, ouvre la fiche de l’extension
4. L’**ID** est dans l’URL :  
   `https://chrome.google.com/webstore/detail/guardi-image-shield/`**`abcdefghijklmnopqrstuvwxyzabcd`**

   Ou va sur `chrome://extensions` → mode développeur → si tu installes depuis le store, l’ID s’affiche.

5. Note cet ID (32 caractères minuscules).

### B4. Vérifier que la force-install marche

Sur le PC de test, **après publication** :

1. Édite `extension/store-config.json` (voir Partie D)
2. Lance Guardi en admin
3. Ouvre `chrome://policy` → tu dois voir `ExtensionInstallForcelist` avec ton ID
4. Ferme Chrome complètement, rouvre-le
5. `chrome://extensions` → **Guardi Image Shield** doit apparaître (installée par l’enterprise policy)

---

## Partie C — Firefox AMO (pour Firefox Release)

### C1. Créer les clés API

1. Connecte-toi sur [addons.mozilla.org](https://addons.mozilla.org)
2. Va sur [API credentials](https://addons.mozilla.org/developers/addon/api/key/)
3. Crée une clé **JWT** (pas legacy)
4. Note `WEB_EXT_API_KEY` et `WEB_EXT_API_SECRET`

### C2. Signer l’extension

Dans PowerShell (remplace par tes vraies clés) :

```powershell
cd C:\Users\vferr\Projects\EduGuardAgent\extension
$env:WEB_EXT_API_KEY = "ton_api_key"
$env:WEB_EXT_API_SECRET = "ton_api_secret"
npm.cmd run build:firefox
npm.cmd run sign:firefox
```

Le XPI signé apparaît dans `extension/web-ext-output/*.xpi`.

**Erreur « Unsupported file type » / `listed: true`** : bug connu avec Node.js 24+ et vieilles versions de `web-ext` (le zip est envoyé sous le nom `blob`). Le script `sign-firefox.mjs` force `web-ext@10.3.0` qui corrige ce problème. Relance `npm.cmd run sign:firefox` après `npm.cmd run build:firefox`.

**Erreur « Unauthorized » / « Error decoding signature »** :
1. Accepte l’accord de distribution sur [addons.mozilla.org](https://addons.mozilla.org) (Developers → Submit a version)
2. Régénère les clés JWT (sans espaces en copiant)
3. Synchronise l’horloge Windows (Paramètres → Heure → Synchroniser)
4. Utilise `npm.cmd` dans PowerShell, pas `npm`

### C3. Héberger le XPI en HTTPS

Firefox policy `install_url` doit pointer vers une URL **HTTPS** directe vers le fichier `.xpi`.

Exemples :
- `https://eduguard.app/extension/guardi-image-shield.xpi`
- `https://github.com/ton-user/EduGuardAgent/releases/download/v0.6.9/guardi-image-shield.xpi`

Upload le fichier `.xpi` signé, teste l’URL dans le navigateur (le téléchargement doit démarrer).

### C4. Vérifier

1. Remplis `firefoxInstallUrl` dans `store-config.json`
2. Guardi en admin → ouvre Firefox
3. `about:policies` → `ExtensionSettings` avec `image-shield@guardi.app`

---

## Partie D — Configurer Guardi

Édite `extension/store-config.json` :

```json
{
  "chromiumExtensionId": "TON_ID_CHROME_WEB_STORE",
  "chromeUpdateUrl": "https://clients2.google.com/service/update2/crx",
  "firefoxAddonId": "image-shield@guardi.app",
  "firefoxInstallUrl": "https://ton-domaine.com/guardi-image-shield.xpi",
  "version": "0.6.9"
}
```

Remplace :
- `TON_ID_CHROME_WEB_STORE` → l’ID de la Partie B3
- `firefoxInstallUrl` → l’URL HTTPS du XPI signé (Partie C3)

**Ne commite pas de secrets** — ce fichier ne contient que des IDs publics.

---

## Partie E — Tester

```powershell
cd C:\Users\vferr\Projects\EduGuardAgent
dotnet run
```

Dans l’audit (`%AppData%\EduGuard\audit.log`) :

```
Image shield store config loaded — Chrome ID xxxxx, Firefox URL configured
Image shield policies ready — Chromium values: 21, ...
Extension install method — Google Chrome: Chromium — force-install from Chrome Web Store
```

Puis :
1. Ouvre Chrome (pas avant d’avoir publié + rempli store-config)
2. Attends 1–5 min (premier téléchargement depuis le store)
3. `chrome://extensions` → extension visible
4. Guardi ne doit plus afficher l’overlay d’installation

---

## Dépannage

| Symptôme | Cause probable | Action |
|----------|----------------|--------|
| Log `REPLACE_* placeholders` | store-config pas rempli | Partie D |
| `Chromium values: 0` | Pas lancé en admin | CMD admin + UAC |
| Chrome policy OK, pas d’extension | Extension pas encore publiée / review en cours | Attendre publication |
| Firefox bloqué | XPI non signé ou URL HTTP | HTTPS + `npm run sign:firefox` |
| Overlay reste > 5 min | ID Chrome incorrect | Vérifier ID dans store vs store-config |

---

## Mises à jour futures

1. Bump `version` dans `extension/public/manifest.base.json`
2. `npm run build` + `npm run zip:chromium`
3. Upload nouvelle version Chrome Web Store
4. `npm run sign:firefox` + re-upload XPI
5. Bump `version` dans `store-config.json`
6. Relance Guardi (policies ré-appliquées)

---

## Ce que Guardi ne fait plus

- Pas de serveur local `127.0.0.1:8765`
- Pas de CRX / `--load-extension`
- Pas de restart en boucle pour « installer »

Guardi **écrit les policies** et **vérifie** que l’extension est là. C’est Chrome/Firefox qui téléchargent depuis les stores.
