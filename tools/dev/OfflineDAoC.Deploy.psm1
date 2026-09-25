$script:BlockedProcessNames = @('CoreServer', 'OfflineDAoC', 'game', 'game.dll', 'camelot', 'connect')
$script:ServerAssemblyNames = @('GameServer', 'CoreBase', 'CoreDatabase', 'CoreServer')
$script:LauncherFileNames = @(
    'OfflineDAoC.dll',
    'OfflineDAoC.exe',
    'OfflineDAoC.pdb',
    'OfflineDAoC.deps.json',
    'OfflineDAoC.runtimeconfig.json',
    'Join Friend.cmd'
)
$script:NativeFileNames = @(
    'SQLite.Interop.dll',
    'Detour.dll',
    'e_sqlite3.dll',
    'sni.dll'
)
$script:ProtectedRelativePaths = @(
    'runtime\account.txt',
    'runtime\data\opendaoc.sqlite3.db',
    'runtime\data\opendaoc.sqlite3.db-wal',
    'runtime\data\opendaoc.sqlite3.db-shm',
    'runtime\server\config\serverconfig.xml',
    'runtime\server\bot-goals.json',
    'runtime\server\rvr-world.json'
)
$script:InstallTargetDirs = @('runtime\server', 'runtime\server\lib')

function Get-OfflineDaocRunningProcesses {
    param([string[]]$Names = $script:BlockedProcessNames)
    Get-Process -Name $Names -ErrorAction SilentlyContinue
}

function Assert-OfflineDaocStopped {
    $running = @(Get-OfflineDaocRunningProcesses)
    if ($running.Count -gt 0) {
        $list = ($running | ForEach-Object { $_.Name + ' (PID ' + $_.Id + ')' }) -join ', '
        throw "Close server, client and launcher first: $list"
    }
}

