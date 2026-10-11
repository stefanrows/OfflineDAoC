[CmdletBinding(DefaultParameterSetName = 'Install')]
param(
    [Parameter(Mandatory = $true)][string]$InstallRoot,
    [Parameter(Mandatory = $true, ParameterSetName = 'Install')][string]$Stage,
    [Parameter(ParameterSetName = 'Install')][switch]$Apply,
    [Parameter(Mandatory = $true, ParameterSetName = 'Restore')][string]$RestoreBackup
)

$ErrorActionPreference = 'Stop'

# Installs the staged 512x512 Map window built by
# source/server/tools/build_map_client.py: Atlantis and Isles map_window.xml,
# the Isles map_icons.tga marker atlas, and any staged ui/maps/*.dds. Dry run by
# default. Every file is hash checked; the game, server, and launcher must be
# closed. Nothing is stopped by this script.

$BaselineFixed = @{
    'ui/atlantis/map_window.xml' = 'cd1c6cf793363e8eecbdc922af34c83e3dc84b8c86d65ac4b8abd4bf480913f7'
    'ui/isles/map_window.xml'    = 'cd1c6cf793363e8eecbdc922af34c83e3dc84b8c86d65ac4b8abd4bf480913f7'
    'ui/isles/map_icons.tga'     = 'cd1f458f86d096dd3b7c3a1772626994345403a7c99b374f8218d2babcf42311'
}
$AllowedPath = '^ui/(atlantis/map_window\.xml|isles/map_window\.xml|isles/map_icons\.tga|maps/[A-Za-z0-9_]+\.dds)$'

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

function ToWindowsPath([string]$Relative) {
    return ($Relative -replace '/', '\')
}

$root = [IO.Path]::GetFullPath($InstallRoot).TrimEnd('\')
$client = Join-Path $root 'runtime\client-opendaoc\app'
if (-not (Test-Path -LiteralPath $client -PathType Container)) { throw "Client folder not found: $client" }
RequireStopped

if ($PSCmdlet.ParameterSetName -eq 'Restore') {
    $backup = [IO.Path]::GetFullPath($RestoreBackup)
    $manifestPath = Join-Path $backup 'manifest.json'
    if (-not (Test-Path -LiteralPath $manifestPath)) { throw "Backup manifest not found: $manifestPath" }
    $record = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($record.InstallRoot -ne $root) { throw 'Backup is for a different installation.' }
    $files = @($record.Files)
    foreach ($file in $files) {
        if ($file.Path -notmatch $AllowedPath) { throw "Unexpected path in backup manifest: $($file.Path)" }
        RequireHash (Join-Path $backup (ToWindowsPath $file.Path)) $file.PreSha256 'Backup'
        $current = Hash (Join-Path $client (ToWindowsPath $file.Path))
        if ($current -ne $file.OutputSha256 -and $current -ne $file.PreSha256) {
            throw "Installed file changed since the map install: $($file.Path) ($current). Restore refused."
        }
    }
    foreach ($file in $files) {
        CopyVerified (Join-Path $backup (ToWindowsPath $file.Path)) (Join-Path $client (ToWindowsPath $file.Path)) $file.PreSha256
    }
    Write-Host "Map window restored from $backup; backup retained."
    return
}

$stageRoot = [IO.Path]::GetFullPath($Stage)
$manifestPath = Join-Path $stageRoot 'manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath)) { throw "Stage manifest not found: $manifestPath" }
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.kind -ne 'map-window-512') { throw 'Stage is not a 512x512 map window build.' }

$entries = @($manifest.files)
foreach ($required in $BaselineFixed.Keys) {
    if (-not ($entries | Where-Object { $_.path -eq $required })) { throw "Stage is missing $required" }
}

$installs = foreach ($entry in $entries) {
    if ($entry.path -notmatch $AllowedPath) { throw "Unexpected file in stage manifest: $($entry.path)" }
    if ($entry.stagePath -ne $entry.path.Substring(3)) { throw "Stage path does not match $($entry.path)" }
    if ($BaselineFixed.ContainsKey($entry.path) -and $entry.baselineSha256 -ne $BaselineFixed[$entry.path]) {
        throw "Stage baseline differs for $($entry.path)."
    }
    $installed = Join-Path $client (ToWindowsPath $entry.path)
    $pre = Hash $installed
    if ($pre -ne $entry.baselineSha256 -and $pre -ne $entry.sha256) {
        throw "Installed $($entry.path) matches neither the stock nor the staged hash ($pre)."
    }
    RequireHash (Join-Path $stageRoot (ToWindowsPath $entry.stagePath)) $entry.sha256 "Stage $($entry.path)"
    [pscustomobject]@{
        Path = $entry.path
        Local = $installed
        Stage = (Join-Path $stageRoot (ToWindowsPath $entry.stagePath))
        Pre = $pre
        Output = $entry.sha256
    }
}
$installs = @($installs)

Write-Host 'Files to install (stock or already staged hashes verified):'
foreach ($file in $installs) {
    $state = if ($file.Pre -eq $file.Output) { 'already staged' } else { 'stock' }
    Write-Host "  $($file.Path) [$state]"
}
if (-not $Apply) { Write-Host 'Dry run only. Pass -Apply to install.'; return }

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backup = "$root-backups\client-map-$stamp"
if (Test-Path -LiteralPath $backup) { throw "Backup already exists: $backup" }
New-Item -ItemType Directory -Path $backup -Force | Out-Null
foreach ($file in $installs) {
    $target = Join-Path $backup (ToWindowsPath $file.Path)
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    CopyVerified $file.Local $target $file.Pre
}
$record = [pscustomobject]@{
    InstallRoot = $root
    Stage = $stageRoot
    Files = @($installs | ForEach-Object {
        [pscustomobject]@{ Path = $_.Path; PreSha256 = $_.Pre; OutputSha256 = $_.Output }
    })
}
$record | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $backup 'manifest.json') -Encoding UTF8

try {
    RequireStopped
    foreach ($file in $installs) {
        CopyVerified $file.Stage $file.Local $file.Output
    }
    Write-Host "Map window installed. Backup: $backup"
    Write-Host "Restore with: -InstallRoot '$root' -RestoreBackup '$backup'"
}
catch {
    $reason = $_.Exception.Message
    try {
        RequireStopped
        foreach ($file in $installs) {
            CopyVerified (Join-Path $backup (ToWindowsPath $file.Path)) $file.Local $file.Pre
        }
    }
    catch { throw "Install failed ($reason); automatic rollback failed ($($_.Exception.Message)). Backup: $backup" }
    throw "Install failed and was rolled back: $reason. Backup: $backup"
}
