@echo off
setlocal
cd /d "%~dp0"
set "UPDATER=%~dp0tools\fork-update\Update-OfflineDAoC.ps1"
if not exist "%UPDATER%" set "UPDATER=%~dp0Update-OfflineDAoC.ps1"
if not exist "%UPDATER%" (
  echo Download Update-OfflineDAoC.ps1 into this same folder first.
  pause
  exit /b 1
)
echo This downloads the latest stefanrows/OfflineDAoC fork update from GitHub,
echo verifies its SHA-256 hash, backs up every file it replaces, and installs it.
echo Close the game, server and launcher first. Saves, account and configs are not touched.
echo PowerShell's script policy is set only for this process, not for Windows.
rem The update may replace this file, so the rest is parsed as one block.
(
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%UPDATER%"
  if errorlevel 1 (
    echo The update did not complete. Read the error above.
    pause
    exit /b 1
  )
  echo Run START OFFLINE DAOC.cmd to play.
  pause
  exit /b 0
)
