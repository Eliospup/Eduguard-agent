# Guardi Image Shield — Firefox AMO + GitHub Releases (one-shot)
# Run from repo root or extension folder. Requires Node 18+, npm, gh CLI for upload.

param(
    [string]$Version = "0.8.4",
    [switch]$SkipSign,
    [switch]$SkipRelease
)

$ErrorActionPreference = "Stop"
$ExtensionDir = $PSScriptRoot | Split-Path -Parent
Set-Location $ExtensionDir

if (-not $env:WEB_EXT_API_KEY -and -not $SkipSign) {
    Write-Host @"

=== Clés AMO requises ===
Crée des credentials JWT sur https://addons.mozilla.org/developers/addon/api/key/

  `$env:WEB_EXT_API_KEY = "user:123456:78"
  `$env:WEB_EXT_API_SECRET = "ton_secret_sans_guillemets"

Puis relance ce script, ou utilise -SkipSign pour builder seulement.

"@
    exit 1
}

$publishArgs = @("--version", $Version)
if ($SkipSign) { $publishArgs += "--skip-sign" }

npm.cmd run publish:firefox -- @publishArgs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if ($SkipSign -or $SkipRelease) {
    Write-Host "Étape GitHub Release ignorée (-SkipSign ou -SkipRelease)."
    exit 0
}

$Tag = "extension-v$Version"
$Xpi = Join-Path $ExtensionDir "releases\guardi-image-shield.xpi"
if (-not (Test-Path $Xpi)) {
    Write-Error "XPI introuvable: $Xpi"
}

Write-Host "`n=== GitHub Release $Tag ===`n"
gh release create $Tag $Xpi `
    --repo "Eliospup/Eduguard-agent" `
    --title "Guardi Image Shield extension v$Version" `
    --notes @"
Firefox XPI v$Version

- Extension inactive quand le shield est OFF
- Extension inactive quand Guardi est fermé
- Miroir agent prioritaire sur les policies stale
"@

if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host @"

=== Déploiement terminé ===

URL policy Firefox:
  https://github.com/Eliospup/Eduguard-agent/releases/download/$Tag/guardi-image-shield.xpi

1. Relance Guardi en administrateur
2. Redémarre Firefox (ferme toutes les fenêtres)
3. Vérifie about:policies → ExtensionSettings → image-shield@guardi.app
4. Vérifie about:addons → version $Version

"@
