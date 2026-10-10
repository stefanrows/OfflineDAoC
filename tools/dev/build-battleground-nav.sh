#!/usr/bin/env bash
# Build only the campaign's native zones into developer state; never install them.
#
# Default: bake the ten campaign meshes, including server-built keep pieces for the
# battleground keeps listed in tools/dev/battleground-keeps.json. After staging, every
# battleground keep centre (central and portal) is checked for a path from every realm landing
# of its region on the same navmeshes (tools/dev/bg-keep-connectivity.cpp). The helper exits 3
# when a keep is not reachable with default filters; the meshes are staged either way.
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
  --zones=165,234,235,236,237,238,240,241,251,254 --non-interactive 2>&1 | tee "$OUTPUT_ROOT/builder/build.log"
# Connectivity checker: the repo's Detour sources (uncapped path search, the same dtPolyRef width as the server).
DETOUR_ROOT="$REPO_ROOT/source/development-tools/OpenDAoC-Core/Pathing/Detour"
CONN_TOOL="$OUTPUT_ROOT/tools/bg-keep-connectivity"
mkdir -p "$OUTPUT_ROOT/tools"
g++ -std=c++17 -O2 -w -DDT_POLYREF64 -I"$DETOUR_ROOT/Include" "$REPO_ROOT/tools/dev/bg-keep-connectivity.cpp" "$DETOUR_ROOT"/Source/*.cpp -o "$CONN_TOOL"
python3 - "$OUTPUT_ROOT" "$BG_KEEPS" "$CONN_TOOL" <<'PY'
from pathlib import Path
import json
import shutil
import struct
import subprocess
import sys
root = Path(sys.argv[1])
bg_keeps = Path(sys.argv[2])
conn_tool = sys.argv[3]
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

# Battleground keep connectivity: each keep centre (central and portal) must be reachable from every realm landing
# (the portal keep centres) of its region. Region 242 (Leirvik) is zone 254; TestBG zone 242 is never used.
data = json.loads(bg_keeps.read_text())
def zone_for(region):
    return 254 if region == 242 else region
entries = [('central', site) for site in data['sites']] + [('portal', site) for site in data['portalKeepSites']]
lines = []
keep_zones = set()
for kind, site in entries:
    zone = zone_for(site['region'])
    keep_zones.add(zone)
    lines.append(f"K {zone} {site['region']} {site['keepId']} {kind} {site['x']} {site['y']} {site['z']}")
for site in data['portalKeepSites']:
    lines.append(f"L {site['region']} {site['keepId']} {site['x']} {site['y']} {site['z']}")
conn_dir = root / 'connectivity'
conn_dir.mkdir(exist_ok=True)
input_path = conn_dir / 'input.txt'
input_path.write_text('\n'.join(lines) + '\n')
results = []
for zone in sorted(keep_zones):
    nav = root / 'builder' / 'base' / 'zones' / f'zone{zone:03d}.nav'
    out_path = conn_dir / f'zone{zone:03d}.txt'
    if out_path.exists():
        out_path.unlink()
    proc = subprocess.run([conn_tool, str(nav), str(zone), str(input_path), str(out_path)], capture_output=True, text=True)
    if proc.returncode != 0 or not out_path.exists():
        results.append(f'zone={zone} status=checker_failed exit={proc.returncode} {proc.stderr.strip()}')
        continue
    results.extend(line for line in out_path.read_text().splitlines() if line.strip())
(root / 'bg-keep-connectivity.txt').write_text('\n'.join(results) + '\n')
print('Battleground keep connectivity (unbounded path search; default, blocking and gates_closed filters):')
for line in results:
    print('  ' + line)
# Terrain height under each server-built keep piece, against the keep Z the server places it at (builder log).
ground = [line for line in (root / 'builder' / 'build.log').read_text(errors='replace').splitlines() if 'BG_KEEP_GROUND_SUMMARY' in line]
(root / 'bg-keep-ground.txt').write_text('\n'.join(ground) + '\n')
print('Battleground keep ground deviation (terrain height minus keep Z, per keep):')
for line in ground:
    print('  ' + line[line.index('BG_KEEP_GROUND_SUMMARY'):])
unknown = [line for line in results if ' default=UNKNOWN' in line]
if unknown:
    print(f'WARNING: {len(unknown)} pair(s) ran out of path nodes; result unknown.')
failed = [line for line in results if ' default=FAIL' in line or ' default=NO_FLOOR' in line or ' status=' in line]
if not results:
    raise SystemExit('Battleground keep connectivity produced no results; the check did not run.')
if failed:
    print(f'FAILED: {len(failed)} check(s) found a battleground keep not reachable with default filters. Meshes are staged; see {root / "bg-keep-connectivity.txt"}.')
    raise SystemExit(3)
print('Battleground keep connectivity: every keep centre is reachable from every realm landing with default filters.')
PY
