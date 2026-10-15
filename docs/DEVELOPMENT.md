# Development and reproducibility

## Baseline and layout

This fork's launcher displays `DisplayVersion` from
`source/tools/OfflineDaoc.Launcher/MainForm.cs` (see CHANGELOG.md). The upstream
playable download remains GitHub v0.3; that package is the runtime, world data,
and navigation baseline. Do not confuse this fork's three-part version with the
original author's private 0.4 launcher label. Portable account bootstrap and
default settings are release-specific differences. The active Camlann full-PvP
conversion and its current Tier 9 checkpoint are documented in `docs/CAMLANN.md`.
Work only on the tier the owner requests; do not deploy it over a running install.

Companion-bot reward and progression rules are documented in
`docs/COMPANION_BOTS.md`.

The release's runtime/server contains the reference installed binaries and 99
navigation meshes. Runtime/data contains a cleaned world database. Runtime/client-opendaoc/app
contains the compatible game installation. Never use the author's old absolute paths.

The runnable release also bundles tools/dotnet and tools/nuget-feed for offline C#
development, the navigation builder/native dependencies, and the texture tool's
Python runtime. A GitHub source ZIP alone is not the complete runtime download.

## Fast local R&D shipping

The root `AGENTS.md` shipping policy overrides the generic merge workflow.
"ship now" and "merge to main" authorize direct merge/push to the fork and a
fresh local server/launcher build deployed to `D:\Games\OfflineDAoC`, without
another confirmation. No PR, CI wait, Docker check, automatic test suite, or
post-deploy monitoring is required. Upstream Docker files do not participate in
this Windows workflow. Git remains the source backup; the owner tests gameplay.

Fetch/integrate fork main before building. Build both entry projects, including
for documentation changes, then commit/merge/push and deploy those same outputs.
Incremental Release builds use cached dependencies; restore only if assets are
missing or stale. These output trees are separate from the installed game:

```bash
# Server and its dependencies; does not build the test project.
export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"
dotnet build source/server/CoreServer/CoreServer.csproj -c Release --no-restore

# Windows launcher; output defaults to bin/Release/net10.0-windows.
tools/dev/winnet.sh build source/tools/OfflineDaoc.Launcher/OfflineDaoc.Launcher.csproj \
  -c Release --no-restore
# If restore is needed, restore the corresponding project first; for the
# launcher use winnet.sh restore with --configfile /mnt/d/Games/OfflineDAoC/NuGet.Config.

# After merge/push and stopping this installation's verified processes:
tools/dev/deploy.sh -InstallRoot /mnt/d/Games/OfflineDAoC \
  -ServerBuild source/server/Release \
  -LauncherBuild source/tools/OfflineDaoc.Launcher/bin/Release/net10.0-windows \
  -Apply
```

Stopping this installation's components is pre-authorized for shipping; prefer
graceful shutdown and verify process paths before terminating anything. The
deploy tool still requires stopped processes and preserves backups, rollback,
and save/configuration protections. Leave the game stopped for owner testing.
Report build, merge/push, and deployment outcomes separately. Ordinary development
requests do not deploy. The full test and setup commands below are reference
instructions for explicitly requested validation, not shipping gates.

## Windows server process priority

At startup, CoreServer applies `BelowNormal` process priority on Windows so
normal-priority desktop/foreground apps can preempt server CPU work. Bot/NPC
cadences and individual thread priorities are unchanged; under CPU contention,
the server can run slower. This is scheduling preference, not a CPU usage cap.

Set `<ProcessPriority>BelowNormal</ProcessPriority>` inside `<Server>` in the
active `config/serverconfig.xml` (or the file selected with `-config`). Allowed
values are `Normal`, `BelowNormal`, and `Idle`, ignoring case and surrounding
whitespace. Missing or invalid values fall back to `BelowNormal`; invalid values
log a warning. Restart the server after changing this setting. Non-Windows
hosts skip the priority change. A failure to set priority logs a warning and
allows startup to continue.

