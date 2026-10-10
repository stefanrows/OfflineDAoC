[CmdletBinding(DefaultParameterSetName = 'Install')]
param(
    [string]$InstallRoot,
    [Parameter(ParameterSetName = 'Install')][switch]$Yes,
    [Parameter(Mandatory = $true, ParameterSetName = 'Restore')][string]$RestoreBackup
)

$ErrorActionPreference = 'Stop'

# Applies a stefanrows/OfflineDAoC fork update pack to an Offline DAoC v0.3
# playable folder (or an earlier fork update of one). The pack's manifest pins
# every file by SHA-256. Native client files are replaced only when the
# installed copy is a known v0.3 or fork build. Every replaced file is backed up
# under update-backups, and any failure rolls the whole update back. Saves,
# account, server configuration and bot settings are never touched. Nothing is
# started or stopped by this script.

$ProtectedPaths = @(
    'runtime/account.txt',
    'runtime/data/opendaoc.sqlite3.db',
    'runtime/data/opendaoc.sqlite3.db-wal',
    'runtime/data/opendaoc.sqlite3.db-shm',
    'runtime/server/config/serverconfig.xml',
    'runtime/server/bot-goals.json',
    'runtime/server/rvr-world.json'
)

function Hash([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function RequireHash([string]$Path, [string]$Expected, [string]$Description) {
    $actual = Hash $Path
    if ($actual -ne $Expected) { throw "$Description hash mismatch: $Path ($actual)" }
}

function RequireStopped {
    $names = @('CoreServer', 'OfflineDAoC', 'game', 'game.dll', 'camelot', 'connect')
    $running = @(Get-Process -Name $names -ErrorAction SilentlyContinue)
    if ($running.Count -gt 0) {
        throw ('Close the game, server, and launcher first: ' + (($running | ForEach-Object { "$($_.Name) PID $($_.Id)" }) -join ', '))
    }
}

function NativePath([string]$Root, [string]$Relative) {
    $full = [IO.Path]::GetFullPath((Join-Path $Root ($Relative -replace '/', '\')))
    if (-not $full.StartsWith($Root + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Path escapes the target folder: $Relative" }
    return $full
}

function CopyVerified([string]$Source, [string]$Destination, [string]$Expected) {
    RequireHash $Source $Expected 'Source'
    New-Item -ItemType Directory -Path (Split-Path -Parent $Destination) -Force | Out-Null
    Copy-Item -LiteralPath $Source -Destination $Destination -Force
    RequireHash $Destination $Expected 'Installed'
}

function ResolveInstallRoot([string]$Path) {
    if (-not $Path) {
        $parent = Split-Path -Parent $PSScriptRoot
        if (Test-Path -LiteralPath (Join-Path $parent 'START OFFLINE DAOC.cmd')) { $Path = $parent }
        else { $Path = Read-Host 'Drag your Offline DAoC folder (the one with START OFFLINE DAOC.cmd) here and press Enter' }
    }
    $Path = $Path.Trim().Trim('"')
    if (-not $Path) { throw 'No game folder given.' }
    $root = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    foreach ($marker in @('START OFFLINE DAOC.cmd', 'runtime\server\CoreServer.dll', 'runtime\client-opendaoc\app\game.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $root $marker))) { throw "Not an Offline DAoC v0.3 folder (missing $marker): $root" }
    }
    return $root
}

$root = ResolveInstallRoot $InstallRoot
RequireStopped

if ($PSCmdlet.ParameterSetName -eq 'Restore') {
    $backup = [IO.Path]::GetFullPath($RestoreBackup).TrimEnd('\')
    $record = Get-Content -LiteralPath (Join-Path $backup 'backup-manifest.json') -Raw | ConvertFrom-Json
    foreach ($item in $record.Files) {
        $target = NativePath $root $item.Path
        $current = Hash $target
        if ($current -ne $item.InstalledSha256 -and $current -ne $item.PreviousSha256) {
            throw "Restore stopped before changing anything; $($item.Path) was changed after the update ($current)."
        }
        if ($item.PreviousSha256) { RequireHash (NativePath $backup $item.Path) $item.PreviousSha256 'Backup file' }
    }
    foreach ($item in $record.Files) {
        $target = NativePath $root $item.Path
        if ($item.PreviousSha256) { CopyVerified (NativePath $backup $item.Path) $target $item.PreviousSha256 }
        elseif (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Force }
    }
    Write-Host "Restored $($record.Files.Count) files from $backup. Version before the update: $($record.PreviousVersion)."
    return
}

$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'fork-update-manifest.json') -Raw | ConvertFrom-Json
if ($manifest.Format -ne 1 -or $manifest.BaseVersion -ne '0.3' -or $manifest.Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Unexpected update manifest.' }
$payload = Join-Path $PSScriptRoot 'payload'

# Check everything before changing anything.
$plan = @()
foreach ($file in $manifest.Files) {
    if ($ProtectedPaths -contains $file.Path.ToLowerInvariant() -or $file.Path -like 'runtime/data/*') { throw "Update pack contains a protected path: $($file.Path)" }
    $source = NativePath ([IO.Path]::GetFullPath($payload).TrimEnd('\')) $file.Path
    RequireHash $source $file.SHA256 'Update pack file'
    $target = NativePath $root $file.Path
    $current = Hash $target
    if ($current -eq $file.SHA256) { continue }
    if ($file.Guard -eq 'native' -and @($file.Allowed) -notcontains $current) {
        if ($current) { throw "Refusing to replace $($file.Path): installed copy is not a known v0.3 or fork build ($current). Nothing was changed." }
        if (@($file.Allowed) -notcontains 'absent') { throw "Refusing to update: $($file.Path) is missing from the installation. Nothing was changed." }
    }
    $plan += [pscustomobject]@{ Path = $file.Path; Source = $source; Target = $target; Previous = $current; New = $file.SHA256 }
}

$previousVersion = 'unknown'
$launcher = Join-Path $root 'runtime\OfflineDAoC.exe'
if (Test-Path -LiteralPath $launcher) { $previousVersion = (Get-Item -LiteralPath $launcher).VersionInfo.FileVersion }

Write-Host "Offline DAoC fork $($manifest.Version) update for $root"
if ($plan.Count -eq 0) {
    Write-Host 'Every file already matches this version. Nothing to do.'
    return
}
Write-Host "$($plan.Count) files will be replaced or added. Saves, account, server config and bot settings are not touched."
$hasSave = Test-Path -LiteralPath (Join-Path $root 'runtime\data\opendaoc.sqlite3.db')
if ($hasSave -and -not $Yes) {
    Write-Host ''
    Write-Host 'IMPORTANT: this fork is a Camlann/Mordred style full-PvP world. The first time the'
    Write-Host 'launcher starts after this update it asks for a one-time world reset. It makes a full'
    Write-Host 'backup first and keeps your local account, but old v0.3 characters, inventories, bots'
    Write-Host 'and guilds do not carry over. Keep a copy of this folder if you want to go back.'
}
if (-not $Yes) {
    $answer = Read-Host 'Type YES to install'
    if ($answer -ne 'YES') { Write-Host 'Cancelled. Nothing was changed.'; return }
}

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backup = Join-Path $root "update-backups\fork-$($manifest.Version)-$stamp"
New-Item -ItemType Directory -Path $backup | Out-Null
$records = @()
foreach ($item in $plan) {
    if ($item.Previous) { CopyVerified $item.Target (NativePath $backup $item.Path) $item.Previous }
    $records += [pscustomobject]@{ Path = $item.Path; PreviousSha256 = $item.Previous; InstalledSha256 = $item.New }
}
[pscustomobject]@{ Version = $manifest.Version; PreviousVersion = $previousVersion; Files = $records } |
    ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $backup 'backup-manifest.json') -Encoding UTF8

$done = @()
try {
    foreach ($item in $plan) {
        $done += $item
        CopyVerified $item.Source $item.Target $item.New
    }
}
catch {
    Write-Host "Update failed: $($_.Exception.Message). Rolling back..."
    foreach ($item in $done) {
        if ($item.Previous) { CopyVerified (NativePath $backup $item.Path) $item.Target $item.Previous }
        elseif (Test-Path -LiteralPath $item.Target) { Remove-Item -LiteralPath $item.Target -Force }
    }
    throw 'The update was rolled back; the installation is unchanged.'
}

Write-Host "Installed Offline DAoC fork $($manifest.Version) ($($plan.Count) files)."
Write-Host "Backup: $backup"
Write-Host "To undo: powershell -ExecutionPolicy Bypass -File `"$PSCommandPath`" -InstallRoot `"$root`" -RestoreBackup `"$backup`""
