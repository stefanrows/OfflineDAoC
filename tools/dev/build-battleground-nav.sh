#!/usr/bin/env bash
# Build only the campaign's native zones into developer state; never install them.
#
# Default: bake the ten campaign meshes, including server-built keep pieces for the
# battleground keeps listed in tools/dev/battleground-keeps.json.
# --bg-keep-sites: search for flat, clear sites for the new keeps (zones 234, 235, 236, 238, 240)
# and write their X/Y/Z into the same JSON. Run it first, then run the default build.
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
INSTALL_ROOT="${OFFLINE_DAOC_ROOT:-/mnt/d/Games/OfflineDAoC}"
OUTPUT_ROOT=""
BG_KEEPS="$REPO_ROOT/tools/dev/battleground-keeps.json"
MODE="build"
while [[ $# -gt 0 ]]; do
  case "$1" in
    --install-root) INSTALL_ROOT="$2"; shift 2 ;;
    --output) OUTPUT_ROOT="$2"; shift 2 ;;
    --bg-keeps) BG_KEEPS="$2"; shift 2 ;;
    --bg-keep-sites) MODE="sites"; shift ;;
    *) echo "Usage: build-battleground-nav.sh [--install-root <WSL path>] [--output <WSL path>] [--bg-keeps <JSON>] [--bg-keep-sites]" >&2; exit 1 ;;
  esac
done
OUTPUT_ROOT="${OUTPUT_ROOT:-${OFFLINE_DAOC_DEV:-${INSTALL_ROOT}-dev}/battleground-nav}"
INSTALL_ROOT="$(realpath "$INSTALL_ROOT")"
OUTPUT_ROOT="$(realpath -m "$OUTPUT_ROOT")"
BG_KEEPS="$(realpath "$BG_KEEPS")"
if [[ "$OUTPUT_ROOT" == "$INSTALL_ROOT" || "$OUTPUT_ROOT" == "$INSTALL_ROOT/"* ]]; then
  echo "Output must be outside the game installation." >&2
  exit 1
fi
CLIENT_ROOT="$INSTALL_ROOT/runtime/client-opendaoc/app"
if [[ ! -f "$CLIENT_ROOT/zones/zones.mpk" ]]; then
  echo "Native client zones.mpk missing: $CLIENT_ROOT" >&2
  exit 1
fi
if [[ ! -f "$BG_KEEPS" ]]; then
  echo "Battleground keep data missing: $BG_KEEPS" >&2
  exit 1
fi
PROJECT="$REPO_ROOT/source/development-tools/OpenDAoC-BuildNav/OpenDAoC-BuildNav.csproj"
mkdir -p "$OUTPUT_ROOT/builder" "$OUTPUT_ROOT/meshes"
export OFFLINE_DAOC_ROOT="$INSTALL_ROOT"
if [[ ! -f "${PROJECT%/*}/obj/project.assets.json" ]]; then
  "$SCRIPT_DIR/winnet.sh" restore "$PROJECT" --configfile "$INSTALL_ROOT/NuGet.Config"
fi
"$SCRIPT_DIR/winnet.sh" build "$PROJECT" -c Release --no-restore -o "$OUTPUT_ROOT/builder"
mkdir -p "$OUTPUT_ROOT/builder/base/lib"
if [[ ! -f "$OUTPUT_ROOT/builder/base/lib/Detour.dll" ]]; then
  cp "$INSTALL_ROOT/tools/NavmeshBuilder/base/lib/Detour.dll" "$OUTPUT_ROOT/builder/base/lib/Detour.dll"
fi
cp "$REPO_ROOT/source/development-tools/OpenDAoC-BuildNav/ignorelist.txt" "$OUTPUT_ROOT/builder/ignorelist.txt"
BG_KEEPS_WIN="$(wslpath -w "$BG_KEEPS")"
# The tool writes zones/ and reads ../ignorelist.txt relative to its working directory, so run it from builder/base.
cd "$OUTPUT_ROOT/builder/base"
if [[ "$MODE" == "sites" ]]; then
  # Writes accepted X/Y/Z into the JSON (--bg-keeps). No zone files are produced in this mode.
  "$SCRIPT_DIR/winnet.sh" "$OUTPUT_ROOT/builder/OpenDAoC-BuildNav.dll" \
    "--daoc=$(wslpath -w "$CLIENT_ROOT")" \
    "--bg-keeps=$BG_KEEPS_WIN" --bg-keep-sites --non-interactive
  echo "Battleground keep sites searched. Coordinates are in $BG_KEEPS (regenerate C# with tools/dev/battleground-keeps.json)."
  exit 0
fi
# Leirvik is zone254, region242. TestBG zone242 and the incomplete Braemar239
# are deliberately excluded. Murdaigean251 supplies the25–29 bracket.
"$SCRIPT_DIR/winnet.sh" "$OUTPUT_ROOT/builder/OpenDAoC-BuildNav.dll" \
  "--daoc=$(wslpath -w "$CLIENT_ROOT")" \
  "--bg-keeps=$BG_KEEPS_WIN" \
  --zones=165,234,235,236,237,238,240,241,251,254 --non-interactive
python3 - "$OUTPUT_ROOT" <<'PY'
from pathlib import Path
import shutil
import struct
import sys
root = Path(sys.argv[1])
zones = [165, 234, 235, 236, 237, 238, 240, 241, 251, 254]
sources = [root / 'builder' / 'base' / 'zones' / f'zone{zone:03d}.nav' for zone in zones]
for path in sources:
    with path.open('rb') as source:
        magic, version, tiles = struct.unpack('<iii', source.read(12))
    if path.stat().st_size < 2048 or (magic, version) != (0x4d534554, 1) or tiles <= 0:
        raise SystemExit(f'Invalid or empty native mesh: {path}')
for path in sources:
    shutil.copy2(path, root / 'meshes' / path.name)
print(f'Validated {len(sources)} native meshes in {root / "meshes"}. No installed files changed.')
PY
