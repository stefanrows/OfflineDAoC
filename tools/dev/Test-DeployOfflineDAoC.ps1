# Self-check for Deploy/Restore against a temporary fake install tree.
# Never targets D:\Games\OfflineDAoC. Run with:
#   powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\dev\Test-DeployOfflineDAoC.ps1
[CmdletBinding()]
param(
    [string]$ScratchRoot
)
$ErrorActionPreference = 'Stop'
$failed = 0
$passed = 0

function Write-Check {
    param([bool]$Ok, [string]$Name, [string]$Detail = '')
    if ($Ok) {
        $script:passed++
        Write-Output "PASS  $Name"
    }
    else {
        $script:failed++
        if ($Detail) {
            Write-Output "FAIL  $Name :: $Detail"
        }
        else {
            Write-Output "FAIL  $Name"
        }
    }
}

Import-Module -Force -Name (Join-Path $PSScriptRoot 'OfflineDAoC.Deploy.psm1')

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if (-not $ScratchRoot) {
    $ScratchRoot = Join-Path $repoRoot 'artifacts\deploy-selfcheck'
}
if (Test-Path -LiteralPath $ScratchRoot) {
    Remove-Item -LiteralPath $ScratchRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $ScratchRoot -Force | Out-Null

$install = Join-Path $ScratchRoot 'install'
$build = Join-Path $ScratchRoot 'build'
$launcherBuild = Join-Path $ScratchRoot 'launcher-build'

function New-FakeFile {
    param([string]$Path, [string]$Content)
    $parent = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }
    Set-Content -LiteralPath $Path -Value $Content -Encoding ASCII
}

foreach ($dir in @('runtime\server', 'runtime\server\lib', 'runtime\server\config', 'runtime\data', 'runtime\logs')) {
    New-Item -ItemType Directory -Path (Join-Path $install $dir) -Force | Out-Null
}
New-Item -ItemType Directory -Path (Join-Path $build 'lib') -Force | Out-Null
New-Item -ItemType Directory -Path $launcherBuild -Force | Out-Null

$serverFiles = @('GameServer', 'CoreBase', 'CoreDatabase', 'CoreServer')
foreach ($name in $serverFiles) {
    New-FakeFile (Join-Path (Join-Path $install 'runtime\server') ($name + '.dll')) "installed-$name-root"
    New-FakeFile (Join-Path (Join-Path $install 'runtime\server') ($name + '.pdb')) "installed-$name-root-pdb"
    New-FakeFile (Join-Path (Join-Path $install 'runtime\server\lib') ($name + '.dll')) "installed-$name-lib"
    New-FakeFile (Join-Path (Join-Path $install 'runtime\server\lib') ($name + '.pdb')) "installed-$name-lib-pdb"
    New-FakeFile (Join-Path (Join-Path $build 'lib') ($name + '.dll')) "built-$name"
    New-FakeFile (Join-Path (Join-Path $build 'lib') ($name + '.pdb')) "built-$name-pdb"
}

New-FakeFile (Join-Path $install 'runtime\server\lib\Newtonsoft.Json.dll') 'installed-json'
New-FakeFile (Join-Path $build 'lib\Newtonsoft.Json.dll') 'built-json'
New-FakeFile (Join-Path $install 'runtime\server\lib\SQLite.Interop.dll') 'installed-sqlite-native'
New-FakeFile (Join-Path $build 'lib\SQLite.Interop.dll') 'built-sqlite-native'

New-FakeFile (Join-Path $install 'runtime\account.txt') 'account-secret'
New-FakeFile (Join-Path $install 'runtime\data\opendaoc.sqlite3.db') 'save-db'
New-FakeFile (Join-Path $install 'runtime\server\config\serverconfig.xml') '<config/>'
New-FakeFile (Join-Path $install 'runtime\server\bot-goals.json') '{}'
New-FakeFile (Join-Path $install 'runtime\server\rvr-world.json') '{}'
New-FakeFile (Join-Path $install 'runtime\logs\server.log') 'log-line'
New-FakeFile (Join-Path $install 'runtime\OfflineDAoC.dll') 'installed-launcher'
New-FakeFile (Join-Path $install 'runtime\OfflineDAoC.exe') 'installed-launcher-exe'
New-FakeFile (Join-Path $launcherBuild 'OfflineDAoC.dll') 'built-launcher'
New-FakeFile (Join-Path $launcherBuild 'OfflineDAoC.exe') 'built-launcher-exe'

