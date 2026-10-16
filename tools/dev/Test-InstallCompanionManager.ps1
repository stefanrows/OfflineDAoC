# Self-check for Install-CompanionManager.ps1 against scratch copies laid out like an install root.
# Never targets a real installation: it copies the given client files into -ScratchRoot and runs the installer there.
# The installer only copies files and stops nothing; it refuses to run while the game or server runs.
#   powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\dev\Test-InstallCompanionManager.ps1 `
#       -Stage <new stage dir> -Client276 <app dir of a 0.276.0 install> -Backup169 <companion-manager-upgrade backup of 0.169.0> `
#       -RawClient <dir with game.dll and ui\uimain.xml of the raid baseline> -Client282 <app dir of a 0.282.0 install> `
#       -ScratchRoot <new empty dir>
# -Client276, -Client282 and -Backup169 are only read. The 0.169.0 set is rebuilt from the backup (game.dll, uimain.xml,
# custom8 windows). The scratch folder is created and deleted by this script (marker file).
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Stage,
    [Parameter(Mandatory = $true)][string]$Client276,
    [Parameter(Mandatory = $true)][string]$Backup169,
    [string]$RawClient,
    [string]$Client282,
    [Parameter(Mandatory = $true)][string]$ScratchRoot
)
$ErrorActionPreference = 'Stop'
$failed = 0
$passed = 0
$installer = Join-Path $PSScriptRoot 'Install-CompanionManager.ps1'
$shell = (Get-Process -Id $PID).Path

function Write-Check {
    param([bool]$Ok, [string]$Name, [string]$Detail = '')
    if ($Ok) { $script:passed++; Write-Output "PASS  $Name" }
    else { $script:failed++; Write-Output ("FAIL  $Name" + $(if ($Detail) { " :: $Detail" } else { '' })) }
}

function Hash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }

function Snapshot([string]$Root) {
    $base = $Root.TrimEnd('\') + '\'
    @(Get-ChildItem -LiteralPath $Root -Recurse -File | Sort-Object FullName |
        ForEach-Object { $_.FullName.Substring($base.Length) + '=' + (Hash $_.FullName) }) -join "`n"
}

# Runs the installer in a child shell; returns exit code and the combined output text.
function Run-Installer([string[]]$Arguments) {
    $ErrorActionPreference = 'Continue'
    $out = & $shell -NoProfile -ExecutionPolicy Bypass -File $installer @Arguments 2>&1 | ForEach-Object { "$_" }
    [pscustomobject]@{ Exit = $LASTEXITCODE; Text = ($out -join "`n") }
}

function New-Case([string]$Name, [string]$Kind) {
    $root = Join-Path $ScratchRoot "$Name\root"
    $app = Join-Path $root 'runtime\client-opendaoc\app'
    foreach ($skin in 'atlantis', 'isles') { New-Item -ItemType Directory -Path (Join-Path $app "ui\$skin") -Force | Out-Null }
    if ($Kind -eq '276') {
        Copy-Item -LiteralPath (Join-Path $Client276 'game.dll') $app
        Copy-Item -LiteralPath (Join-Path $Client276 'ui\uimain.xml') (Join-Path $app 'ui')
        foreach ($skin in 'atlantis', 'isles') {
            foreach ($n in 6, 7, 8) { Copy-Item -LiteralPath (Join-Path $Client276 "ui\$skin\custom${n}_window.xml") (Join-Path $app "ui\$skin") }
        }
    }
    elseif ($Kind -eq '282') {
        Copy-Item -LiteralPath (Join-Path $Client282 'game.dll') $app
        Copy-Item -LiteralPath (Join-Path $Client282 'ui\uimain.xml') (Join-Path $app 'ui')
        foreach ($skin in 'atlantis', 'isles') {
            foreach ($n in 5, 6, 7, 8) { Copy-Item -LiteralPath (Join-Path $Client282 "ui\$skin\custom${n}_window.xml") (Join-Path $app "ui\$skin") }
        }
    }
    elseif ($Kind -eq '169') {
        Copy-Item -LiteralPath (Join-Path $Backup169 'game.dll') $app
        Copy-Item -LiteralPath (Join-Path $Backup169 'uimain.xml') (Join-Path $app 'ui')
        foreach ($skin in 'atlantis', 'isles') { Copy-Item -LiteralPath (Join-Path $Backup169 "$skin-custom8_window.xml") (Join-Path $app "ui\$skin\custom8_window.xml") }
    }
    else {
        Copy-Item -LiteralPath (Join-Path $RawClient 'game.dll') $app
        Copy-Item -LiteralPath (Join-Path $RawClient 'ui\uimain.xml') (Join-Path $app 'ui')
    }
    return $root
}

$marker = '.offline-daoc-selfcheck'
if (Test-Path -LiteralPath $ScratchRoot) {
    if (-not (Test-Path -LiteralPath (Join-Path $ScratchRoot $marker))) {
        throw "Refusing to delete $ScratchRoot : it has no $marker marker. Pass an empty or new -ScratchRoot."
    }
    Remove-Item -LiteralPath $ScratchRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $ScratchRoot -Force | Out-Null
New-Item -ItemType File -Path (Join-Path $ScratchRoot $marker) | Out-Null

$m = Get-Content -LiteralPath (Join-Path $Stage 'manifest.json') -Raw | ConvertFrom-Json
$startKinds = @('276', '169')
if ($RawClient) { $startKinds += 'raw' }
if ($Client282) { $startKinds += '282' }

foreach ($kind in $startKinds) {
    $case = "round-$kind"
    $root = New-Case $case $kind
    $app = Join-Path $root 'runtime\client-opendaoc\app'
    $before = Snapshot $root

    # Dry run: reports the upgrade (or fresh install), lists custom5, changes nothing.
    $dry = Run-Installer @('-InstallRoot', $root, '-Stage', $Stage)
    Write-Check ($dry.Exit -eq 0) "$kind dry run exits 0" $dry.Text
    Write-Check ($dry.Text -match 'custom5_window\.xml \(OPEN BG\)') "$kind dry run lists custom5"
    Write-Check ($dry.Text -match 'Dry run only') "$kind dry run says dry run"
    if ($kind -ne 'raw') { Write-Check ($dry.Text -match 'Upgrading an earlier Companion Manager build') "$kind dry run reports the upgrade" }
    Write-Check ((Snapshot $root) -eq $before) "$kind dry run changes nothing"

    # Apply.
    $apply = Run-Installer @('-InstallRoot', $root, '-Stage', $Stage, '-Apply')
    Write-Check ($apply.Exit -eq 0) "$kind apply exits 0" $apply.Text
    $ok = (Hash (Join-Path $app 'game.dll')) -eq $m.outputSha256 -and
        (Hash (Join-Path $app 'ui\uimain.xml')) -eq $m.uimainOutputSha256
    foreach ($skin in 'atlantis', 'isles') {
        $ok = $ok -and (Hash (Join-Path $app "ui\$skin\custom5_window.xml")) -eq $m.bgWindowSha256 -and
            (Hash (Join-Path $app "ui\$skin\custom6_window.xml")) -eq $m.gearWindowSha256 -and
            (Hash (Join-Path $app "ui\$skin\custom7_window.xml")) -eq $m.barWindowSha256 -and
            (Hash (Join-Path $app "ui\$skin\custom8_window.xml")) -eq $m.windowSha256
    }
    Write-Check $ok "$kind apply installs the stage files incl. custom5"
    $again = Run-Installer @('-InstallRoot', $root, '-Stage', $Stage)
    Write-Check ($again.Exit -eq 0 -and $again.Text -match 'Already installed') "$kind second run reports already installed"

    # Restore.
    $backups = @(Get-ChildItem -LiteralPath "$root-backups" -Directory)
    Write-Check ($backups.Count -eq 1) "$kind exactly one backup folder"
    $restore = Run-Installer @('-InstallRoot', $root, '-RestoreBackup', $backups[0].FullName)
    Write-Check ($restore.Exit -eq 0) "$kind restore exits 0" $restore.Text
    Write-Check ((Snapshot $root) -eq $before) "$kind restore returns byte-identical files"
}

# A foreign custom5 is refused (dry run and apply), nothing changes.
$root = New-Case 'foreign-custom5' '276'
$app = Join-Path $root 'runtime\client-opendaoc\app'
Set-Content -LiteralPath (Join-Path $app 'ui\isles\custom5_window.xml') -Value 'foreign' -NoNewline
$before = Snapshot $root
foreach ($extra in @(@(), @('-Apply'))) {
    $r = Run-Installer (@('-InstallRoot', $root, '-Stage', $Stage) + $extra)
    Write-Check ($r.Exit -ne 0 -and $r.Text -match 'custom5_window\.xml differs from this stage') "foreign custom5 refused ($($extra -join ' '))" $r.Text
}
Write-Check ((Snapshot $root) -eq $before -and -not (Test-Path -LiteralPath "$root-backups")) 'foreign custom5: nothing changed, no backup'

# 0.282.0 -> 0.283.0: only custom5 changes (game.dll and uimain.xml are the same). Unknown custom5 files are refused.
if ($Client282) {
    $oldBg = Hash (Join-Path $Client282 'ui\atlantis\custom5_window.xml')
    Write-Check ($oldBg -ne $m.bgWindowSha256 -and (Hash (Join-Path $Client282 'game.dll')) -eq $m.outputSha256 -and
        (Hash (Join-Path $Client282 'ui\uimain.xml')) -eq $m.uimainOutputSha256) '282 source: earlier custom5, same game.dll and uimain.xml as the stage'

    # The backup holds the 0.282.0 custom5 files, and the second run after the apply is a no-op.
    $root = New-Case 'backup-282' '282'
    $r = Run-Installer @('-InstallRoot', $root, '-Stage', $Stage, '-Apply')
    $backup = @(Get-ChildItem -LiteralPath "$root-backups" -Directory)[0].FullName
    Write-Check ($r.Exit -eq 0 -and (Hash (Join-Path $backup 'atlantis-custom5_window.xml')) -eq $oldBg -and
        (Hash (Join-Path $backup 'isles-custom5_window.xml')) -eq $oldBg) '282 apply backs up the earlier custom5 files' $r.Text

    # One skin already new, the other old: still an upgrade; restore returns the mixed state.
    $root = New-Case 'mixed-282' '282'
    $app = Join-Path $root 'runtime\client-opendaoc\app'
    Copy-Item -LiteralPath (Join-Path $Stage 'isles\custom5_window.xml') (Join-Path $app 'ui\isles\custom5_window.xml') -Force
    $before = Snapshot $root
    $r = Run-Installer @('-InstallRoot', $root, '-Stage', $Stage, '-Apply')
    Write-Check ($r.Exit -eq 0 -and (Hash (Join-Path $app 'ui\atlantis\custom5_window.xml')) -eq $m.bgWindowSha256) '282 mixed skins upgrade' $r.Text
    $r = Run-Installer @('-InstallRoot', $root, '-RestoreBackup', @(Get-ChildItem -LiteralPath "$root-backups" -Directory)[0].FullName)
    Write-Check ($r.Exit -eq 0 -and (Snapshot $root) -eq $before) '282 mixed skins restore returns byte-identical files' $r.Text

    # An unknown custom5 next to the 0.282.0 set is refused (dry run and apply), nothing changes.
    foreach ($skin in 'atlantis', 'isles') {
        $root = New-Case "foreign-282-$skin" '282'
        $app = Join-Path $root 'runtime\client-opendaoc\app'
        Set-Content -LiteralPath (Join-Path $app "ui\$skin\custom5_window.xml") -Value 'foreign' -NoNewline
        $before = Snapshot $root
        foreach ($extra in @(@(), @('-Apply'))) {
            $r = Run-Installer (@('-InstallRoot', $root, '-Stage', $Stage) + $extra)
            Write-Check ($r.Exit -ne 0 -and $r.Text -match 'UI files do not') "282 foreign $skin custom5 refused ($($extra -join ' '))" $r.Text
        }
        Write-Check ((Snapshot $root) -eq $before -and -not (Test-Path -LiteralPath "$root-backups")) "282 foreign $skin custom5: nothing changed, no backup"
    }

    # A 0.282.0 set with another file changed is refused as well (custom8 here).
    $root = New-Case 'changed-282' '282'
    $app = Join-Path $root 'runtime\client-opendaoc\app'
    [IO.File]::AppendAllText((Join-Path $app 'ui\atlantis\custom8_window.xml'), ' ')
    $before = Snapshot $root
    $r = Run-Installer @('-InstallRoot', $root, '-Stage', $Stage, '-Apply')
    Write-Check ($r.Exit -ne 0 -and (Snapshot $root) -eq $before -and -not (Test-Path -LiteralPath "$root-backups")) '282 with a changed custom8 refused, nothing changed' $r.Text
}

# An unknown game.dll is refused.
$root = New-Case 'unknown-game' '276'
$app = Join-Path $root 'runtime\client-opendaoc\app'
[IO.File]::AppendAllText((Join-Path $app 'game.dll'), 'x')
$before = Snapshot $root
$r = Run-Installer @('-InstallRoot', $root, '-Stage', $Stage, '-Apply')
Write-Check ($r.Exit -ne 0 -and $r.Text -match 'hash mismatch') 'unknown game.dll refused' $r.Text
Write-Check ((Snapshot $root) -eq $before -and -not (Test-Path -LiteralPath "$root-backups")) 'unknown game.dll: nothing changed, no backup'

# A tampered stage file is refused before anything is changed.
$root = New-Case 'rollback' '276'
$tamper = Join-Path $ScratchRoot 'stage-tampered'
Copy-Item -LiteralPath $Stage -Destination $tamper -Recurse
[IO.File]::AppendAllText((Join-Path $tamper 'isles\custom5_window.xml'), ' ')
$before = Snapshot $root
$r = Run-Installer @('-InstallRoot', $root, '-Stage', $tamper, '-Apply')
Write-Check ($r.Exit -ne 0 -and $r.Text -match 'Stage Isles OPEN BG XML hash mismatch') 'tampered stage refused before any change' $r.Text
Write-Check ((Snapshot $root) -eq $before) 'tampered stage: nothing changed'

Remove-Item -LiteralPath $ScratchRoot -Recurse -Force
Write-Output "$passed passed, $failed failed"
if ($failed -gt 0) { exit 1 }