Verify after startup in Task Manager → Details: enable the **Base priority**
column and check `CoreServer.exe` shows **Below normal**. The startup log should
also contain `Server process priority applied: BelowNormal.` Compare desktop
responsiveness under the same bot load, and confirm bots still move/fight.

## Offline World Speed

The launcher can request 1×, 2×, 3×, 5×, 10×, or 20× world speed for a running local server.
The game loop retains its 30 Hz logical tick and runs complete ticks at a
shorter real interval; overload slows actual progress rather than skipping
ticks. Any connected client forces 1×, and the selected speed resumes five
seconds after the last disconnect. A new server session always selects 1×.
The launcher's achieved-speed reading is measured, not an XP multiplier.

Gameplay UTC is derived from completed world ticks. Operational timestamps
(logs, request freshness, and backups) remain real UTC. A versioned clock
checkpoint in the save's `offline_local_options` row keeps gameplay deadlines
continuous through restarts; downtime advances those deadlines at 1×. Keep
this row with the save when making backups or restoring progress. The local
`world-speed.request.json` and `world-speed.status.json` files are transient
launcher/server control and status, not save data.

## Build (does not deploy or start the server)

For the owner's WSL2 + Windows setup (install location, SDKs, baseline,
deploy/restore scripts), follow the completed tiered plan in
`docs/completed/DEV-SETUP.md`.

### WSL2 + Windows loop

Verified end to end on 2026-09-19: build, tests, self-check, dry run,
`-Apply`, in-game smoke test, Restore, and redeploy against the real install.
Before Camlann Tier 0, make a fresh named save backup (`docs/completed/DEV-SETUP.md`
Tier 6).

Repo: `/home/stefan/Development/Games/OfflineDAoC`. Install:
`D:\Games\OfflineDAoC` (`OFFLINE_DAOC_ROOT`, default `/mnt/d/Games/OfflineDAoC`).
Dev state: `D:\Games\OfflineDAoC-dev` (SDK caches / NuGet; not in the repo).

```bash
# Server build + tests on WSL
export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"
dotnet restore 'source/server/Dawn of Light.sln' -p:Configuration=Release
dotnet build 'source/server/Dawn of Light.sln' -c Release --no-restore
dotnet test source/server/Tests/Tests.csproj -c Release --no-build --no-restore

# Launcher tests via the install's Windows SDK
tools/dev/winnet.sh restore \
  source/tools/OfflineDaoc.Launcher.Tests/OfflineDaoc.Launcher.Tests.csproj \
  --configfile /mnt/d/Games/OfflineDAoC/NuGet.Config
tools/dev/winnet.sh test \
  source/tools/OfflineDaoc.Launcher.Tests/OfflineDaoc.Launcher.Tests.csproj \
  -c Release --no-restore

# Deploy dry-run (default) / apply (shipping is pre-authorized; server must be stopped)
tools/dev/deploy.sh -InstallRoot /mnt/d/Games/OfflineDAoC \
  -ServerBuild source/server/Release
# tools/dev/deploy.sh ... -Apply
# Changed third-party DLLs are skipped unless the owner adds -IncludeThirdParty.
# powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/dev/Restore-OfflineDAoC.ps1 \
#   -InstallRoot 'D:\Games\OfflineDAoC' -Backup 'D:\Games\OfflineDAoC-backups\deploy-<stamp>'
```

Self-check (never targets the real install):
`powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/dev/Test-DeployOfflineDAoC.ps1`

Use a .NET 10 SDK on Windows, or the SDK in the complete release. From the root:

```powershell
dotnet restore 'source/server/Dawn of Light.sln' -p:Configuration=Release
dotnet build 'source/server/Dawn of Light.sln' -c Release --no-restore
dotnet test source/server/Tests/Tests.csproj -c Release --no-build --no-restore
dotnet restore source/tools/OfflineDaoc.Launcher.Tests/OfflineDaoc.Launcher.Tests.csproj
dotnet test source/tools/OfflineDaoc.Launcher.Tests/OfflineDaoc.Launcher.Tests.csproj -c Release --no-restore
```