$accountBefore = Get-OfflineDaocFileHash (Join-Path $install 'runtime\account.txt')
$dbBefore = Get-OfflineDaocFileHash (Join-Path $install 'runtime\data\opendaoc.sqlite3.db')
$configBefore = Get-OfflineDaocFileHash (Join-Path $install 'runtime\server\config\serverconfig.xml')
$gsLibBefore = Get-OfflineDaocFileHash (Join-Path $install 'runtime\server\lib\GameServer.dll')
$gsLibBuilt = Get-OfflineDaocFileHash (Join-Path $build 'lib\GameServer.dll')
$jsonInstalled = Get-OfflineDaocFileHash (Join-Path $install 'runtime\server\lib\Newtonsoft.Json.dll')
$jsonBuilt = Get-OfflineDaocFileHash (Join-Path $build 'lib\Newtonsoft.Json.dll')
$nativeInstalled = Get-OfflineDaocFileHash (Join-Path $install 'runtime\server\lib\SQLite.Interop.dll')
$nativeBuilt = Get-OfflineDaocFileHash (Join-Path $build 'lib\SQLite.Interop.dll')
$launcherInstalled = Get-OfflineDaocFileHash (Join-Path $install 'runtime\OfflineDAoC.dll')
$launcherBuilt = Get-OfflineDaocFileHash (Join-Path $launcherBuild 'OfflineDAoC.dll')

# Dry run changes nothing.
$plan = Invoke-OfflineDaocDeploy -InstallRoot $install -ServerBuild $build -LauncherBuild $launcherBuild -PassThru
Write-Check ((Get-OfflineDaocFileHash (Join-Path $install 'runtime\server\lib\GameServer.dll')) -eq $gsLibBefore) 'dry-run leaves server files unchanged'
Write-Check ((Get-OfflineDaocFileHash (Join-Path $install 'runtime\account.txt')) -eq $accountBefore) 'dry-run leaves protected files unchanged'

$replace = @($plan | Where-Object { $_.Action -eq 'replace' })
$third = @($plan | Where-Object { $_.Action -eq 'skip-third-party' })
$native = @($plan | Where-Object { $_.Action -eq 'skip-native' })
Write-Check ($replace.Count -gt 0) 'dry-run lists server replacements' "count=$($replace.Count)"
Write-Check ($third.Count -ge 1) 'dry-run lists third-party skip' "count=$($third.Count)"
Write-Check ($native.Count -ge 1) 'dry-run lists native skip' "count=$($native.Count)"
Write-Check (@($replace | Where-Object { $_.Relative -match 'Newtonsoft|SQLite.Interop' }).Count -eq 0) 'third-party and native are not in the replace set'

$outsideThrown = $false
try {
    Assert-OfflineDaocPathUnderRoot -InstallRoot $install -Path 'C:\Windows\System32\ntdll.dll' | Out-Null
}
catch {
    $outsideThrown = $_.Exception.Message -match 'outside install root'
}
Write-Check $outsideThrown 'outside-root path is refused'

