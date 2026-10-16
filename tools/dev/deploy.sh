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
navmesh_build=""
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
    -InstallRoot|--install-root|-ServerBuild|--server-build|-LauncherBuild|--launcher-build|-NavmeshBuild|--navmesh-build)
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
    -NavmeshBuild|--navmesh-build)
      navmesh_build="$(to_windows "$2")"
      shift 2
      ;;
    -Apply|--apply)
      apply=1
      shift
      ;;
    *)
      echo "deploy.sh: unknown argument: $1" >&2
      echo "usage: deploy.sh -InstallRoot <path> -ServerBuild <path> [-LauncherBuild <path>] [-NavmeshBuild <path>] [-IncludeThirdParty] [-Apply]" >&2
      exit 1
      ;;
  esac
done

if [[ -z "$install_root" || -z "$server_build" ]]; then
  echo "deploy.sh: -InstallRoot and -ServerBuild are required." >&2
  exit 1
fi

# The battleground meshes (zones 165, 234-238, 240, 241, 251, 252, 254) only reach the install through
# -NavmeshBuild. Deploys without it left zone252.nav (open BG) uninstalled and the open BG closed with
# OPEN_BG_UNAVAILABLE reason=no_navmesh. When one is missing and the staged set is complete, add it.
if [[ -z "$navmesh_build" ]]; then
  install_wsl="$(wslpath -u "$install_root")"
  staged_meshes="${OFFLINE_DAOC_DEV:-${install_wsl}-dev}/battleground-nav/meshes"
  missing_mesh=0
  staged_complete=1
  for zone in 165 234 235 236 237 238 240 241 251 252 254; do
    mesh="zone${zone}.nav"
    if [[ ! -f "$install_wsl/runtime/server/pathing/$mesh" && ! -f "$install_wsl/runtime/server/navmesh/$mesh" ]]; then
      missing_mesh=1
    fi
    [[ -f "$staged_meshes/$mesh" ]] || staged_complete=0
  done
  if [[ $missing_mesh -eq 1 && $staged_complete -eq 1 ]]; then
    echo "deploy.sh: a battleground navmesh is missing from the install; adding -NavmeshBuild $staged_meshes" >&2
    navmesh_build="$(to_windows "$staged_meshes")"
  elif [[ $missing_mesh -eq 1 ]]; then
    echo "deploy.sh: warning: a battleground navmesh is missing from the install and no complete staged set exists in $staged_meshes (tools/dev/build-battleground-nav.sh); the open BG stays closed." >&2
  fi
fi

args=(-NoProfile -ExecutionPolicy Bypass -File "$PS1" -InstallRoot "$install_root" -ServerBuild "$server_build")
if [[ -n "$launcher_build" ]]; then
  args+=(-LauncherBuild "$launcher_build")
fi
if [[ -n "$navmesh_build" ]]; then
  args+=(-NavmeshBuild "$navmesh_build")
fi
if [[ $include_third_party -eq 1 ]]; then
  args+=(-IncludeThirdParty)
fi
if [[ $apply -eq 1 ]]; then
  args+=(-Apply)
fi

exec powershell.exe "${args[@]}"
