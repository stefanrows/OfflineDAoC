[CmdletBinding(DefaultParameterSetName = 'Install')]
param(
    [Parameter(Mandatory = $true)][string]$InstallRoot,
    [Parameter(Mandatory = $true, ParameterSetName = 'Install')][string]$Stage,
    [Parameter(ParameterSetName = 'Install')][switch]$Apply,
    [Parameter(Mandatory = $true, ParameterSetName = 'Restore')][string]$RestoreBackup
)

$ErrorActionPreference = 'Stop'

# Installs the staged Companion Manager client files (game.dll, ui/uimain.xml,
# Atlantis and Isles custom8_window.xml) built by
# source/server/tools/build_companion_manager_client.py. Dry run by default.
# An installation that already has an earlier manager build (0.32.1 or 0.33.0 game.dll with
# the manager's own uimain.xml) is upgraded in place: game.dll and both window files are
# backed up and replaced, and -RestoreBackup puts the previous files back.
# Every file is hash checked; the game, server, and launcher must be closed.
# Nothing is stopped by this script. Saves and server files are not touched.

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

# game.dll builds of earlier Companion Manager releases that can be upgraded in place.
$KnownManagerBuilds = @(
    '3b6274dc385b90bf892f27d96c9e56cb457e1e94cbf45b5892d462a9e4d70890', # 0.32.1
    '88530c0093b285fd38fd6759e464373baa65ebb20473a949bc3b79fccbb41fd3'  # 0.33.0 through 0.166.0
)

function CopyVerified([string]$Source, [string]$Destination, [string]$Expected) {
    RequireHash $Source $Expected 'Source'
    Copy-Item -LiteralPath $Source -Destination $Destination -Force
    RequireHash $Destination $Expected 'Installed'
}

$root = [IO.Path]::GetFullPath($InstallRoot).TrimEnd('\')
$client = Join-Path $root 'runtime\client-opendaoc\app'
if (-not (Test-Path -LiteralPath $client -PathType Container)) { throw "Client folder not found: $client" }
RequireStopped

$game = Join-Path $client 'game.dll'
$main = Join-Path $client 'ui\uimain.xml'
$atlantis = Join-Path $client 'ui\atlantis\custom8_window.xml'
$isles = Join-Path $client 'ui\isles\custom8_window.xml'

if ($PSCmdlet.ParameterSetName -eq 'Restore') {
    $backup = [IO.Path]::GetFullPath($RestoreBackup)
    $manifestPath = Join-Path $backup 'manifest.json'
    if (-not (Test-Path -LiteralPath $manifestPath)) { throw "Backup manifest not found: $manifestPath" }
    $record = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($record.InstallRoot -ne $root) { throw 'Backup is for a different installation.' }
    if ($record.InstallKind -eq 'upgrade') {
        # An in-place upgrade: put back the previous game.dll and window files; uimain.xml was not changed.
        $previous = @(@($game, 'game.dll', $record.PreviousGameSha256, $record.OutputGameSha256),
                      @($atlantis, 'atlantis-custom8_window.xml', $record.PreviousAtlantisSha256, $record.WindowSha256),
                      @($isles, 'isles-custom8_window.xml', $record.PreviousIslesSha256, $record.WindowSha256))
        foreach ($item in $previous) {
            RequireHash (Join-Path $backup $item[1]) $item[2] 'Backup file'
            $current = Hash $item[0]
            if ($current -ne $item[3] -and $current -ne $item[2]) {
                throw "Installed file changed since the Companion Manager upgrade: $($item[0]) ($current). Restore refused."
            }
        }
        foreach ($item in $previous) { CopyVerified (Join-Path $backup $item[1]) $item[0] $item[2] }
        Write-Host "Companion Manager client files restored to the earlier manager build from $backup; backup retained."
        return
    }
    $oldGame = Join-Path $backup 'game.dll'
    $oldMain = Join-Path $backup 'uimain.xml'
    RequireHash $oldGame $record.BaselineGameSha256 'Backup game.dll'
    RequireHash $oldMain $record.BaselineMainSha256 'Backup uimain.xml'
    foreach ($pair in @(@($game, $record.OutputGameSha256, $record.BaselineGameSha256),
                       @($main, $record.OutputMainSha256, $record.BaselineMainSha256),
                       @($atlantis, $record.WindowSha256, $null),
                       @($isles, $record.WindowSha256, $null))) {
        $current = Hash $pair[0]
        if ($current -ne $pair[1] -and $current -ne $pair[2]) {
            throw "Installed file changed since the Companion Manager install: $($pair[0]) ($current). Restore refused."
        }
    }
    CopyVerified $oldGame $game $record.BaselineGameSha256
    CopyVerified $oldMain $main $record.BaselineMainSha256
    foreach ($path in @($atlantis, $isles)) {
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
    }
    Write-Host "Companion Manager client files restored from $backup; backup retained."
    return
}

$stageRoot = [IO.Path]::GetFullPath($Stage)
$manifestPath = Join-Path $stageRoot 'manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath)) { throw "Stage manifest not found: $manifestPath" }
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.baselineSha256 -ne '67dcf68a37b95a93946a943b99d5e19b4a03e08cd6469275e25c7b909de21e99') {
    throw 'Stage was not built from the verified native raid client.'
}
if ($manifest.protocolVersion -ne 3) { throw 'Stage is not a Companion Manager protocol 3 build.' }
$stageGame = Join-Path $stageRoot 'game.dll'
$stageMain = Join-Path $stageRoot 'uimain.xml'
$stageAtlantis = Join-Path $stageRoot 'atlantis\custom8_window.xml'
$stageIsles = Join-Path $stageRoot 'isles\custom8_window.xml'
$upgrade = $KnownManagerBuilds -contains (Hash $game)
if ($upgrade) {
    RequireHash $main $manifest.uimainOutputSha256 'Installed uimain.xml (earlier manager)'
    foreach ($path in @($atlantis, $isles)) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Earlier manager window file missing: $path" }
    }
}
else {
    RequireHash $game $manifest.baselineSha256 'Installed game.dll'
    RequireHash $main $manifest.uimainBaselineSha256 'Installed uimain.xml'
}
RequireHash $stageGame $manifest.outputSha256 'Stage game.dll'
RequireHash $stageMain $manifest.uimainOutputSha256 'Stage uimain.xml'
RequireHash $stageAtlantis $manifest.windowSha256 'Stage Atlantis XML'
RequireHash $stageIsles $manifest.windowSha256 'Stage Isles XML'
if (-not $upgrade -and ((Test-Path -LiteralPath $atlantis) -or (Test-Path -LiteralPath $isles))) {
    throw 'Custom8 XML already exists in the installation.'
}

