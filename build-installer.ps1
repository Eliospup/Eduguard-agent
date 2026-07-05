<#
.SYNOPSIS
    Builds the branded Guardi installer end-to-end.

.DESCRIPTION
    1. Publishes a self-contained win-x64 build (the .NET 9 runtime is embedded, so a
       regular user needs no prerequisites).
    2. Generates the Guardi-branded installer artwork (icon + wizard images) from the
       mascot PNG.
    3. Compiles installer\Guardi.iss with Inno Setup (ISCC) into a single setup.exe.

    Run from a normal shell. Guardi must NOT be running/installed while you (re)build,
    or the publish step cannot overwrite files.

.PARAMETER Version
    Version stamped on the installer. Defaults to the extension store-config version.

.PARAMETER SkipPublish
    Reuse an existing publish\GuardiSetup folder (faster when only tweaking the installer).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\build-installer.ps1
#>
[CmdletBinding()]
param(
    [string]$Version = "0.8.43",
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"
$root       = $PSScriptRoot
$project    = Join-Path $root "EduGuardAgent.csproj"
$publishDir = Join-Path $root "publish\GuardiSetup"
$assetsDir  = Join-Path $root "installer\assets"
$outputDir  = Join-Path $root "installer\output"
$localFeed  = Join-Path $root ".nuget-local-feed"
$mascot     = Join-Path $root "Assets\Mascots\guardi.png"

Write-Host "== Guardi installer build ==" -ForegroundColor Cyan

# --- Optional Authenticode signing -----------------------------------------
# Signing activates the runtime binary-trust check (Security/AuthenticodeVerifier) that gates
# the secure-state IPC pipe, and removes the SmartScreen warning on the installer. Provide a
# certificate to sign; without one, the build proceeds UNSIGNED (fully functional - the trust
# check simply degrades to the path-only check).
#   Env vars (either form):
#     GUARDI_SIGN_PFX + GUARDI_SIGN_PASS   - path to a .pfx and its password
#     GUARDI_SIGN_THUMB                    - thumbprint of a cert already in the cert store
#   Optional: GUARDI_SIGN_TS (RFC-3161 timestamp URL, default DigiCert).
function Find-SignTool {
    $cmd = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $roots = @("${env:ProgramFiles(x86)}\Windows Kits\10\bin", "${env:ProgramFiles}\Windows Kits\10\bin")
    foreach ($r in $roots) {
        if (Test-Path $r) {
            $found = Get-ChildItem -Path $r -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
                Where-Object { $_.FullName -match '\\x64\\' } | Sort-Object FullName -Descending | Select-Object -First 1
            if ($found) { return $found.FullName }
        }
    }
    return $null
}

$script:SignTool = $null
$script:SignConfigured = [bool]($env:GUARDI_SIGN_PFX -or $env:GUARDI_SIGN_THUMB)

function Invoke-GuardiSign {
    param([string]$File, [string]$Label)
    if (-not $script:SignConfigured) { return }
    if (-not $script:SignTool) {
        $script:SignTool = Find-SignTool
        if (-not $script:SignTool) {
            Write-Host "      Signing requested but signtool.exe not found (install the Windows 10/11 SDK). Continuing UNSIGNED." -ForegroundColor Yellow
            $script:SignConfigured = $false
            return
        }
    }
    $ts = if ($env:GUARDI_SIGN_TS) { $env:GUARDI_SIGN_TS } else { "http://timestamp.digicert.com" }
    if ($env:GUARDI_SIGN_PFX) {
        & $script:SignTool sign /fd SHA256 /f $env:GUARDI_SIGN_PFX /p $env:GUARDI_SIGN_PASS /tr $ts /td SHA256 $File | Out-Null
    } else {
        & $script:SignTool sign /fd SHA256 /sha1 $env:GUARDI_SIGN_THUMB /tr $ts /td SHA256 $File | Out-Null
    }
    if ($LASTEXITCODE -ne 0) { throw "signtool failed on $Label (exit $LASTEXITCODE)." }
    Write-Host "      Signed $Label." -ForegroundColor DarkGray
}

if ($script:SignConfigured) {
    Write-Host "Authenticode signing: ENABLED." -ForegroundColor Green
} else {
    Write-Host "Authenticode signing: disabled (build will be UNSIGNED - set GUARDI_SIGN_PFX/PASS or GUARDI_SIGN_THUMB to enable)." -ForegroundColor Yellow
}

# --- 1. Publish (self-contained) -------------------------------------------
if (-not $SkipPublish) {
    Write-Host "[1/3] Publishing self-contained win-x64 build..." -ForegroundColor Cyan
    if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

    # The csproj pins RestoreSources to the local feed. ADD nuget.org via
    # RestoreAdditionalProjectSources (RestoreSources path-normalizes URLs and breaks them),
    # so the self-contained runtime packs can be pulled.
    & dotnet publish $project `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=false `
        -p:DebugType=none `
        -p:DebugSymbols=false `
        -p:RestoreAdditionalProjectSources=https://api.nuget.org/v3/index.json `
        -o $publishDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)." }

    # Sign the main executable BEFORE Inno packages it, so the installed exe carries the
    # signature the runtime IPC trust check compares against.
    Invoke-GuardiSign -File (Join-Path $publishDir "EduGuardAgent.exe") -Label "EduGuardAgent.exe"
} else {
    Write-Host "[1/3] Skipping publish (reusing $publishDir)." -ForegroundColor Yellow
    if (-not (Test-Path (Join-Path $publishDir "EduGuardAgent.exe"))) {
        throw "publish/GuardiSetup/EduGuardAgent.exe not found - run without -SkipPublish first."
    }
}

