<#
.SYNOPSIS
    Verifies that Guardi left NO trace after uninstall.

.DESCRIPTION
    Checks every place Guardi touches the machine - scheduled tasks, data folders (all
    profiles), the root certificate, the hosts file, browser managed policies, and the
    native-messaging registration - and reports each as CLEAN or LEFTOVER.

    Run elevated AFTER uninstalling. Exit code 0 = fully clean, 1 = leftovers found.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\verify-clean.ps1
#>
[CmdletBinding()]
param()

$issues = @()
function Check {
    param([string]$Name, [scriptblock]$Test)
    $leftover = & $Test
    if ($leftover) {
        Write-Host ("  [LEFTOVER] {0}" -f $Name) -ForegroundColor Red
        foreach ($l in @($leftover)) { Write-Host ("             -> {0}" -f $l) -ForegroundColor DarkYellow }
        $script:issues += $Name
    } else {
        Write-Host ("  [clean]    {0}" -f $Name) -ForegroundColor Green
    }
}

Write-Host "== Guardi leave-no-trace verification ==" -ForegroundColor Cyan

# Scheduled tasks
Check "Scheduled task: GuardiAgent" {
    if (Get-ScheduledTask -TaskName "GuardiAgent" -ErrorAction SilentlyContinue) { "GuardiAgent still registered" }
}
Check "Scheduled task: GuardiSystem" {
    if (Get-ScheduledTask -TaskName "GuardiSystem" -ErrorAction SilentlyContinue) { "GuardiSystem still registered" }
}

# Data folders
Check "ProgramData\EduGuard" {
    $p = Join-Path $env:ProgramData "EduGuard"
    if (Test-Path $p) { $p }
}
Check "Per-profile AppData\EduGuard" {
    $found = @()
    $usersRoot = Split-Path $env:USERPROFILE -Parent
    Get-ChildItem $usersRoot -Directory -ErrorAction SilentlyContinue | ForEach-Object {
        foreach ($sub in @("AppData\Roaming\EduGuard", "AppData\Local\EduGuard")) {
            $p = Join-Path $_.FullName $sub
            if (Test-Path $p) { $found += $p }
        }
    }
    $found
}

# Root certificate
Check "Root certificate (EduGuard Supervision Root)" {
    Get-ChildItem Cert:\LocalMachine\Root -ErrorAction SilentlyContinue |
        Where-Object { $_.Subject -like "*EduGuard Supervision Root*" } |
        ForEach-Object { $_.Thumbprint }
}

# Hosts file
Check "Hosts file (Guardi block)" {
    $hosts = Join-Path $env:WINDIR "System32\drivers\etc\hosts"
    if (Test-Path $hosts) {
        $hit = Select-String -Path $hosts -Pattern "EduGuard","Guardi" -SimpleMatch -ErrorAction SilentlyContinue
        if ($hit) { $hit.Line }
    }
}

# Chromium managed policies (forcelist / ExtensionSettings / SafeSearch)
$extId = "pooilkajkfmogajdafmaphmjecofpbbk"
Check "Chromium policies (Chrome/Edge/Brave)" {
    $found = @()
    $roots = @(
        "HKLM:\SOFTWARE\Policies\Google\Chrome",
        "HKLM:\SOFTWARE\Policies\Microsoft\Edge",
        "HKLM:\SOFTWARE\Policies\BraveSoftware\Brave")
    foreach ($r in $roots) {
        $fl = Join-Path $r "ExtensionInstallForcelist"
        if (Test-Path $fl) {
            (Get-Item $fl).Property | ForEach-Object {
                $v = (Get-ItemProperty $fl).$_
                if ($v -like "$extId*") { $found += "$fl -> $v" }
            }
        }
        if (Test-Path $r) {
            $es = (Get-ItemProperty $r -Name "ExtensionSettings" -ErrorAction SilentlyContinue).ExtensionSettings
            if ($es -and $es -like "*$extId*") { $found += "$r\ExtensionSettings contains $extId" }
            $tp = Join-Path $r "3rdparty\extensions\$extId"
            if (Test-Path $tp) { $found += $tp }
        }
    }
    $found
}

# Native messaging host
Check "Native messaging host (com.guardi.eduguard)" {
    $found = @()
    foreach ($hive in @("HKLM:", "HKCU:")) {
        foreach ($vendor in @("Software\Mozilla", "Software\Google\Chrome", "Software\Microsoft\Edge")) {
            $p = "$hive\$vendor\NativeMessagingHosts\com.guardi.eduguard"
            if (Test-Path $p) { $found += $p }
        }
    }
    $found
}

Write-Host ""
if ($issues.Count -eq 0) {
    Write-Host "RESULT: fully clean - no Guardi trace found." -ForegroundColor Green
    exit 0
} else {
    Write-Host ("RESULT: {0} leftover(s) found." -f $issues.Count) -ForegroundColor Red
    exit 1
}
