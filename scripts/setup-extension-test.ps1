# One-shot setup for local extension force-install testing (Windows)
$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$Ext = Join-Path $Root "extension"

Write-Host "=== Guardi extension local test setup ===" -ForegroundColor Cyan

Push-Location $Ext
try {
    npm install
    npm run pack:host
}
finally {
    Pop-Location
}

Write-Host ""
Write-Host "Rebuild agent:" -ForegroundColor Yellow
Write-Host "  dotnet build `"$Root\EduGuardAgent.csproj`""
Write-Host ""
Write-Host "Then in a SECOND terminal, keep the host running:" -ForegroundColor Yellow
Write-Host "  cd `"$Ext`""
Write-Host "  npm run serve:host"
Write-Host ""
Write-Host "Run EduGuardAgent as Administrator and enroll." -ForegroundColor Green
Write-Host "Full guide: docs/EXTENSION_LOCAL_TEST.md"
