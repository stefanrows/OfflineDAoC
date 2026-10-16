[CmdletBinding(DefaultParameterSetName = 'Install')]
param(
    [Parameter(Mandatory = $true)][string]$InstallRoot,
    [Parameter(Mandatory = $true, ParameterSetName = 'Install')][string]$Stage,
    [Parameter(ParameterSetName = 'Install')][switch]$Apply,
    [Parameter(Mandatory = $true, ParameterSetName = 'Restore')][string]$RestoreBackup
)

$ErrorActionPreference = 'Stop'

# Installs the staged Companion Manager client files (game.dll, ui/uimain.xml, Atlantis and Isles
# custom8_window.xml, since 0.251.0 the COMPANIONS window's Atlantis and Isles custom7_window.xml, since
# 0.276.0 the COMPANION GEAR window's Atlantis and Isles custom6_window.xml, and since the OPEN BG window the
# Atlantis and Isles custom5_window.xml) built by
# source/server/tools/build_companion_manager_client.py. Dry run by default.
# An installation that already has an earlier manager build (0.32.1, 0.33.0 through 0.169.0, 0.251.0, 0.272.0,
# the 0.273.0 / 0.275.0 companion paperdoll probe, or 0.276.0) is upgraded in place: game.dll, uimain.xml and the window
# files are backed up and replaced, and -RestoreBackup puts the previous files back. An installation with the same
# game.dll and uimain.xml but the 0.282.0 OPEN BG window (0.283.0 redesign) only gets custom5_window.xml replaced.
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
    '88530c0093b285fd38fd6759e464373baa65ebb20473a949bc3b79fccbb41fd3', # 0.33.0 through 0.166.0
    '49095c51e792200daf824f8a00880c70815a0cacbc37f452cf062cffacb3b63d', # 0.169.0 (protocol 3, no COMPANIONS window)
    '73223deffd8455944974b555f5bce5762c43a642c4442b5102194e337e47daf4', # 0.251.0 (first COMPANIONS window)
    'c938e6b50057a4a99ec39211004765dcacb3ffa47880e4ba3fc193add87e612d', # 0.272.0 (COMPANIONS window v2)
    'ff5fb5238fbf473c10503887e66ad1a8679d72342445bfa415e889ded6e5a022', # 0.273.0 companion paperdoll probe
    'f1f5c399ab79a6966b623746059e3e57a22665929a97a5ccff3e6ca093768394', # 0.275.0 companion gear window probe
    'bfefe97d7f7175204ae18c6d23a6958e13819e45155e84738bc5ae9c8d90c264'  # 0.276.0 (COMPANION GEAR window, no OPEN BG window)
)
# Builds whose own COMPANIONS window files (custom7_window.xml) an upgrade replaces after backing them up.
$KnownBarBuilds = @(
    '73223deffd8455944974b555f5bce5762c43a642c4442b5102194e337e47daf4'  # 0.251.0
)
# uimain.xml of the 0.251.0-0.272.0 builds (manager plus COMPANIONS window). The paperdoll probe's uimain.xml is the
# stage's own (same includes), and the manager-only UI is in the stage manifest.
$KnownBarMain = '7d573c315a56bcacae8e8ebc871646654bfe8da5bb1dc745d6023da7f93b9dd9'
# uimain.xml of the 0.276.0 build (manager, COMPANIONS and COMPANION GEAR windows, no OPEN BG window).
$KnownGearMain = 'cef698f354bd3d1c2e7c40f794dac38d0f172a5889d7b8996f1d41c131a1328c'
# The paperdoll probe windows (custom6_window.xml) an upgrade replaces after backing them up.
$KnownGearWindows = @(
    '60d41e0817463f6a17a1e2a053e960ae4d8f4f4b75b4aaf5e3f740d3dd626cd5', # 0.273.0 four-slot probe
    '9a47faa82bd3daaec0c99d26cbd6db12170a01f14af0805c3a384d295e1755ee'  # 0.275.0 full-window probe
)

# OPEN BG windows (custom5_window.xml) of earlier builds that an upgrade replaces after backing them up.
$KnownBgWindows = @(
    '38739f7d407de64e83d361848a688883de9161b6f56b36970f5e2ecdc8273401'  # 0.282.0 (first OPEN BG window; same game.dll and uimain.xml as 0.283.0)
)

function CopyVerified([string]$Source, [string]$Destination, [string]$Expected) {
    RequireHash $Source $Expected 'Source'
    Copy-Item -LiteralPath $Source -Destination $Destination -Force
    RequireHash $Destination $Expected 'Installed'
}

