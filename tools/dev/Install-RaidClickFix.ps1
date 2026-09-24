[CmdletBinding(DefaultParameterSetName = 'Install')]
param(
    [Parameter(Mandatory = $true)][string]$InstallRoot,
    [Parameter(Mandatory = $true, ParameterSetName = 'Install')][string]$Stage,
    [Parameter(ParameterSetName = 'Install')][switch]$Apply,
    [Parameter(Mandatory = $true, ParameterSetName = 'Restore')][string]$RestoreBackup
)

$ErrorActionPreference = 'Stop'

# Installs the staged raid click-to-target fix (Atlantis and Isles
# custom9_window.xml and custom10_window.xml) built by
# source/server/tools/build_raid_click_fix_client.py. Dry run by default.
# game.dll is only checked, never changed. Every file is hash checked; the game,
# server, and launcher must be closed. Nothing is stopped by this script.

$BaselineWindows = @{
    'custom9_window.xml'  = '6ff8e226ce4160e5fd4be87d1b91f45f59ae0df1a9c0cfdd122ec643d4e1a1d1'
    'custom10_window.xml' = 'aca82bf6c672a9fd0baec129cb457218d8ea700336d0db40ae752911ec7fc6f3'
}
$Skins = @('atlantis', 'isles')

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

function CopyVerified([string]$Source, [string]$Destination, [string]$Expected) {
    RequireHash $Source $Expected 'Source'
    Copy-Item -LiteralPath $Source -Destination $Destination -Force
    RequireHash $Destination $Expected 'Installed'
}

$root = [IO.Path]::GetFullPath($InstallRoot).TrimEnd('\')
$client = Join-Path $root 'runtime\client-opendaoc\app'
if (-not (Test-Path -LiteralPath $client -PathType Container)) { throw "Client folder not found: $client" }
RequireStopped

# One entry per installed file: skin, name, installed path.
$files = foreach ($skin in $Skins) {
    foreach ($name in $BaselineWindows.Keys) {
        [pscustomobject]@{ Skin = $skin; Name = $name; Path = (Join-Path $client "ui\$skin\$name") }
    }
}

if ($PSCmdlet.ParameterSetName -eq 'Restore') {
    $backup = [IO.Path]::GetFullPath($RestoreBackup)
    $manifestPath = Join-Path $backup 'manifest.json'
    if (-not (Test-Path -LiteralPath $manifestPath)) { throw "Backup manifest not found: $manifestPath" }
    $record = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($record.InstallRoot -ne $root) { throw 'Backup is for a different installation.' }
    foreach ($file in $files) {
        $baseline = $BaselineWindows[$file.Name]
        RequireHash (Join-Path $backup "$($file.Skin)\$($file.Name)") $baseline 'Backup'
        $current = Hash $file.Path
        if ($current -ne $record.Outputs.($file.Name) -and $current -ne $baseline) {
            throw "Installed file changed since the raid click fix: $($file.Path) ($current). Restore refused."
        }
    }
    foreach ($file in $files) {
        CopyVerified (Join-Path $backup "$($file.Skin)\$($file.Name)") $file.Path $BaselineWindows[$file.Name]
    }
    Write-Host "Raid windows restored from $backup; backup retained."
    return
}

$stageRoot = [IO.Path]::GetFullPath($Stage)
$manifestPath = Join-Path $stageRoot 'manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath)) { throw "Stage manifest not found: $manifestPath" }
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.raidEventBase -ne 1536) { throw 'Stage is not a raid click-to-target fix build.' }
$game = Hash (Join-Path $client 'game.dll')
if (@($manifest.supportedGameSha256) -notcontains $game) {
    throw "Installed game.dll is not a supported native raid build ($game)."
}
$outputs = @{}
foreach ($name in $BaselineWindows.Keys) {
    $window = $manifest.windows.$name
    if ($window.baselineSha256 -ne $BaselineWindows[$name]) { throw "Stage baseline differs for $name." }
    $outputs[$name] = $window.outputSha256
}
foreach ($file in $files) {
    RequireHash $file.Path $BaselineWindows[$file.Name] "Installed $($file.Skin) $($file.Name)"
    RequireHash (Join-Path $stageRoot "$($file.Skin)\$($file.Name)") $outputs[$file.Name] "Stage $($file.Skin) $($file.Name)"
}

Write-Host "Verified game.dll (unchanged): $game"
Write-Host 'Files: Atlantis and Isles custom9_window.xml and custom10_window.xml'
if (-not $Apply) { Write-Host 'Dry run only. Pass -Apply to install.'; return }

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backup = "$root-backups\raid-click-fix-client-$stamp"
if (Test-Path -LiteralPath $backup) { throw "Backup already exists: $backup" }
foreach ($skin in $Skins) { New-Item -ItemType Directory -Path (Join-Path $backup $skin) -Force | Out-Null }
foreach ($file in $files) {
    CopyVerified $file.Path (Join-Path $backup "$($file.Skin)\$($file.Name)") $BaselineWindows[$file.Name]
}
$record = [pscustomobject]@{
    InstallRoot = $root
    GameSha256 = $game
    Outputs = [pscustomobject]$outputs
    Stage = $stageRoot
}
$record | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $backup 'manifest.json') -Encoding UTF8
try {
    RequireStopped
    foreach ($file in $files) {
        CopyVerified (Join-Path $stageRoot "$($file.Skin)\$($file.Name)") $file.Path $outputs[$file.Name]
    }
    Write-Host "Raid click fix installed. Backup: $backup"
    Write-Host "Restore with: -InstallRoot '$root' -RestoreBackup '$backup'"
}
catch {
    $reason = $_.Exception.Message
    try {
        RequireStopped
        foreach ($file in $files) {
            CopyVerified (Join-Path $backup "$($file.Skin)\$($file.Name)") $file.Path $BaselineWindows[$file.Name]
        }
    }
    catch { throw "Install failed ($reason); automatic rollback failed ($($_.Exception.Message)). Backup: $backup" }
    throw "Install failed and was rolled back: $reason. Backup: $backup"
}