# --- 2. Branding artwork ---------------------------------------------------
Write-Host "[2/3] Generating Guardi-branded installer artwork..." -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $assetsDir | Out-Null
Add-Type -AssemblyName System.Drawing

function New-GuardiIco {
    param([string]$SourcePng, [string]$OutIco)
    $src = [System.Drawing.Image]::FromFile($SourcePng)
    try {
        $size = 256
        $bmp = New-Object System.Drawing.Bitmap($size, $size)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.Clear([System.Drawing.Color]::Transparent)
        # Fit the mascot preserving aspect ratio.
        $scale = [Math]::Min($size / $src.Width, $size / $src.Height)
        $w = [int]($src.Width * $scale); $h = [int]($src.Height * $scale)
        $g.DrawImage($src, [int](($size - $w) / 2), [int](($size - $h) / 2), $w, $h)
        $g.Dispose()

        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $png = $ms.ToArray(); $ms.Dispose(); $bmp.Dispose()

        # Vista+ PNG-compressed ICO (1 image).
        $fs = [System.IO.File]::Create($OutIco)
        $bw = New-Object System.IO.BinaryWriter($fs)
        $bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]1)      # ICONDIR
        $bw.Write([Byte]0); $bw.Write([Byte]0)                                # 256x256 -> 0,0
        $bw.Write([Byte]0); $bw.Write([Byte]0)                                # colors, reserved
        $bw.Write([UInt16]1); $bw.Write([UInt16]32)                           # planes, bpp
        $bw.Write([UInt32]$png.Length); $bw.Write([UInt32]22)                 # size, offset
        $bw.Write($png)
        $bw.Flush(); $bw.Dispose(); $fs.Dispose()
    } finally { $src.Dispose() }
}