function Resolve-OfflineDaocInstallRoot {
    param([Parameter(Mandatory = $true)][string]$InstallRoot)
    if (-not (Test-Path -LiteralPath $InstallRoot)) {
        throw "Install root not found: $InstallRoot"
    }
    return ([IO.Path]::GetFullPath($InstallRoot)).TrimEnd('\')
}

function Assert-OfflineDaocPathUnderRoot {
    param(
        [Parameter(Mandatory = $true)][string]$InstallRoot,
        [Parameter(Mandatory = $true)][string]$Path
    )
    $root = ([IO.Path]::GetFullPath($InstallRoot)).TrimEnd('\')
    $full = [IO.Path]::GetFullPath($Path)
    $prefix = $root + '\'
    if ($full -ne $root -and -not $full.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path is outside install root: $full"
    }
    return $full
}

function Get-OfflineDaocFileHash {
    param([string]$Path)
    if (-not $Path -or -not (Test-Path -LiteralPath $Path)) {
        return $null
    }
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

function Find-OfflineDaocBuildFile {
    param(
        [Parameter(Mandatory = $true)][string]$BuildRoot,
        [Parameter(Mandatory = $true)][string]$FileName
    )
    if (-not (Test-Path -LiteralPath $BuildRoot)) {
        return $null
    }
    $direct = Join-Path $BuildRoot $FileName
    if (Test-Path -LiteralPath $direct) {
        return [IO.Path]::GetFullPath($direct)
    }
    $lib = Join-Path (Join-Path $BuildRoot 'lib') $FileName
    if (Test-Path -LiteralPath $lib) {
        return [IO.Path]::GetFullPath($lib)
    }
    # CoreServer publishes beside Release\; other assemblies go to Release\lib\.
    if ([IO.Path]::GetFileName($BuildRoot) -ieq 'lib') {
        $sibling = Join-Path (Split-Path -Parent $BuildRoot) $FileName
        if (Test-Path -LiteralPath $sibling) {
            return [IO.Path]::GetFullPath($sibling)
        }
    }
    $matches = @(Get-ChildItem -LiteralPath $BuildRoot -Recurse -Filter $FileName -File -ErrorAction SilentlyContinue |
        Where-Object {
            $_.FullName -notmatch '\\obj\\' -and
            $_.FullName -notmatch '\\Tests\\' -and
            $_.FullName -notmatch '\\runtimes\\'
        } |
        Sort-Object @{ Expression = { if ($_.DirectoryName -match '\\lib$') { 0 } else { 1 } } }, @{ Expression = { $_.FullName.Length } })
    if ($matches.Count -gt 0) {
        return $matches[0].FullName
    }
    return $null
}

function Get-OfflineDaocProtectedHashes {
    param([Parameter(Mandatory = $true)][string]$InstallRoot)
    $items = @()
    foreach ($relative in $script:ProtectedRelativePaths) {
        $path = Join-Path $InstallRoot $relative
        Assert-OfflineDaocPathUnderRoot -InstallRoot $InstallRoot -Path $path | Out-Null
        if (Test-Path -LiteralPath $path) {
            $items += [pscustomobject]@{
                Relative = $relative
                Path     = $path
                Hash     = Get-OfflineDaocFileHash $path
            }
        }
    }
    return $items
}

function Assert-OfflineDaocProtectedUnchanged {
    param(
        [Parameter(Mandatory = $true)][string]$InstallRoot,
        [Parameter(Mandatory = $true)]$Before
    )
    $after = @(Get-OfflineDaocProtectedHashes -InstallRoot $InstallRoot)
    foreach ($item in $Before) {
        $now = $after | Where-Object { $_.Relative -eq $item.Relative } | Select-Object -First 1
        if (-not $now) {
            throw "Protected file disappeared: $($item.Relative)"
        }
        if ($now.Hash -ne $item.Hash) {
            throw "Protected file changed: $($item.Relative)"
        }
    }
}

function New-OfflineDaocDeployEntry {
    param(
        [string]$Relative,
        [string]$Target,
        [string]$Source,
        [string]$Kind,
        [string]$InstalledHash,
        [string]$NewHash,
        [switch]$IncludeThirdParty
    )
    $action = 'unchanged'
    if ($Kind -eq 'native') {
        $action = 'skip-native'
    }
    elseif ($Kind -eq 'third-party-new') {
        # Present in the build but in neither install folder; never added automatically.
        $action = 'missing-in-install'
    }
    elseif ($Kind -eq 'third-party') {
        if ($InstalledHash -and $NewHash -and $InstalledHash -ne $NewHash) {
            if ($IncludeThirdParty) {
                $action = 'replace'
            }
            else {
                $action = 'skip-third-party'
            }
        }
        else {
            $action = 'unchanged'
        }
    }
    elseif (-not $Source) {
        $action = 'error-missing-source'
    }
    elseif ($InstalledHash -ne $NewHash) {
        $action = 'replace'
    }
    return [pscustomobject]@{
        Relative      = $Relative
        Target        = $Target
        Source        = $Source
        Kind          = $Kind
        InstalledHash = $InstalledHash
        NewHash       = $NewHash
        Action        = $action
    }
}

function Get-OfflineDaocDeployPlan {
    param(
        [Parameter(Mandatory = $true)][string]$InstallRoot,
        [Parameter(Mandatory = $true)][string]$ServerBuild,
        [string]$LauncherBuild,
        [switch]$IncludeThirdParty
    )
    $root = Resolve-OfflineDaocInstallRoot -InstallRoot $InstallRoot
    $buildRoot = [IO.Path]::GetFullPath($ServerBuild)
    if (-not (Test-Path -LiteralPath $buildRoot)) {
        throw "Server build folder not found: $ServerBuild"
    }

    $entries = @()
    foreach ($name in $script:ServerAssemblyNames) {
        foreach ($ext in @('.dll', '.pdb')) {
            $fileName = $name + $ext
            $source = Find-OfflineDaocBuildFile -BuildRoot $buildRoot -FileName $fileName
            foreach ($dir in $script:InstallTargetDirs) {
                $relative = Join-Path $dir $fileName
                $target = Join-Path $root $relative
                Assert-OfflineDaocPathUnderRoot -InstallRoot $root -Path $target | Out-Null
                if (-not (Test-Path -LiteralPath $target)) {
                    continue
                }
                if ($ext -eq '.dll' -and -not $source) {
                    throw "Build is missing $fileName required for $relative"
                }
                if (-not $source) {
                    Write-Warning "Build has no $fileName; installed $relative will not match the deployed DLL."
                    continue
                }
                $entries += New-OfflineDaocDeployEntry -Relative $relative -Target $target -Source $source `
                    -Kind 'server' -InstalledHash (Get-OfflineDaocFileHash $target) -NewHash (Get-OfflineDaocFileHash $source)
            }
        }
    }

    $seenThirdParty = @{}
    $searchRoots = @($buildRoot)
    $libRoot = Join-Path $buildRoot 'lib'
    if (Test-Path -LiteralPath $libRoot) {
        $searchRoots += $libRoot
    }
    foreach ($search in $searchRoots) {
        Get-ChildItem -LiteralPath $search -Filter '*.dll' -File -ErrorAction SilentlyContinue | ForEach-Object {
            $fileName = $_.Name
            $base = [IO.Path]::GetFileNameWithoutExtension($fileName)
            if ($script:ServerAssemblyNames -contains $base) {
                return
            }
            if ($seenThirdParty.ContainsKey($fileName)) {
                return
            }
            $seenThirdParty[$fileName] = $true
            $kind = 'third-party'
            if ($script:NativeFileNames -contains $fileName) {
                $kind = 'native'
            }
            $found = $false
            foreach ($dir in $script:InstallTargetDirs) {
                $relative = Join-Path $dir $fileName
                $target = Join-Path $root $relative
                Assert-OfflineDaocPathUnderRoot -InstallRoot $root -Path $target | Out-Null
                if (-not (Test-Path -LiteralPath $target)) {
                    continue
                }
                $found = $true
                $entries += New-OfflineDaocDeployEntry -Relative $relative -Target $target -Source $_.FullName `
                    -Kind $kind -InstalledHash (Get-OfflineDaocFileHash $target) -NewHash (Get-OfflineDaocFileHash $_.FullName) `
                    -IncludeThirdParty:$IncludeThirdParty
            }
            if (-not $found -and $kind -eq 'third-party') {
                $entries += New-OfflineDaocDeployEntry -Relative (Join-Path 'runtime\server' $fileName) -Target $null `
                    -Source $_.FullName -Kind 'third-party-new' -InstalledHash $null -NewHash (Get-OfflineDaocFileHash $_.FullName)
            }
        }
    }

    if ($LauncherBuild) {
        $launcherRoot = [IO.Path]::GetFullPath($LauncherBuild)
        if (-not (Test-Path -LiteralPath $launcherRoot)) {
            throw "Launcher build folder not found: $LauncherBuild"
        }
        foreach ($fileName in $script:LauncherFileNames) {
            $source = Find-OfflineDaocBuildFile -BuildRoot $launcherRoot -FileName $fileName
            $relative = Join-Path 'runtime' $fileName
            $target = Join-Path $root $relative
            Assert-OfflineDaocPathUnderRoot -InstallRoot $root -Path $target | Out-Null
            if (-not (Test-Path -LiteralPath $target) -and $fileName -ne 'Join Friend.cmd') {
                continue
            }
            if (-not $source) {
                throw "Launcher build is missing $fileName required for $relative"
            }
            $entries += New-OfflineDaocDeployEntry -Relative $relative -Target $target -Source $source `
                -Kind 'launcher' -InstalledHash (Get-OfflineDaocFileHash $target) -NewHash (Get-OfflineDaocFileHash $source)
        }
    }

    if ($entries.Count -eq 0) {
        throw "No matching install files found under $root for this build."
    }
    return $entries
}

function Copy-OfflineDaocVerified {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination,
        [Parameter(Mandatory = $true)][string]$ExpectedHash
    )
    $parent = Split-Path -Parent $Destination
    if (-not (Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }
    $temporary = $Destination + '.offline-daoc-new'
    if (Test-Path -LiteralPath $temporary) {
        throw "Unexpected staging file: $temporary"
    }
    Copy-Item -LiteralPath $Source -Destination $temporary
    $staged = Get-OfflineDaocFileHash $temporary
    if ($staged -ne $ExpectedHash) {
        Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue
        throw "Staging hash mismatch for $Destination"
    }
    if (Test-Path -LiteralPath $Destination) {
        Remove-Item -LiteralPath $Destination -Force
    }
    Move-Item -LiteralPath $temporary -Destination $Destination
    $installed = Get-OfflineDaocFileHash $Destination
    if ($installed -ne $ExpectedHash) {
        throw "Installed hash mismatch for $Destination"
    }
}

function Restore-OfflineDaocEntriesFromBackup {
    # Rolls back only entries whose backup copy was verified. Returns the
    # relative paths that could not be put back to their original hash.
    param(
        [Parameter(Mandatory = $true)][string]$BackupRoot,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()]$Entries
    )
    $failures = @()
    foreach ($entry in $Entries) {
        try {
            $temporary = $entry.Target + '.offline-daoc-new'
            if (Test-Path -LiteralPath $temporary) {
                Remove-Item -LiteralPath $temporary -Force
            }
            if (-not $entry.InstalledHash) {
                if (Test-Path -LiteralPath $entry.Target) {
                    Remove-Item -LiteralPath $entry.Target -Force
                }
                if (Test-Path -LiteralPath $entry.Target) {
                    throw 'new file remained after rollback'
                }
                continue
            }
            if ((Get-OfflineDaocFileHash $entry.Target) -eq $entry.InstalledHash) {
                continue
            }
            $copy = Join-Path $BackupRoot $entry.Relative
            if ((Get-OfflineDaocFileHash $copy) -ne $entry.InstalledHash) {
                throw 'backup copy does not match the original hash'
            }
            Copy-Item -LiteralPath $copy -Destination $entry.Target -Force
            if ((Get-OfflineDaocFileHash $entry.Target) -ne $entry.InstalledHash) {
                throw 'restored file does not match the original hash'
            }
        }
        catch {
            $failures += ('{0} ({1})' -f $entry.Relative, $_.Exception.Message)
        }
    }
    return $failures
}

function Invoke-OfflineDaocDeploy {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$InstallRoot,
        [Parameter(Mandatory = $true)][string]$ServerBuild,
        [string]$LauncherBuild,
        [switch]$IncludeThirdParty,
        [switch]$Apply,
        [switch]$PassThru
    )
    Assert-OfflineDaocStopped
    $root = Resolve-OfflineDaocInstallRoot -InstallRoot $InstallRoot
    $entries = @(Get-OfflineDaocDeployPlan -InstallRoot $root -ServerBuild $ServerBuild -LauncherBuild $LauncherBuild `
            -IncludeThirdParty:$IncludeThirdParty)
    # Short hash prefixes keep each row on one line; manifest.json keeps full hashes.
    $shortHash = { param($hash) if ($hash) { $hash.Substring(0, 12) } else { '-' } }
    Write-Host ($entries | Format-Table Action, Relative,
        @{ Label = 'Installed'; Expression = { & $shortHash $_.InstalledHash } },
        @{ Label = 'New'; Expression = { & $shortHash $_.NewHash } } -AutoSize | Out-String -Width 4096)

    $missing = @($entries | Where-Object { $_.Action -eq 'error-missing-source' })
    if ($missing.Count -gt 0) {
        throw ("Build is missing files: " + (($missing | ForEach-Object { $_.Relative }) -join ', '))
    }
    $newThirdParty = @($entries | Where-Object { $_.Action -eq 'missing-in-install' })
    if ($newThirdParty.Count -gt 0) {
        Write-Warning ("Build has DLLs the install lacks (not deployed; the server may fail to load without them): " +
            (($newThirdParty | ForEach-Object { [IO.Path]::GetFileName($_.Relative) }) -join ', '))
    }
    $skippedThirdParty = @($entries | Where-Object { $_.Action -eq 'skip-third-party' })
    if ($skippedThirdParty.Count -gt 0) {
        Write-Warning 'Changed third-party DLLs are skipped. Pass -IncludeThirdParty to deploy them.'
    }

    if (-not $Apply) {
        if ($PassThru) { return $entries }
        return
    }

    $replace = @($entries | Where-Object { $_.Action -eq 'replace' })
    if ($replace.Count -eq 0) {
        Write-Host 'No files to replace. Install unchanged.'
        if ($PassThru) { return $entries }
        return
    }

    $protected = @(Get-OfflineDaocProtectedHashes -InstallRoot $root)
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $backupRoot = '{0}-backups\deploy-{1}' -f $root, $stamp
    if (Test-Path -LiteralPath $backupRoot) {
        throw "Backup folder already exists: $backupRoot. Wait a second and retry."
    }
    New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
    $backedUp = @()

    try {
        foreach ($entry in $replace) {
            Assert-OfflineDaocPathUnderRoot -InstallRoot $root -Path $entry.Target | Out-Null
            Assert-OfflineDaocPathUnderRoot -InstallRoot $root -Path (Join-Path $root $entry.Relative) | Out-Null
            if ($entry.InstalledHash) {
                $copy = Join-Path $backupRoot $entry.Relative
                New-Item -ItemType Directory -Path (Split-Path -Parent $copy) -Force | Out-Null
                Copy-Item -LiteralPath $entry.Target -Destination $copy
                if ((Get-OfflineDaocFileHash $copy) -ne $entry.InstalledHash) {
                    Remove-Item -LiteralPath $copy -Force -ErrorAction SilentlyContinue
                    throw "Backup verification failed for $($entry.Relative)"
                }
            }
            $backedUp += $entry
        }

        $manifest = [pscustomobject]@{
            Created     = (Get-Date).ToString('o')
            InstallRoot = $root
            Entries     = @($replace | ForEach-Object {
                    [pscustomobject]@{
                        Relative      = $_.Relative
                        Target        = $_.Target
                        Source        = $_.Source
                        InstalledHash = $_.InstalledHash
                        NewHash       = $_.NewHash
                        OldHash       = $_.InstalledHash
                    }
                })
            Protected   = @($protected | ForEach-Object {
                    [pscustomobject]@{
                        Relative = $_.Relative
                        Path     = $_.Path
                        Hash     = $_.Hash
                    }
                })
        }
        $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $backupRoot 'manifest.json') -Encoding UTF8

        foreach ($entry in $replace) {
            Assert-OfflineDaocStopped
            $current = Get-OfflineDaocFileHash $entry.Target
            if ($entry.InstalledHash) {
                if ($current -ne $entry.InstalledHash) {
                    throw "Runtime changed during deployment: $($entry.Relative)"
                }
            }
            elseif (Test-Path -LiteralPath $entry.Target) {
                throw "Runtime changed during deployment: $($entry.Relative)"
            }
            Copy-OfflineDaocVerified -Source $entry.Source -Destination $entry.Target -ExpectedHash $entry.NewHash
        }

        Assert-OfflineDaocProtectedUnchanged -InstallRoot $root -Before $protected
        Write-Host ("Deployed {0} file(s). Accounts, database and settings unchanged. Backup: {1}" -f $replace.Count, $backupRoot)
        if ($PassThru) { return $entries }
    }
    catch {
        # Roll back before anything else can throw: a process that started
        # mid-deploy is the usual reason we are here.
        $reason = $_.Exception.Message
        $failures = @(Restore-OfflineDaocEntriesFromBackup -BackupRoot $backupRoot -Entries $backedUp)
        if ($failures.Count -gt 0) {
            $message = ("Deploy failed ({0}) and rollback is INCOMPLETE; the install may be partially deployed. " +
                "Close the game and copy these back from {1}: {2}") -f $reason, $backupRoot, ($failures -join '; ')
            throw $message
        }
        throw "Deploy failed and was rolled back; install unchanged. Reason: $reason"
    }
}

function Invoke-OfflineDaocRestore {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$InstallRoot,
        [Parameter(Mandatory = $true)][string]$Backup,
        [switch]$PassThru
    )
    Assert-OfflineDaocStopped
    $root = Resolve-OfflineDaocInstallRoot -InstallRoot $InstallRoot
    $backupRoot = [IO.Path]::GetFullPath($Backup)
    $manifestPath = Join-Path $backupRoot 'manifest.json'
    if (-not (Test-Path -LiteralPath $manifestPath)) {
        throw "Backup manifest not found: $manifestPath"
    }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.InstallRoot -and ([IO.Path]::GetFullPath($manifest.InstallRoot).TrimEnd('\') -ne $root)) {
        throw "Backup install root $($manifest.InstallRoot) does not match $root"
    }

    $pending = @()
    foreach ($entry in @($manifest.Entries)) {
        $target = Assert-OfflineDaocPathUnderRoot -InstallRoot $root -Path (Join-Path $root $entry.Relative)
        if ($entry.Target -and ([IO.Path]::GetFullPath($entry.Target) -ne $target)) {
            throw "Invalid restore target: $($entry.Target)"
        }
        $oldHash = $entry.OldHash
        if (-not $oldHash) { $oldHash = $entry.InstalledHash }
        $current = Get-OfflineDaocFileHash $target
        if ($current -eq $oldHash) {
            # Already restored (e.g. an interrupted earlier restore); nothing to do.
            continue
        }
        if ($current -ne $entry.NewHash) {
            throw "Installed hash for $($entry.Relative) is neither the deployed nor the backed-up hash; restore refused."
        }
        $copy = Join-Path $backupRoot $entry.Relative
        if ((Get-OfflineDaocFileHash $copy) -ne $oldHash) {
            throw "Backup file hash mismatch for $($entry.Relative)"
        }
        $pending += [pscustomobject]@{ Target = $target; Copy = $copy; OldHash = $oldHash }
    }

    foreach ($item in $pending) {
        Assert-OfflineDaocStopped
        Copy-OfflineDaocVerified -Source $item.Copy -Destination $item.Target -ExpectedHash $item.OldHash
    }

    Write-Host ("Restored {0} of {1} file(s) from {2}. Saves and settings were not rolled back." -f $pending.Count, @($manifest.Entries).Count, $backupRoot)
    if ($PassThru) { return $manifest }
}

Export-ModuleMember -Function @(
    'Get-OfflineDaocRunningProcesses',
    'Assert-OfflineDaocStopped',
    'Resolve-OfflineDaocInstallRoot',
    'Assert-OfflineDaocPathUnderRoot',
    'Get-OfflineDaocFileHash',
    'Find-OfflineDaocBuildFile',
    'Get-OfflineDaocProtectedHashes',
    'Get-OfflineDaocDeployPlan',
    'Invoke-OfflineDaocDeploy',
    'Invoke-OfflineDaocRestore'
)
