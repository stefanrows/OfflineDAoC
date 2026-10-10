param([string]$InstallRoot)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

# Downloads the latest stefanrows/OfflineDAoC fork release, verifies its
# SHA-256, and applies it with the pack's own Apply-OfflineDAoCFork.ps1
# (hash-guarded, backed up, rolled back on failure). Refuses while the game,
# server or launcher is open. Saves, account.txt and configs are never touched.

$Repository = 'stefanrows/OfflineDAoC'

function RequireStopped {
    $names = @('CoreServer', 'OfflineDAoC', 'game', 'game.dll', 'camelot', 'connect')
    $running = @(Get-Process -Name $names -ErrorAction SilentlyContinue)
    if ($running.Count -gt 0) {
        throw ('Close the game, server, and launcher first: ' + (($running | ForEach-Object { "$($_.Name) PID $($_.Id)" }) -join ', '))
    }
}

if (-not $InstallRoot) {
    $here = $PSScriptRoot
    foreach ($candidate in @($here, (Split-Path -Parent (Split-Path -Parent $here)))) {
        if ($candidate -and (Test-Path -LiteralPath (Join-Path $candidate 'START OFFLINE DAOC.cmd'))) { $InstallRoot = $candidate; break }
    }
}
if (-not $InstallRoot) { $InstallRoot = Read-Host 'Drag your Offline DAoC folder (the one with START OFFLINE DAOC.cmd) here and press Enter' }
$root = [IO.Path]::GetFullPath($InstallRoot.Trim().Trim('"')).TrimEnd('\')
if (-not (Test-Path -LiteralPath (Join-Path $root 'START OFFLINE DAOC.cmd'))) { throw "Not an Offline DAoC folder: $root" }
RequireStopped

Write-Host "Looking up the latest $Repository release..."
$release = Invoke-RestMethod -UseBasicParsing -Uri "https://api.github.com/repos/$Repository/releases/latest" -Headers @{ 'User-Agent' = 'OfflineDAoC-Updater' }
$base = "https://github.com/$Repository/releases/download/$($release.tag_name)"
$info = Invoke-RestMethod -UseBasicParsing -Uri "$base/fork-release.json"
if ($info.Format -ne 1 -or $info.BaseVersion -ne '0.3' -or $info.Version -notmatch '^\d+\.\d+\.\d+$' -or
    $info.Package -ne "OfflineDAoC-Fork-$($info.Version)-Update.zip" -or $info.RootFolder -ne "OfflineDAoC-Fork-$($info.Version)-Update" -or
    $info.SHA256 -notmatch '^[a-f0-9]{64}$') { throw 'Unexpected release metadata; nothing was changed.' }
Write-Host "Latest fork version: $($info.Version)"

$downloads = Join-Path $root 'update-downloads'
New-Item -ItemType Directory -Path $downloads -Force | Out-Null
$zipPath = Join-Path $downloads $info.Package
$valid = (Test-Path -LiteralPath $zipPath) -and (Get-Item -LiteralPath $zipPath).Length -eq $info.Bytes -and
    (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant() -eq $info.SHA256
if (-not $valid) {
    Write-Host "Downloading $($info.Package) ($([math]::Round($info.Bytes / 1MB, 1)) MB)..."
    Invoke-WebRequest -UseBasicParsing -Uri "$base/$($info.Package)" -OutFile $zipPath
    if ((Get-Item -LiteralPath $zipPath).Length -ne $info.Bytes -or
        (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $info.SHA256) {
        Remove-Item -LiteralPath $zipPath -Force
        throw 'Download verification failed (SHA-256 mismatch). Nothing was changed; run again to retry.'
    }
}
Write-Host 'Download verified (SHA-256).'

$extract = Join-Path $downloads $info.RootFolder
if (Test-Path -LiteralPath $extract) { Remove-Item -LiteralPath $extract -Recurse -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $prefix = $info.RootFolder + '/'
    $downloadsPrefix = $downloads.TrimEnd('\') + '\'
    foreach ($entry in $zip.Entries) {
        $resolved = [IO.Path]::GetFullPath((Join-Path $downloads $entry.FullName))
        if (-not $entry.FullName.StartsWith($prefix, [StringComparison]::Ordinal) -or $entry.FullName.Contains(':') -or
            -not $resolved.StartsWith($downloadsPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe update archive layout; nothing was changed.' }
    }
    foreach ($entry in $zip.Entries) {
        if ($entry.FullName.EndsWith('/')) { continue }
        $resolved = Join-Path $downloads $entry.FullName
        New-Item -ItemType Directory -Path (Split-Path -Parent $resolved) -Force | Out-Null
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $resolved, $true)
    }
}
finally { $zip.Dispose() }

RequireStopped
& (Join-Path $extract 'Apply-OfflineDAoCFork.ps1') -InstallRoot $root
