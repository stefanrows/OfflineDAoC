[CmdletBinding(DefaultParameterSetName = 'Install')]
param(
    [Parameter(Mandatory = $true)][string]$InstallRoot,
    [Parameter(Mandatory = $true, ParameterSetName = 'Install')][string]$Stage,
    [Parameter(ParameterSetName = 'Install')][switch]$Apply,
    [Parameter(Mandatory = $true, ParameterSetName = 'Restore')][string]$RestoreBackup
)

$ErrorActionPreference = 'Stop'

# Installs the companion paperdoll research probe (TASKS 129, docs/COMPANION_PAPERDOLL_RE.md) on top of an
# installed Companion Manager 0.272.0 client. The probe stage is built by source/server/tools/probe_companion_paperdoll.py
# and holds game.dll, uimain.xml, and the Atlantis and Isles custom6_window.xml. It is a research probe, not the
# paperdoll window. Dry run by default; pass -Apply to install. Every file is hash checked.
# The game, server, and launcher must be closed. Nothing is stopped by this script.
# Saves and server files are not touched. Restore the 0.272.0 files with -RestoreBackup <backup folder>.

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

# The verified Companion Manager 0.272.0 game.dll and ui\uimain.xml that the probe replaces.
$BaselineGameSha256 = 'c938e6b50057a4a99ec39211004765dcacb3ffa47880e4ba3fc193add87e612d'
$BaselineMainSha256 = '7d573c315a56bcacae8e8ebc871646654bfe8da5bb1dc745d6023da7f93b9dd9'

$root = [IO.Path]::GetFullPath($InstallRoot).TrimEnd('\')
$client = Join-Path $root 'runtime\client-opendaoc\app'
if (-not (Test-Path -LiteralPath $client -PathType Container)) { throw "Client folder not found: $client" }
RequireStopped

$game = Join-Path $client 'game.dll'
$main = Join-Path $client 'ui\uimain.xml'
$atlantis = Join-Path $client 'ui\atlantis\custom6_window.xml'
$isles = Join-Path $client 'ui\isles\custom6_window.xml'

if ($PSCmdlet.ParameterSetName -eq 'Restore') {
    $backup = [IO.Path]::GetFullPath($RestoreBackup)
    $manifestPath = Join-Path $backup 'backup-manifest.json'
    if (-not (Test-Path -LiteralPath $manifestPath)) { throw "Backup manifest not found: $manifestPath" }
    $record = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($record.InstallRoot -ne $root) { throw 'Backup is for a different installation.' }
    if (-not ($record.BaselineGameSha256 -and $record.BaselineMainSha256 -and $record.ProbeGameSha256 -and $record.ProbeMainSha256 -and $record.ProbeWindowSha256)) {
        throw "Backup manifest is incomplete: $manifestPath"
    }
    RequireHash (Join-Path $backup 'game.dll') $record.BaselineGameSha256 'Backup game.dll'
    RequireHash (Join-Path $backup 'uimain.xml') $record.BaselineMainSha256 'Backup uimain.xml'
    $gameNow = Hash $game
    if ($gameNow -ne $record.ProbeGameSha256 -and $gameNow -ne $record.BaselineGameSha256) {
        throw "Installed game.dll is neither the probe nor the 0.272.0 build: $game ($gameNow). Restore refused."
    }
    $mainNow = Hash $main
    if ($mainNow -ne $record.ProbeMainSha256 -and $mainNow -ne $record.BaselineMainSha256) {
        throw "Installed uimain.xml is neither the probe nor the 0.272.0 file: $main ($mainNow). Restore refused."
    }
    CopyVerified (Join-Path $backup 'game.dll') $game $record.BaselineGameSha256
    CopyVerified (Join-Path $backup 'uimain.xml') $main $record.BaselineMainSha256
    foreach ($path in @($atlantis, $isles)) {
        $current = Hash $path
        if (-not $current) { continue }
        if ($current -eq $record.ProbeWindowSha256) { Remove-Item -LiteralPath $path -Force }
        else { Write-Warning "Left in place, not the probe window: $path ($current)" }
    }
    Write-Host "Companion paperdoll probe restored from $backup; backup retained."
    return
}

$stageRoot = [IO.Path]::GetFullPath($Stage)
$manifestPath = Join-Path $stageRoot 'manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath)) { throw "Stage manifest not found: $manifestPath" }
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.probe -ne $true -or $manifest.probeName -ne 'companion-paperdoll-probe') {
    throw 'Stage is not a companion paperdoll probe build.'
}
if ($manifest.inputSha256 -ne $BaselineGameSha256 -or $manifest.inputUimainSha256 -ne $BaselineMainSha256) {
    throw 'Stage was not built from the installed Companion Manager 0.272.0 files.'
}
$probeGame = $manifest.outputSha256
$probeMain = $manifest.uimainOutputSha256
$probeWindow = $manifest.windowSha256
$stageGame = Join-Path $stageRoot 'game.dll'
$stageMain = Join-Path $stageRoot 'uimain.xml'
$stageAtlantis = Join-Path $stageRoot 'atlantis\custom6_window.xml'
$stageIsles = Join-Path $stageRoot 'isles\custom6_window.xml'
RequireHash $stageGame $probeGame 'Stage game.dll'
RequireHash $stageMain $probeMain 'Stage uimain.xml'
RequireHash $stageAtlantis $probeWindow 'Stage Atlantis XML'
RequireHash $stageIsles $probeWindow 'Stage Isles XML'