`source/server/CoreServer/config/serverconfig.xml` is a local, ignored runtime
configuration. A clean checkout has only `serverconfig.example.xml`; the
CoreServer project automatically uses that example as the build output config
when the local file is absent. A real local `serverconfig.xml`, when present,
remains authoritative and is never replaced by the build or deploy scripts.

For offline restore, use the release's NuGet.Config and set NUGET_PACKAGES to a
local developer-state directory. Some launcher tests require that no DAoC server
is listening locally; a running server can correctly trigger the save lock.

If the complete download is in `playable`, its SDK is
`playable/tools/dotnet/dotnet.exe` and its offline configuration is
`playable/NuGet.Config`. These are explicit alternatives to a globally installed SDK.
The release's BUILD AND TEST SOURCE.cmd is the ready-made offline build entrypoint
for the source bundled inside that complete download. It never deploys a build.

## Native client modifications

The native raid and bot-map builders are in source/server/tools, with validation
tests alongside them. They patch a specific verified x86 binary; they are not the
original client's C++ source. Their baseline hashes and dependencies matter.
Historical scripts may refer to backup input paths on the author's PC: these must
be parameterized and the required baseline supplied before rerunning. Do not
substitute an arbitrary game.dll or remove a failed hash guard. The supported
`tools/build-client-raid.py` wrapper resolves inputs from `--distribution`, includes
the exact baseline, and stages into a fresh `--output` directory. In release
verification this rebuilt the installed game.dll byte-for-byte.

### Static client analysis with REA and Ghidra