if ($upgrade) { Write-Host "Upgrading an earlier Companion Manager build (installed game.dll $(Hash $game))." }
else { Write-Host "Verified baseline game.dll: $($manifest.baselineSha256)" }
Write-Host "Staged manager game.dll:   $($manifest.outputSha256)"
Write-Host 'Files: game.dll, ui/uimain.xml, Atlantis and Isles custom8_window.xml'
if (-not $Apply) { Write-Host 'Dry run only. Pass -Apply to install.'; return }

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
if ($upgrade) {
    $backup = "$root-backups\companion-manager-upgrade-$stamp"
    if (Test-Path -LiteralPath $backup) { throw "Backup already exists: $backup" }
    New-Item -ItemType Directory -Path $backup -Force | Out-Null
    $previousGame = Hash $game
    $previousAtlantis = Hash $atlantis
    $previousIsles = Hash $isles
    CopyVerified $game (Join-Path $backup 'game.dll') $previousGame
    CopyVerified $atlantis (Join-Path $backup 'atlantis-custom8_window.xml') $previousAtlantis
    CopyVerified $isles (Join-Path $backup 'isles-custom8_window.xml') $previousIsles
    [pscustomobject]@{
        InstallRoot = $root
        InstallKind = 'upgrade'
        PreviousGameSha256 = $previousGame
        PreviousAtlantisSha256 = $previousAtlantis
        PreviousIslesSha256 = $previousIsles
        OutputGameSha256 = $manifest.outputSha256
        WindowSha256 = $manifest.windowSha256
        Stage = $stageRoot
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $backup 'manifest.json') -Encoding UTF8
    try {
        RequireStopped
        CopyVerified $stageGame $game $manifest.outputSha256
        CopyVerified $stageAtlantis $atlantis $manifest.windowSha256
        CopyVerified $stageIsles $isles $manifest.windowSha256
        Write-Host "Companion Manager client upgraded. Backup: $backup"
        Write-Host "Restore the earlier build with: -InstallRoot '$root' -RestoreBackup '$backup'"
    }
    catch {
        $reason = $_.Exception.Message
        try {
            RequireStopped
            CopyVerified (Join-Path $backup 'game.dll') $game $previousGame
            CopyVerified (Join-Path $backup 'atlantis-custom8_window.xml') $atlantis $previousAtlantis
            CopyVerified (Join-Path $backup 'isles-custom8_window.xml') $isles $previousIsles
        }
        catch { throw "Upgrade failed ($reason); automatic rollback failed ($($_.Exception.Message)). Backup: $backup" }
        throw "Upgrade failed and was rolled back: $reason. Backup: $backup"
    }
    return
}

$backup = "$root-backups\companion-manager-client-$stamp"
if (Test-Path -LiteralPath $backup) { throw "Backup already exists: $backup" }
New-Item -ItemType Directory -Path $backup -Force | Out-Null
CopyVerified $game (Join-Path $backup 'game.dll') $manifest.baselineSha256
CopyVerified $main (Join-Path $backup 'uimain.xml') $manifest.uimainBaselineSha256
$record = [pscustomobject]@{
    InstallRoot = $root
    BaselineGameSha256 = $manifest.baselineSha256
    OutputGameSha256 = $manifest.outputSha256
    BaselineMainSha256 = $manifest.uimainBaselineSha256
    OutputMainSha256 = $manifest.uimainOutputSha256
    WindowSha256 = $manifest.windowSha256
    Stage = $stageRoot
}
$record | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $backup 'manifest.json') -Encoding UTF8
try {
    RequireStopped
    CopyVerified $stageGame $game $manifest.outputSha256
    CopyVerified $stageMain $main $manifest.uimainOutputSha256
    CopyVerified $stageAtlantis $atlantis $manifest.windowSha256
    CopyVerified $stageIsles $isles $manifest.windowSha256
    Write-Host "Companion Manager client installed. Backup: $backup"
    Write-Host "Restore with: -InstallRoot '$root' -RestoreBackup '$backup'"
}
catch {
    $reason = $_.Exception.Message
    try {
        RequireStopped
        CopyVerified (Join-Path $backup 'game.dll') $game $manifest.baselineSha256
        CopyVerified (Join-Path $backup 'uimain.xml') $main $manifest.uimainBaselineSha256
        foreach ($path in @($atlantis, $isles)) {
            if ((Hash $path) -eq $manifest.windowSha256) { Remove-Item -LiteralPath $path -Force }
        }
    }
    catch { throw "Install failed ($reason); automatic rollback failed ($($_.Exception.Message)). Backup: $backup" }
    throw "Install failed and was rolled back: $reason. Backup: $backup"
}