$running = @(Get-OfflineDaocRunningProcesses)
if ($running.Count -gt 0) {
    Write-Check $false 'running-process refuse (close the game first)' (($running | ForEach-Object { $_.Name }) -join ', ')
}
else {
    $stubDir = Join-Path $ScratchRoot 'proc-stub'
    New-Item -ItemType Directory -Path $stubDir -Force | Out-Null
    $stub = Join-Path $stubDir 'CoreServer.exe'
    Copy-Item -LiteralPath (Join-Path $env:SystemRoot 'System32\cmd.exe') -Destination $stub
    $proc = Start-Process -FilePath $stub -ArgumentList '/c ping -n 20 127.0.0.1 >nul' -WindowStyle Hidden -PassThru
    try {
        Start-Sleep -Milliseconds 400
        $refused = $false
        try {
            Invoke-OfflineDaocDeploy -InstallRoot $install -ServerBuild $build | Out-Null
        }
        catch {
            $refused = $_.Exception.Message -match 'Close server'
        }
        Write-Check $refused 'running process is refused'
    }
    finally {
        if ($proc -and -not $proc.HasExited) {
            Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
        }
        Get-Process -Name 'CoreServer' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 200
    }
}

Write-Check ((Get-OfflineDaocFileHash (Join-Path $install 'runtime\server\lib\GameServer.dll')) -eq $gsLibBefore) 'refused deploy did not change files'

Invoke-OfflineDaocDeploy -InstallRoot $install -ServerBuild $build -LauncherBuild $launcherBuild -Apply | Out-Null
Write-Check ((Get-OfflineDaocFileHash (Join-Path $install 'runtime\server\lib\GameServer.dll')) -eq $gsLibBuilt) 'apply replaces GameServer.dll in lib'
Write-Check ((Get-OfflineDaocFileHash (Join-Path $install 'runtime\server\GameServer.dll')) -eq $gsLibBuilt) 'apply replaces GameServer.dll beside CoreServer'
Write-Check ((Get-OfflineDaocFileHash (Join-Path $install 'runtime\server\lib\Newtonsoft.Json.dll')) -eq $jsonInstalled) 'apply does not copy third-party by default'
Write-Check ($jsonInstalled -ne $jsonBuilt) 'third-party hashes actually differ in the fixture'
Write-Check ((Get-OfflineDaocFileHash (Join-Path $install 'runtime\server\lib\SQLite.Interop.dll')) -eq $nativeInstalled) 'apply does not copy native interop'
Write-Check ($nativeInstalled -ne $nativeBuilt) 'native hashes actually differ in the fixture'
Write-Check ((Get-OfflineDaocFileHash (Join-Path $install 'runtime\account.txt')) -eq $accountBefore) 'apply leaves account.txt untouched'
Write-Check ((Get-OfflineDaocFileHash (Join-Path $install 'runtime\data\opendaoc.sqlite3.db')) -eq $dbBefore) 'apply leaves the save database untouched'
Write-Check ((Get-OfflineDaocFileHash (Join-Path $install 'runtime\server\config\serverconfig.xml')) -eq $configBefore) 'apply leaves serverconfig.xml untouched'
Write-Check ((Get-OfflineDaocFileHash (Join-Path $install 'runtime\OfflineDAoC.dll')) -eq $launcherBuilt) 'apply deploys the optional launcher when asked'

$backupParent = $install + '-backups'
$backupRoot = Get-ChildItem -LiteralPath $backupParent -Directory | Sort-Object Name -Descending | Select-Object -First 1
Write-Check (($null -ne $backupRoot) -and (Test-Path -LiteralPath (Join-Path $backupRoot.FullName 'manifest.json'))) 'apply wrote a backup folder with manifest.json'

if ($backupRoot) {
    Invoke-OfflineDaocRestore -InstallRoot $install -Backup $backupRoot.FullName | Out-Null
    Write-Check ((Get-OfflineDaocFileHash (Join-Path $install 'runtime\server\lib\GameServer.dll')) -eq $gsLibBefore) 'restore returns original GameServer.dll hash'
    Write-Check ((Get-OfflineDaocFileHash (Join-Path $install 'runtime\OfflineDAoC.dll')) -eq $launcherInstalled) 'restore returns original launcher hash'
    Write-Check ((Get-OfflineDaocFileHash (Join-Path $install 'runtime\account.txt')) -eq $accountBefore) 'restore leaves protected files untouched'
}

Write-Output ''
Write-Output ("Self-check: {0} passed, {1} failed." -f $passed, $failed)
if ($failed -gt 0) {
    exit 1
}
exit 0