# The probe is already in place: nothing to do, or refuse a half-installed set.
if ((Hash $game) -eq $probeGame) {
    $complete = (Hash $main) -eq $probeMain -and (Hash $atlantis) -eq $probeWindow -and (Hash $isles) -eq $probeWindow
    if ($complete) {
        Write-Host "Already installed: the paperdoll probe $probeGame and its UI files match this stage. Nothing to do."
        return
    }
    throw 'game.dll already matches this stage, but the UI files do not. Nothing was changed; inspect the installation.'
}

RequireHash $game $BaselineGameSha256 'Installed game.dll'
RequireHash $main $BaselineMainSha256 'Installed uimain.xml'
foreach ($path in @($atlantis, $isles)) {
    if (Test-Path -LiteralPath $path) { throw "custom6_window.xml already present: $path" }
}

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backup = "$root-backups\companion-paperdoll-probe-$stamp"
$copies = @(@($stageGame, $game, $probeGame), @($stageMain, $main, $probeMain), @($stageAtlantis, $atlantis, $probeWindow), @($stageIsles, $isles, $probeWindow))
Write-Host "Verified 0.272.0 game.dll: $BaselineGameSha256"
Write-Host "Staged probe game.dll:     $probeGame"
if (-not $Apply) {
    foreach ($copy in $copies) { Write-Host "  $($copy[0]) ($($copy[2])) -> $($copy[1])" }
    Write-Host "Backup folder: $backup"
    Write-Host 'Dry run only. Pass -Apply to install.'
    return
}

if (Test-Path -LiteralPath $backup) { throw "Backup already exists: $backup" }
New-Item -ItemType Directory -Path $backup -Force | Out-Null
CopyVerified $game (Join-Path $backup 'game.dll') $BaselineGameSha256
CopyVerified $main (Join-Path $backup 'uimain.xml') $BaselineMainSha256
[pscustomobject]@{
    InstallRoot = $root
    Created = $stamp
    BaselineGameSha256 = $BaselineGameSha256
    BaselineMainSha256 = $BaselineMainSha256
    ProbeGameSha256 = $probeGame
    ProbeMainSha256 = $probeMain
    ProbeWindowSha256 = $probeWindow
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $backup 'backup-manifest.json') -Encoding UTF8
try {
    RequireStopped
    CopyVerified $stageGame $game $probeGame
    CopyVerified $stageMain $main $probeMain
    CopyVerified $stageAtlantis $atlantis $probeWindow
    CopyVerified $stageIsles $isles $probeWindow
    Write-Host "Companion paperdoll probe installed. Backup: $backup"
    Write-Host "Restore with: & '$PSCommandPath' -InstallRoot '$root' -RestoreBackup '$backup'"
}
catch {
    $reason = $_.Exception.Message
    try {
        RequireStopped
        CopyVerified (Join-Path $backup 'game.dll') $game $BaselineGameSha256
        CopyVerified (Join-Path $backup 'uimain.xml') $main $BaselineMainSha256
        foreach ($path in @($atlantis, $isles)) {
            if ((Hash $path) -eq $probeWindow) { Remove-Item -LiteralPath $path -Force }
        }
    }
    catch { throw "Install failed ($reason); automatic rollback failed ($($_.Exception.Message)). Backup: $backup" }
    throw "Install failed and was rolled back: $reason. Backup: $backup"
}