function New-GuardiWizardBmp {
    param([string]$SourcePng, [string]$OutBmp, [int]$Width, [int]$Height, [switch]$MascotOnly)
    $src = [System.Drawing.Image]::FromFile($SourcePng)
    try {
        $bmp = New-Object System.Drawing.Bitmap($Width, $Height)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality

        # Guardi vertical gradient: Primary #2563EB -> PrimaryDark #1E3A8A.
        $top    = [System.Drawing.Color]::FromArgb(0x25, 0x63, 0xEB)
        $bottom = [System.Drawing.Color]::FromArgb(0x1E, 0x3A, 0x8A)
        $rect = New-Object System.Drawing.Rectangle(0, 0, $Width, $Height)
        $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $top, $bottom, 90)
        $g.FillRectangle($brush, $rect)
        $brush.Dispose()

        # Mascot: fill for the small tile, upper area for the tall banner.
        if ($MascotOnly) {
            $pad = [int]($Width * 0.12)
            $box = $Width - 2 * $pad
            $scale = [Math]::Min($box / $src.Width, $box / $src.Height)
            $w = [int]($src.Width * $scale); $h = [int]($src.Height * $scale)
            $g.DrawImage($src, [int](($Width - $w) / 2), [int](($Height - $h) / 2), $w, $h)
        } else {
            $box = [int]($Width * 0.72)
            $scale = [Math]::Min($box / $src.Width, $box / $src.Height)
            $w = [int]($src.Width * $scale); $h = [int]($src.Height * $scale)
            $g.DrawImage($src, [int](($Width - $w) / 2), [int]($Height * 0.10), $w, $h)
        }
        $g.Dispose()
        $bmp.Save($OutBmp, [System.Drawing.Imaging.ImageFormat]::Bmp)
        $bmp.Dispose()
    } finally { $src.Dispose() }
}

if (-not (Test-Path $mascot)) { throw "Mascot not found: $mascot" }
New-GuardiIco       -SourcePng $mascot -OutIco (Join-Path $assetsDir "guardi.ico")
New-GuardiWizardBmp -SourcePng $mascot -OutBmp (Join-Path $assetsDir "wizard-large.bmp") -Width 164 -Height 314
New-GuardiWizardBmp -SourcePng $mascot -OutBmp (Join-Path $assetsDir "wizard-small.bmp") -Width 138 -Height 140 -MascotOnly
Write-Host "      Artwork written to installer\assets." -ForegroundColor DarkGray

# --- 3. Compile with Inno Setup --------------------------------------------
Write-Host "[3/3] Compiling installer with Inno Setup..." -ForegroundColor Cyan
$iscc = $null
foreach ($c in @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
    "${env:LOCALAPPDATA}\Programs\Inno Setup 6\ISCC.exe")) {
    if ($c -and (Test-Path $c)) { $iscc = $c; break }
}
if (-not $iscc) {
    $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($cmd) { $iscc = $cmd.Source }
}

New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

if (-not $iscc) {
    Write-Host ""
    Write-Host "Inno Setup (ISCC.exe) not found." -ForegroundColor Yellow
    Write-Host "Install it, then re-run this script:" -ForegroundColor Yellow
    Write-Host "    winget install JRSoftware.InnoSetup" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "The self-contained build is ready in: $publishDir" -ForegroundColor Green
    Write-Host "(you can already test the app by launching EduGuardAgent.exe from that folder)" -ForegroundColor DarkGray
    exit 2
}

& $iscc `
    "/DAppVersion=$Version" `
    "/DSourceDir=$publishDir" `
    "/DAssetsDir=$assetsDir" `
    "/DOutputDir=$outputDir" `
    (Join-Path $root "installer\Guardi.iss")
if ($LASTEXITCODE -ne 0) { throw "ISCC failed (exit $LASTEXITCODE)." }

$setup = Join-Path $outputDir "GuardiSetup-$Version.exe"

# Sign the finished installer so users don't hit a SmartScreen "unknown publisher" warning.
Invoke-GuardiSign -File $setup -Label "GuardiSetup-$Version.exe"

Write-Host ""
Write-Host "== Done ==" -ForegroundColor Green
Write-Host "Installer: $setup" -ForegroundColor Green
if (-not $script:SignConfigured) {
    Write-Host "(unsigned build - set GUARDI_SIGN_PFX/GUARDI_SIGN_PASS or GUARDI_SIGN_THUMB to sign)" -ForegroundColor DarkGray
}
