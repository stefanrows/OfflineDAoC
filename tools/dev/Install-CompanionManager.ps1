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
if ($manifest.protocolVersion -ne 2) { throw 'Stage is not a Companion Manager protocol 2 build.' }
$stageGame = Join-Path $stageRoot 'game.dll'
$stageMain = Join-Path $stageRoot 'uimain.xml'
$stageAtlantis = Join-Path $stageRoot 'atlantis\custom8_window.xml'
$stageIsles = Join-Path $stageRoot 'isles\custom8_window.xml'
RequireHash $game $manifest.baselineSha256 'Installed game.dll'
RequireHash $main $manifest.uimainBaselineSha256 'Installed uimain.xml'
RequireHash $stageGame $manifest.outputSha256 'Stage game.dll'
RequireHash $stageMain $manifest.uimainOutputSha256 'Stage uimain.xml'
RequireHash $stageAtlantis $manifest.windowSha256 'Stage Atlantis XML'
RequireHash $stageIsles $manifest.windowSha256 'Stage Isles XML'
if ((Test-Path -LiteralPath $atlantis) -or (Test-Path -LiteralPath $isles)) {
    throw 'Custom8 XML already exists in the installation.'
}

Write-Host "Verified baseline game.dll: $($manifest.baselineSha256)"
Write-Host "Staged manager game.dll:   $($manifest.outputSha256)"
Write-Host 'Files: game.dll, ui/uimain.xml, Atlantis and Isles custom8_window.xml'
if (-not $Apply) { Write-Host 'Dry run only. Pass -Apply to install.'; return }

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
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
