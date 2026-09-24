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

Test texture-tool source with `python tools/test-assets.py --distribution playable`
(or the actual complete-download path). The wrapper resolves the read-only fixtures
for the source-checkout layout; modifications happen only in temporary test copies.

## World data, navigation, and customization

Keep the clean world definitions and all current navmeshes available to the LLM.
Source route resources alone are not a replacement for the generated native mesh
files or world spawn data. Preserve the current validated meshes until a focused
rebuild is requested. The texture tool converts atlases while preserving carrier
models and bindings; it does not create a usable 3D mesh from a PNG.

## Safe sharing

Never commit a database after playing. It will contain accounts, characters, bot
profiles, inventories and economy history. Generate a fresh sanitized seed in a
separate copy, check every progress table, clear free pages with VACUUM, and verify
integrity before sharing it. Exclude credentials, logs, diagnostics and old backups.

Default setup is local-only. Running a public multiplayer server is a separate
security/deployment project; do not expose this local configuration to the internet.
