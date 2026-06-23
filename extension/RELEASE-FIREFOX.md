# Release Firefox — une commande

Prérequis (une fois) :
- Node.js 18+
- `gh` CLI : `winget install GitHub.cli` puis `gh auth login`
- Clés JWT AMO : https://addons.mozilla.org/developers/addon/api/key/

## Commande unique (copier-coller)

```powershell
cd C:\Users\vferr\Projects\EduGuardAgent\extension

$env:WEB_EXT_API_KEY = "user:XXXXXX:XX"
$env:WEB_EXT_API_SECRET = "ton_secret_sans_guillemets"

npm.cmd run release:firefox
```

Fait automatiquement :
1. Lit la version dans `public/manifest.base.json` (actuellement **0.8.5**)
2. Met à jour `store-config.json` + `Config.cs`
3. Build Firefox (`dist/firefox/`)
4. Signature AMO unlisted
5. Copie `releases/guardi-image-shield.xpi`
6. Crée la release GitHub `extension-v0.8.5`

## Nouvelle version (ex. 0.8.6)

```powershell
cd C:\Users\vferr\Projects\EduGuardAgent\extension

$env:WEB_EXT_API_KEY = "user:XXXXXX:XX"
$env:WEB_EXT_API_SECRET = "ton_secret"

npm.cmd run release:firefox -- --version 0.8.6
```

## Déjà signé sur AMO — GitHub seulement

```powershell
cd C:\Users\vferr\Projects\EduGuardAgent\extension
npm.cmd run prepare:firefox-release
gh release create extension-v0.8.5 "releases\guardi-image-shield.xpi" --repo "Eliospup/Eduguard-agent" --title "Guardi Image Shield extension v0.8.5" --notes "Firefox XPI v0.8.5"
```

## Erreurs fréquentes

| Message | Action |
|---------|--------|
| `Version X already exists` | Bump : `--version 0.8.6` |
| `gh` introuvable | `winget install GitHub.cli` |
| `Unauthorized` (AMO) | Régénère les clés JWT, sync horloge Windows |
| Release GitHub déjà existante | `gh release upload extension-v0.8.5 "releases\guardi-image-shield.xpi" --repo Eliospup/Eduguard-agent --clobber` |

## Après la release

```powershell
cd C:\Users\vferr\Projects\EduGuardAgent
dotnet build
```

Relance Guardi **en admin**, redémarre Firefox.