# Puts an added window file (OPEN BG custom5, COMPANIONS custom7, COMPANION GEAR custom6) back: the earlier copy when there was
# one, otherwise nothing (the file is removed only while it still holds the window this script installed).
function RestoreAddedWindow([string]$Path, [string]$PreviousHash, [string]$BackupFile, [string]$InstalledHash) {
    if ($PreviousHash) {
        CopyVerified $BackupFile $Path $PreviousHash
    }
    elseif ((Hash $Path) -eq $InstalledHash) {
        Remove-Item -LiteralPath $Path -Force
    }
}

$root = [IO.Path]::GetFullPath($InstallRoot).TrimEnd('\')
$client = Join-Path $root 'runtime\client-opendaoc\app'
if (-not (Test-Path -LiteralPath $client -PathType Container)) { throw "Client folder not found: $client" }
RequireStopped

$game = Join-Path $client 'game.dll'
$main = Join-Path $client 'ui\uimain.xml'
$atlantis = Join-Path $client 'ui\atlantis\custom8_window.xml'
$isles = Join-Path $client 'ui\isles\custom8_window.xml'
$atlantisBar = Join-Path $client 'ui\atlantis\custom7_window.xml'
$islesBar = Join-Path $client 'ui\isles\custom7_window.xml'
$atlantisGear = Join-Path $client 'ui\atlantis\custom6_window.xml'
$islesGear = Join-Path $client 'ui\isles\custom6_window.xml'
$atlantisBg = Join-Path $client 'ui\atlantis\custom5_window.xml'
$islesBg = Join-Path $client 'ui\isles\custom5_window.xml'

if ($PSCmdlet.ParameterSetName -eq 'Restore') {
    $backup = [IO.Path]::GetFullPath($RestoreBackup)
    $manifestPath = Join-Path $backup 'manifest.json'
    if (-not (Test-Path -LiteralPath $manifestPath)) { throw "Backup manifest not found: $manifestPath" }
    $record = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($record.InstallRoot -ne $root) { throw 'Backup is for a different installation.' }
    if ($record.InstallKind -eq 'upgrade') {
        # An in-place upgrade: put back the previous game.dll and window files, and uimain.xml when the
        # backup recorded it (older upgrades left uimain.xml alone).
        $previous = @(@($game, 'game.dll', $record.PreviousGameSha256, $record.OutputGameSha256),
                      @($atlantis, 'atlantis-custom8_window.xml', $record.PreviousAtlantisSha256, $record.WindowSha256),
                      @($isles, 'isles-custom8_window.xml', $record.PreviousIslesSha256, $record.WindowSha256))
        if ($record.PreviousMainSha256) {
            $previous += , @($main, 'uimain.xml', $record.PreviousMainSha256, $record.OutputMainSha256)
        }
        foreach ($item in $previous) {
            RequireHash (Join-Path $backup $item[1]) $item[2] 'Backup file'
            $current = Hash $item[0]
            if ($current -ne $item[3] -and $current -ne $item[2]) {
                throw "Installed file changed since the Companion Manager upgrade: $($item[0]) ($current). Restore refused."
            }
        }
        # Added windows: path, backup name, previous hash, installed hash.
        $added = @()
        if ($record.BarWindowSha256) {
            $added += , @($atlantisBar, 'atlantis-custom7_window.xml', $record.PreviousBarAtlantisSha256, $record.BarWindowSha256)
            $added += , @($islesBar, 'isles-custom7_window.xml', $record.PreviousBarIslesSha256, $record.BarWindowSha256)
        }
        if ($record.GearWindowSha256) {
            $added += , @($atlantisGear, 'atlantis-custom6_window.xml', $record.PreviousGearAtlantisSha256, $record.GearWindowSha256)
            $added += , @($islesGear, 'isles-custom6_window.xml', $record.PreviousGearIslesSha256, $record.GearWindowSha256)
        }
        if ($record.BgWindowSha256) {
            $added += , @($atlantisBg, 'atlantis-custom5_window.xml', $record.PreviousBgAtlantisSha256, $record.BgWindowSha256)
            $added += , @($islesBg, 'isles-custom5_window.xml', $record.PreviousBgIslesSha256, $record.BgWindowSha256)
        }
        foreach ($item in $added) {
            $current = Hash $item[0]
            if ($current -and $current -ne $item[3] -and $current -ne $item[2]) {
                throw "Installed file changed since the Companion Manager upgrade: $($item[0]) ($current). Restore refused."
            }
            if ($item[2]) { RequireHash (Join-Path $backup $item[1]) $item[2] 'Backup file' }
        }
        foreach ($item in $previous) { CopyVerified (Join-Path $backup $item[1]) $item[0] $item[2] }
        foreach ($item in $added) { RestoreAddedWindow $item[0] $item[2] (Join-Path $backup $item[1]) $item[3] }
        Write-Host "Companion Manager client files restored to the earlier manager build from $backup; backup retained."
        return
    }
    $oldGame = Join-Path $backup 'game.dll'
    $oldMain = Join-Path $backup 'uimain.xml'
    RequireHash $oldGame $record.BaselineGameSha256 'Backup game.dll'
    RequireHash $oldMain $record.BaselineMainSha256 'Backup uimain.xml'
    $pairs = @(@($game, $record.OutputGameSha256, $record.BaselineGameSha256),
               @($main, $record.OutputMainSha256, $record.BaselineMainSha256),
               @($atlantis, $record.WindowSha256, $null),
               @($isles, $record.WindowSha256, $null))
    if ($record.BarWindowSha256) {
        $pairs += , @($atlantisBar, $record.BarWindowSha256, $null)
        $pairs += , @($islesBar, $record.BarWindowSha256, $null)
    }
    if ($record.GearWindowSha256) {
        $pairs += , @($atlantisGear, $record.GearWindowSha256, $null)
        $pairs += , @($islesGear, $record.GearWindowSha256, $null)
    }
    if ($record.BgWindowSha256) {
        $pairs += , @($atlantisBg, $record.BgWindowSha256, $null)
        $pairs += , @($islesBg, $record.BgWindowSha256, $null)
    }
    foreach ($pair in $pairs) {
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
    if ($record.BarWindowSha256) {
        foreach ($path in @($atlantisBar, $islesBar)) { RestoreAddedWindow $path $null $null $record.BarWindowSha256 }
    }
    if ($record.GearWindowSha256) {
        foreach ($path in @($atlantisGear, $islesGear)) { RestoreAddedWindow $path $null $null $record.GearWindowSha256 }
    }
    if ($record.BgWindowSha256) {
        foreach ($path in @($atlantisBg, $islesBg)) { RestoreAddedWindow $path $null $null $record.BgWindowSha256 }
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
if (-not $manifest.barWindowSha256 -or -not $manifest.uimainManagerOnlySha256) {
    throw 'Stage predates the COMPANIONS window; rebuild it with build_companion_manager_client.py.'
}
if (-not $manifest.gearWindowSha256 -or -not $manifest.uimainBarSha256) {
    throw 'Stage predates the COMPANION GEAR window; rebuild it with build_companion_manager_client.py.'
}
if (-not $manifest.bgWindowSha256 -or -not $manifest.uimainGearSha256) {
    throw 'Stage predates the OPEN BG window; rebuild it with build_companion_manager_client.py.'
}
$stageGame = Join-Path $stageRoot 'game.dll'
$stageMain = Join-Path $stageRoot 'uimain.xml'
$stageAtlantis = Join-Path $stageRoot 'atlantis\custom8_window.xml'
$stageIsles = Join-Path $stageRoot 'isles\custom8_window.xml'
$stageAtlantisBar = Join-Path $stageRoot 'atlantis\custom7_window.xml'
$stageIslesBar = Join-Path $stageRoot 'isles\custom7_window.xml'
$stageAtlantisGear = Join-Path $stageRoot 'atlantis\custom6_window.xml'
$stageIslesGear = Join-Path $stageRoot 'isles\custom6_window.xml'
$stageAtlantisBg = Join-Path $stageRoot 'atlantis\custom5_window.xml'
$stageIslesBg = Join-Path $stageRoot 'isles\custom5_window.xml'

# The stage's own game.dll is already installed: nothing to do, refresh an earlier OPEN BG window, or refuse a half-installed set.
$installedGame = Hash $game
$upgrade = $KnownManagerBuilds -contains $installedGame
if ($installedGame -eq $manifest.outputSha256) {
    $restComplete = (Hash $main) -eq $manifest.uimainOutputSha256 -and
        (Hash $atlantis) -eq $manifest.windowSha256 -and (Hash $isles) -eq $manifest.windowSha256 -and
        (Hash $atlantisBar) -eq $manifest.barWindowSha256 -and (Hash $islesBar) -eq $manifest.barWindowSha256 -and
        (Hash $atlantisGear) -eq $manifest.gearWindowSha256 -and (Hash $islesGear) -eq $manifest.gearWindowSha256
    $complete = $restComplete -and
        (Hash $atlantisBg) -eq $manifest.bgWindowSha256 -and (Hash $islesBg) -eq $manifest.bgWindowSha256
    if ($complete) {
        Write-Host "Already installed: the manager build $($manifest.outputSha256) and its UI files match this stage. Nothing to do."
        return
    }
    # The same game.dll and uimain.xml with an earlier OPEN BG window (0.282.0): only custom5_window.xml is replaced.
    $bgAllowed = @($manifest.bgWindowSha256) + $KnownBgWindows
    if ($restComplete -and $bgAllowed -contains (Hash $atlantisBg) -and $bgAllowed -contains (Hash $islesBg)) {
        $upgrade = $true
    }
    else {
        throw 'game.dll already matches this stage, but the UI files do not. Nothing was changed; inspect the installation.'
    }
}

if ($upgrade) {
    $installedMain = Hash $main
    if (@($manifest.uimainOutputSha256, $manifest.uimainManagerOnlySha256, $manifest.uimainBarSha256, $manifest.uimainGearSha256, $KnownGearMain, $KnownBarMain) -notcontains $installedMain) {
        throw "Installed uimain.xml is not a known Companion Manager UI: $main ($installedMain)"
    }
    foreach ($path in @($atlantis, $isles)) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Earlier manager window file missing: $path" }
    }
    # An installed window from an earlier COMPANIONS build is backed up and replaced; any other stray file is refused.
    if ($KnownBarBuilds -notcontains $installedGame) {
        foreach ($path in @($atlantisBar, $islesBar)) {
            $existing = Hash $path
            if ($existing -and $existing -ne $manifest.barWindowSha256) {
                throw "A COMPANIONS window file differs from this stage: $path ($existing). Refused."
            }
        }
    }
    # A paperdoll probe window is backed up and replaced; any other stray custom6 file is refused.
    foreach ($path in @($atlantisGear, $islesGear)) {
        $existing = Hash $path
        if ($existing -and $existing -ne $manifest.gearWindowSha256 -and $KnownGearWindows -notcontains $existing) {
            throw "A custom6_window.xml differs from this stage and from the known probe windows: $path ($existing). Refused."
        }
    }
    # This stage's own OPEN BG window or an earlier one (backed up and replaced) may already exist; any other custom5 file is refused.
    foreach ($path in @($atlantisBg, $islesBg)) {
        $existing = Hash $path
        if ($existing -and $existing -ne $manifest.bgWindowSha256 -and $KnownBgWindows -notcontains $existing) {
            throw "A custom5_window.xml differs from this stage and from the known earlier OPEN BG window: $path ($existing). Refused."
        }
    }
}
else {
    RequireHash $game $manifest.baselineSha256 'Installed game.dll'
    RequireHash $main $manifest.uimainBaselineSha256 'Installed uimain.xml'
    if ((Test-Path -LiteralPath $atlantis) -or (Test-Path -LiteralPath $isles)) {
        throw 'Custom8 XML already exists in the installation.'
    }
    if ((Test-Path -LiteralPath $atlantisBar) -or (Test-Path -LiteralPath $islesBar)) {
        throw 'Custom7 XML already exists in the installation.'
    }
    if ((Test-Path -LiteralPath $atlantisGear) -or (Test-Path -LiteralPath $islesGear)) {
        throw 'Custom6 XML already exists in the installation.'
    }
    if ((Test-Path -LiteralPath $atlantisBg) -or (Test-Path -LiteralPath $islesBg)) {
        throw 'Custom5 XML already exists in the installation.'
    }
}
RequireHash $stageGame $manifest.outputSha256 'Stage game.dll'
RequireHash $stageMain $manifest.uimainOutputSha256 'Stage uimain.xml'
RequireHash $stageAtlantis $manifest.windowSha256 'Stage Atlantis XML'
RequireHash $stageIsles $manifest.windowSha256 'Stage Isles XML'
RequireHash $stageAtlantisBar $manifest.barWindowSha256 'Stage Atlantis COMPANIONS XML'
RequireHash $stageIslesBar $manifest.barWindowSha256 'Stage Isles COMPANIONS XML'
RequireHash $stageAtlantisGear $manifest.gearWindowSha256 'Stage Atlantis COMPANION GEAR XML'
RequireHash $stageIslesGear $manifest.gearWindowSha256 'Stage Isles COMPANION GEAR XML'
RequireHash $stageAtlantisBg $manifest.bgWindowSha256 'Stage Atlantis OPEN BG XML'
RequireHash $stageIslesBg $manifest.bgWindowSha256 'Stage Isles OPEN BG XML'

if ($upgrade) { Write-Host "Upgrading an earlier Companion Manager build (installed game.dll $installedGame)." }
else { Write-Host "Verified baseline game.dll: $($manifest.baselineSha256)" }
Write-Host "Staged manager game.dll:   $($manifest.outputSha256)"
Write-Host 'Files: game.dll, ui/uimain.xml, Atlantis and Isles custom8_window.xml, custom7_window.xml (COMPANIONS) and custom6_window.xml (COMPANION GEAR) and custom5_window.xml (OPEN BG)'
if (-not $Apply) { Write-Host 'Dry run only. Pass -Apply to install.'; return }

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
if ($upgrade) {
    $backup = "$root-backups\companion-manager-upgrade-$stamp"
    if (Test-Path -LiteralPath $backup) { throw "Backup already exists: $backup" }
    New-Item -ItemType Directory -Path $backup -Force | Out-Null
    $previousGame = Hash $game
    $previousAtlantis = Hash $atlantis
    $previousIsles = Hash $isles
    $previousMain = Hash $main
    $previousBarAtlantis = Hash $atlantisBar
    $previousBarIsles = Hash $islesBar
    $previousGearAtlantis = Hash $atlantisGear
    $previousGearIsles = Hash $islesGear
    $previousBgAtlantis = Hash $atlantisBg
    $previousBgIsles = Hash $islesBg
    CopyVerified $game (Join-Path $backup 'game.dll') $previousGame
    CopyVerified $atlantis (Join-Path $backup 'atlantis-custom8_window.xml') $previousAtlantis
    CopyVerified $isles (Join-Path $backup 'isles-custom8_window.xml') $previousIsles
    CopyVerified $main (Join-Path $backup 'uimain.xml') $previousMain
    if ($previousBarAtlantis) { CopyVerified $atlantisBar (Join-Path $backup 'atlantis-custom7_window.xml') $previousBarAtlantis }
    if ($previousBarIsles) { CopyVerified $islesBar (Join-Path $backup 'isles-custom7_window.xml') $previousBarIsles }
    if ($previousGearAtlantis) { CopyVerified $atlantisGear (Join-Path $backup 'atlantis-custom6_window.xml') $previousGearAtlantis }
    if ($previousGearIsles) { CopyVerified $islesGear (Join-Path $backup 'isles-custom6_window.xml') $previousGearIsles }
    if ($previousBgAtlantis) { CopyVerified $atlantisBg (Join-Path $backup 'atlantis-custom5_window.xml') $previousBgAtlantis }
    if ($previousBgIsles) { CopyVerified $islesBg (Join-Path $backup 'isles-custom5_window.xml') $previousBgIsles }
    [pscustomobject]@{
        InstallRoot = $root
        InstallKind = 'upgrade'
        PreviousGameSha256 = $previousGame
        PreviousAtlantisSha256 = $previousAtlantis
        PreviousIslesSha256 = $previousIsles
        PreviousMainSha256 = $previousMain
        PreviousBarAtlantisSha256 = $previousBarAtlantis
        PreviousBarIslesSha256 = $previousBarIsles
        PreviousGearAtlantisSha256 = $previousGearAtlantis
        PreviousGearIslesSha256 = $previousGearIsles
        PreviousBgAtlantisSha256 = $previousBgAtlantis
        PreviousBgIslesSha256 = $previousBgIsles
        OutputGameSha256 = $manifest.outputSha256
        WindowSha256 = $manifest.windowSha256
        OutputMainSha256 = $manifest.uimainOutputSha256
        BarWindowSha256 = $manifest.barWindowSha256
        GearWindowSha256 = $manifest.gearWindowSha256
        BgWindowSha256 = $manifest.bgWindowSha256
        Stage = $stageRoot
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $backup 'manifest.json') -Encoding UTF8
    try {
        RequireStopped
        CopyVerified $stageGame $game $manifest.outputSha256
        CopyVerified $stageMain $main $manifest.uimainOutputSha256
        CopyVerified $stageAtlantis $atlantis $manifest.windowSha256
        CopyVerified $stageIsles $isles $manifest.windowSha256
        CopyVerified $stageAtlantisBar $atlantisBar $manifest.barWindowSha256
        CopyVerified $stageIslesBar $islesBar $manifest.barWindowSha256
        CopyVerified $stageAtlantisGear $atlantisGear $manifest.gearWindowSha256
        CopyVerified $stageIslesGear $islesGear $manifest.gearWindowSha256
        CopyVerified $stageAtlantisBg $atlantisBg $manifest.bgWindowSha256
        CopyVerified $stageIslesBg $islesBg $manifest.bgWindowSha256
        Write-Host "Companion Manager client upgraded. Backup: $backup"
        Write-Host "Restore the earlier build with: -InstallRoot '$root' -RestoreBackup '$backup'"
    }
    catch {
        $reason = $_.Exception.Message
        try {
            RequireStopped
            CopyVerified (Join-Path $backup 'game.dll') $game $previousGame
            CopyVerified (Join-Path $backup 'uimain.xml') $main $previousMain
            CopyVerified (Join-Path $backup 'atlantis-custom8_window.xml') $atlantis $previousAtlantis
            CopyVerified (Join-Path $backup 'isles-custom8_window.xml') $isles $previousIsles
            RestoreAddedWindow $atlantisBar $previousBarAtlantis (Join-Path $backup 'atlantis-custom7_window.xml') $manifest.barWindowSha256
            RestoreAddedWindow $islesBar $previousBarIsles (Join-Path $backup 'isles-custom7_window.xml') $manifest.barWindowSha256
            RestoreAddedWindow $atlantisGear $previousGearAtlantis (Join-Path $backup 'atlantis-custom6_window.xml') $manifest.gearWindowSha256
            RestoreAddedWindow $islesGear $previousGearIsles (Join-Path $backup 'isles-custom6_window.xml') $manifest.gearWindowSha256
            RestoreAddedWindow $atlantisBg $previousBgAtlantis (Join-Path $backup 'atlantis-custom5_window.xml') $manifest.bgWindowSha256
            RestoreAddedWindow $islesBg $previousBgIsles (Join-Path $backup 'isles-custom5_window.xml') $manifest.bgWindowSha256
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
    InstallKind = 'fresh'
    BaselineGameSha256 = $manifest.baselineSha256
    OutputGameSha256 = $manifest.outputSha256
    BaselineMainSha256 = $manifest.uimainBaselineSha256
    OutputMainSha256 = $manifest.uimainOutputSha256
    WindowSha256 = $manifest.windowSha256
    BarWindowSha256 = $manifest.barWindowSha256
    GearWindowSha256 = $manifest.gearWindowSha256
    BgWindowSha256 = $manifest.bgWindowSha256
    Stage = $stageRoot
}
$record | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $backup 'manifest.json') -Encoding UTF8
try {
    RequireStopped
    CopyVerified $stageGame $game $manifest.outputSha256
    CopyVerified $stageMain $main $manifest.uimainOutputSha256
    CopyVerified $stageAtlantis $atlantis $manifest.windowSha256
    CopyVerified $stageIsles $isles $manifest.windowSha256
    CopyVerified $stageAtlantisBar $atlantisBar $manifest.barWindowSha256
    CopyVerified $stageIslesBar $islesBar $manifest.barWindowSha256
    CopyVerified $stageAtlantisGear $atlantisGear $manifest.gearWindowSha256
    CopyVerified $stageIslesGear $islesGear $manifest.gearWindowSha256
    CopyVerified $stageAtlantisBg $atlantisBg $manifest.bgWindowSha256
    CopyVerified $stageIslesBg $islesBg $manifest.bgWindowSha256
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
        foreach ($path in @($atlantisBar, $islesBar)) {
            if ((Hash $path) -eq $manifest.barWindowSha256) { Remove-Item -LiteralPath $path -Force }
        }
        foreach ($path in @($atlantisGear, $islesGear)) {
            if ((Hash $path) -eq $manifest.gearWindowSha256) { Remove-Item -LiteralPath $path -Force }
        }
        foreach ($path in @($atlantisBg, $islesBg)) {
            if ((Hash $path) -eq $manifest.bgWindowSha256) { Remove-Item -LiteralPath $path -Force }
        }
    }
    catch { throw "Install failed ($reason); automatic rollback failed ($($_.Exception.Message)). Backup: $backup" }
    throw "Install failed and was rolled back: $reason. Backup: $backup"
}