[REA](https://github.com/morluto/rea) (`rea-agents`, MIT) gives an agent
Ghidra decompilation, disassembly, strings, and cross-references for game.dll,
as an MCP server (`rea mcp`) or CLI. Use it to find client functions and call
sites; keep the Unicorn emulation tests and real-client checks as the proof of
behavior. It is a developer tool only: nothing ships to the game installation.

Setup on WSL (user-local; neither REA nor this setup installs Ghidra or Java):

```bash
# Ghidra 12.1.x and a 64-bit JDK 21 are required; check the published SHA-256.
# Extract them under ~/opt (Ghidra zip: python3 zipfile if unzip is missing).
npm install --global --ignore-scripts rea-agents@6.4.0
export GHIDRA_INSTALL_DIR=$HOME/opt/ghidra_12.1.4_PUBLIC
export JAVA_HOME=$HOME/opt/jdk-21.0.12.1+1
rea doctor --provider ghidra --json
rea setup --client claude_code --dry-run   # review, then rerun with --yes
```

Setup registers the MCP server in `~/.claude.json` (backup alongside) and adds
REA's skill under `~/.claude/skills`; restart the agent session to load it.
Do not let setup install Hopper.

Analyze a read-only copy of the verified baseline, never the installed client.
The raid baseline game.dll (`67dcf68a…`) is kept in the
`companion-custom8-probe-*` and `companion-manager-client-*` backups under
`D:\Games\OfflineDAoC-backups`. Work outside the game installation:

```bash
cp <backup>/game.dll <scratch>/game.dll && chmod a-w <scratch>/game.dll
sha256sum <scratch>/game.dll    # must start with 67dcf68a
export REA_GHIDRA_STARTUP_TIMEOUT_MS=1200000
rea decompile <scratch>/game.dll 0x4EA06F --provider ghidra --format json
rea xrefs <scratch>/game.dll 0x4EA06F --provider ghidra --format json
```

Each CLI call reruns Ghidra auto-analysis (about four minutes for game.dll;
`--snapshot` does not avoid it). An MCP session keeps one analysis open, so
only its first query pays that cost.

Validated against earlier manual work on the OnClickEvent mapper `0x4EA06F`:
the pseudocode reads the name from ESI, compares 365 names with `_stricmp`
(364 stock events plus `Unspecified`, which returns `-1`), falls back to `atol`
for text starting with a digit, and otherwise returns `-1`. Its 21 call sites
include `0x4EDA7F`/`0x4EDAA5` inside the InvisibleButtonDef parser `0x4ED9B4`.
See [Companion Manager integration](COMPANION_MANAGER_INTEGRATION.md). The
full XML vocabulary, the value mappers and the event table are in the
[Client UI XML reference](CLIENT_UI_XML_REFERENCE.md). Item slots, drag and
drop and the move-item packet are in the
[companion paperdoll analysis](COMPANION_PAPERDOLL_RE.md).

Test texture-tool source with `python tools/test-assets.py --distribution playable`
(or the actual complete-download path). The wrapper resolves the read-only fixtures
for the source-checkout layout; modifications happen only in temporary test copies.

### Client map pack (512 px Map window)

An optional client file set, built with no game.dll change. It produces
`ui/{atlantis,isles}/map_window.xml`, `ui/isles/map_icons.tga` with a cyan
group-member cell at (144,112), and every `ui/maps/*.dds` at 512 x 512 DXT1.
REA showed that the Map window places markers from the image control's
runtime size (`FUN_00530328`), so every map texture must match the 512 px
control. Requires Python 3 with Pillow and numpy.

```bash
python3 source/server/tools/render_client_maps.py --client <app> \
  --world-db <opendaoc.sqlite3.db> --output <new maps dir>
python3 source/server/tools/build_map_client.py --client <app> \
  --maps <maps dir> --output <new stage dir>
python3 -m unittest discover -s source/server/tools -p test_map_client.py
```

The renderer copies the world database and opens the copy read only. It reads
only static world tables (areas, bind points, keeps, NPC trainers, merchants,
zone points, teleports, relics); it never reads players, accounts or bots.
Zones without a zone-wide ground texture keep their stock art (`--terrain-all`
overrides that). Zones that have no stock map are skipped unless you pass
`--include-new`. Curated place names from the stock maps live in
`source/server/tools/client_map_labels.json` (label centres in stock 256-texel
units; `--labels` picks another file). Install with the game, server and
launcher closed. The install is a dry run unless you pass `-Apply`:

```powershell
tools\dev\Install-ClientMap.ps1 -InstallRoot D:\Games\OfflineDAoC -Stage <stage> [-Apply]
tools\dev\Install-ClientMap.ps1 -InstallRoot D:\Games\OfflineDAoC -RestoreBackup <backup>
```

Maps the stock client does not ship (new terrain for Atlantis zones z073-z087
and Foothills z330, z334, z335) need both `--include-new` in
`render_client_maps.py` and `--allow-new-maps` in `build_map_client.py`. The
installer adds those as new files, and restore deletes them if they still match
the installed hash.

## World data, navigation, and customization

Keep the clean world definitions and all current navmeshes available to the LLM.
Source route resources alone are not a replacement for the generated native mesh
files or world spawn data. Preserve the current validated meshes until a focused
rebuild is requested. The texture tool converts atlases while preserving carrier
models and bindings; it does not create a usable 3D mesh from a PNG.

### Battleground navigation

The 0.222.0 campaign needs ten native meshes missing from the original runtime.
Build only these zones into developer state without modifying the installation:

```bash
tools/dev/build-battleground-nav.sh
# Default output: /mnt/d/Games/OfflineDAoC-dev/battleground-nav/meshes
```

The helper uses the installed compatible client and cached Windows SDK, builds
the navigation tool, and validates all ten nonempty native mesh headers before
staging. It generates zones 165,234–238,240,241,251,254; zone242 is TestBG,
not Leirvik. Output must be outside the installation. Intermediates remain in
the helper's `builder/base/zones` tree. `--install-root` and `--output` support
another complete installation and developer directory. Geometry generation can
take several minutes; no server is started and no live mesh is overwritten.

After staging, the helper checks every battleground keep centre (central and portal) on
the zone's own mesh: a path from each realm landing of its region (the portal keep centres)
must exist with the server's default filters. `tools/dev/bg-keep-connectivity.cpp` runs
an unbounded flood fill, and also reports the gates-closed case (Door polygons excluded)
and the server's 256-polygon corridor limit. Results go to `bg-keep-connectivity.txt`,
and the helper exits 3 when a keep is unreachable; the meshes are staged either way. The
terrain height under each keep piece is logged as `BG_KEEP_GROUND_SUMMARY`
(`bg-keep-ground.txt`).

#### Server-built battleground keeps

The keeps for 234, 235, 236, 237, 238, 240, 241 and 242 are built by the server
from the `/keep fastcreate` bracket layouts (`BattlegroundKeepLayouts.cs`), not
from client keep models. Their navigation pieces come from the client's
frontier kit. The shared data is `tools/dev/battleground-keeps.json`: the keep
sites, the component templates, the KeepPosition door offsets for skins 0, 10
and 11, and the portal-keep centres. Edit the C# table and the JSON together;
`UT_BattlegroundKeepLayouts` fails when they differ.

Order of work when a site or a template changes:

```bash
# 1. Search for flat, clear ground for the new sites (zones 234, 235, 236, 238, 240).
#    Accepted X/Y/Z are written into the JSON. A site with no spot stays in blockedSites.
bash tools/dev/build-battleground-nav.sh --bg-keep-sites
# 2. Copy the accepted X/Y/Z into the matching KeepSite rows in BattlegroundKeepLayouts.Sites.
# 3. Bake the meshes. The helper passes --bg-keeps, so zones 234, 235, 236, 237, 240,
#    241 and 254 (region 242) include the keep pieces.
bash tools/dev/build-battleground-nav.sh
```

Piece selection: a skin maps to its tier-1 piece through `fr_tiers.csv` and
`fr_pieces.csv` in `frontiers/frontiers.mpk`, and each piece is read from
`frontiers/nifs/<piece>.npk`. Pieces are placed with the server's component
formula, so the keep heading applies in degrees as the server applies it.
Orientation is one of four variants (mirror and heading sign). A variant is
valid when at least one server door has a native door node within 96 units.
The valid variant with the most matched doors wins; ties go to the smallest
worst delta, then to the baseline. A server door with no native door node
within 96 units becomes a Door volume at its position (`BG_KEEP_DOOR_VOLUME_ONLY`),
so the server still registers it as blocking. A keep falls back to cylinders
(every 96 units along each wall footprint, radius 64) plus Door volumes only
when no variant is valid, a door piece has no NIF, or a piece has no triangles.

The Thidranki site (keep 143, zone 238) uses a relaxed finder rule: variance up
to 200, keep Z at the lowest ground across the footprint, and no solid-fixture
test. The fixture count is logged (`solid_fixtures_within_clearance`).

The log lines are `BG_KEEP_PIECE zone= keep= comp= nif= tris=`,
`BG_KEEP_DOOR_MATCH zone= keep= comp= door= delta=`, `BG_KEEP_VARIANT`,
`BG_KEEP_NIF_DOORS` (door centres from the NIF against the server doors),
`BG_KEEP_DOOR_VOLUME_ONLY` and `BG_KEEP_FALLBACK` (the keep or piece that fell
back, with the reason).

For a later authorized shipping invocation, include the staged meshes in the
same stopped-install, backup/hash/rollback workflow:

```bash
tools/dev/deploy.sh -InstallRoot /mnt/d/Games/OfflineDAoC \
  -ServerBuild source/server/Release \
  -LauncherBuild source/tools/OfflineDaoc.Launcher/bin/Release/net10.0-windows \
  -NavmeshBuild /mnt/d/Games/OfflineDAoC-dev/battleground-nav/meshes -Apply
```

The optional `-NavmeshBuild` parameter accepts only the ten validated campaign
meshes; other navigation files are untouched. A source checkout without the
compatible runtime/client cannot generate those binary assets. Entry stays
closed until native meshes and safe portal landing geometry are ready.

## Safe sharing

Never commit a database after playing. It will contain accounts, characters, bot
profiles, inventories and economy history. Generate a fresh sanitized seed in a
separate copy, check every progress table, clear free pages with VACUUM, and verify
integrity before sharing it. Exclude credentials, logs, diagnostics and old backups.

Default setup is local-only. Running a public multiplayer server is a separate
security/deployment project; do not expose this local configuration to the internet.
