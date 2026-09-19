#!/usr/bin/env bash
# Run the complete-install bundled Windows SDK from WSL.
# Install root: OFFLINE_DAOC_ROOT (WSL path), default /mnt/d/Games/OfflineDAoC.
# CLI home and NuGet packages: ${OFFLINE_DAOC_DEV:-${OFFLINE_DAOC_ROOT}-dev}/state
# (not in the repo, not on C:). Paths passed through to dotnet.exe are converted
# with wslpath -w when they exist on the WSL side.
set -euo pipefail

to_windows() {
  local path="$1"
  if command -v wslpath >/dev/null 2>&1; then
    wslpath -w "$path"
  else
    printf '%s' "$path"
  fi
}

looks_like_windows_path() {
  local path="$1"
  [[ "$path" =~ ^[A-Za-z]:[\\/] ]] || [[ "$path" == \\\\* ]]
}

convert_arg() {
  local arg="$1"
  if [[ "$arg" == -* ]]; then
    printf '%s' "$arg"
    return
  fi
  if looks_like_windows_path "$arg"; then
    printf '%s' "$arg"
    return
  fi
  if [[ -e "$arg" ]]; then
    to_windows "$arg"
    return
  fi
  printf '%s' "$arg"
}

INSTALL_ROOT="${OFFLINE_DAOC_ROOT:-/mnt/d/Games/OfflineDAoC}"
DEV_ROOT="${OFFLINE_DAOC_DEV:-${INSTALL_ROOT}-dev}"

if [[ ! -d "$INSTALL_ROOT" ]]; then
  echo "winnet.sh: install root not found: $INSTALL_ROOT" >&2
  echo "Set OFFLINE_DAOC_ROOT to the WSL path of the complete portable install." >&2
  exit 1
fi

DOTNET_EXE="${INSTALL_ROOT}/tools/dotnet/dotnet.exe"
if [[ ! -f "$DOTNET_EXE" ]]; then
  echo "winnet.sh: bundled SDK not found: $DOTNET_EXE" >&2
  exit 1
fi

STATE_DIR="${DEV_ROOT}/state"
mkdir -p "${STATE_DIR}/packages"

WIN_INSTALL="$(to_windows "$INSTALL_ROOT")"
WIN_STATE="$(to_windows "$STATE_DIR")"

export DOTNET_ROOT="${WIN_INSTALL}\\tools\\dotnet"
export DOTNET_ROOT_X64="$DOTNET_ROOT"
export DOTNET_MULTILEVEL_LOOKUP=0
export DOTNET_CLI_HOME="$WIN_STATE"
export NUGET_PACKAGES="${WIN_STATE}\\packages"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_GENERATE_ASPNET_CERTIFICATE=false

converted=()
for arg in "$@"; do
  converted+=("$(convert_arg "$arg")")
done

exec "$DOTNET_EXE" "${converted[@]}"
