#!/usr/bin/env bash
# WSL wrapper for Deploy-OfflineDAoC.ps1. Converts WSL paths with wslpath -w
# and forwards -Apply only when the caller passes it.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PS1="$(wslpath -w "$SCRIPT_DIR/Deploy-OfflineDAoC.ps1")"

to_windows() {
  local path="$1"
  if [[ "$path" =~ ^[A-Za-z]:[\\/] ]] || [[ "$path" == \\\\* ]]; then
    printf '%s' "$path"
  else
    wslpath -w "$path"
  fi
}

install_root=""
server_build=""
launcher_build=""
apply=0
include_third_party=0

need_value() {
  if [[ $# -lt 2 || -z "$2" ]]; then
    echo "deploy.sh: $1 needs a path." >&2
    exit 1
  fi
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    -InstallRoot|--install-root|-ServerBuild|--server-build|-LauncherBuild|--launcher-build)
      need_value "$@"
      ;;&
    -IncludeThirdParty|--include-third-party)
      include_third_party=1
      shift
      ;;
    -InstallRoot|--install-root)
      install_root="$(to_windows "$2")"
      shift 2
      ;;
    -ServerBuild|--server-build)
      server_build="$(to_windows "$2")"
      shift 2
      ;;
    -LauncherBuild|--launcher-build)
      launcher_build="$(to_windows "$2")"
      shift 2
      ;;
    -Apply|--apply)
      apply=1
      shift
      ;;
    *)
      echo "deploy.sh: unknown argument: $1" >&2
      echo "usage: deploy.sh -InstallRoot <path> -ServerBuild <path> [-LauncherBuild <path>] [-IncludeThirdParty] [-Apply]" >&2
      exit 1
      ;;
  esac
done

if [[ -z "$install_root" || -z "$server_build" ]]; then
  echo "deploy.sh: -InstallRoot and -ServerBuild are required." >&2
  exit 1
fi

args=(-NoProfile -ExecutionPolicy Bypass -File "$PS1" -InstallRoot "$install_root" -ServerBuild "$server_build")
if [[ -n "$launcher_build" ]]; then
  args+=(-LauncherBuild "$launcher_build")
fi
if [[ $include_third_party -eq 1 ]]; then
  args+=(-IncludeThirdParty)
fi
if [[ $apply -eq 1 ]]; then
  args+=(-Apply)
fi

exec powershell.exe "${args[@]}"
