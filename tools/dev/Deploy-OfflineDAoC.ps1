[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$InstallRoot,
    [Parameter(Mandatory = $true)][string]$ServerBuild,
    [string]$LauncherBuild,
    [switch]$IncludeThirdParty,
    [switch]$Apply
)
$ErrorActionPreference = 'Stop'
Import-Module -Force -Name (Join-Path $PSScriptRoot 'OfflineDAoC.Deploy.psm1')
Invoke-OfflineDaocDeploy -InstallRoot $InstallRoot -ServerBuild $ServerBuild -LauncherBuild $LauncherBuild `
    -IncludeThirdParty:$IncludeThirdParty -Apply:$Apply
