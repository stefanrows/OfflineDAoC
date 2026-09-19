[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$InstallRoot,
    [Parameter(Mandatory = $true)][string]$Backup
)
$ErrorActionPreference = 'Stop'
Import-Module -Force -Name (Join-Path $PSScriptRoot 'OfflineDAoC.Deploy.psm1')
Invoke-OfflineDaocRestore -InstallRoot $InstallRoot -Backup $Backup
