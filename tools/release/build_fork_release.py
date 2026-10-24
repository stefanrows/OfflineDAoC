#!/usr/bin/env python3
"""Package a fork update pack for an Offline DAoC v0.3 playable folder.

Reads Release build outputs, the installed battleground navmeshes and the staged
Companion Manager / raid click-fix client files. Every file is pinned by SHA-256
in fork-update-manifest.json; files identical to v0.3 are left out. Native
client files may only replace a known v0.3 or fork build (guarded by hash in
Apply-OfflineDAoCFork.ps1). Never writes into the game installation and never
packages saves, account.txt, configuration, logs or bot settings.
"""
import argparse, hashlib, json, pathlib, re, shutil, sys, zipfile

REPO = pathlib.Path(__file__).resolve().parents[2]
RELEASE_TOOLS = pathlib.Path(__file__).resolve().parent
UPSTREAM_GAME_DLL = '67dcf68a37b95a93946a943b99d5e19b4a03e08cd6469275e25c7b909de21e99'
# The Companion Manager client stage packaged by default, and the COMPANIONS window files (custom7_window.xml)
# and companion paperdoll probe windows (custom6_window.xml) of earlier builds that the update pack may replace.
# The OPEN BG window (custom5_window.xml) was added in 0.282.0 and redesigned in 0.283.0 (same game.dll and
# uimain.xml), so the update pack may add it or replace the 0.282.0 window.
COMPANION_STAGE = 'stage-companion-manager-0.283.0'
KNOWN_BAR_WINDOWS = ['7920bcd32909a0e6b5ad2e625902f9f8807fe04ea4370f2ace2b301a8ee59aba']  # 0.251.0
KNOWN_GEAR_WINDOWS = ['60d41e0817463f6a17a1e2a053e960ae4d8f4f4b75b4aaf5e3f740d3dd626cd5',  # 0.273.0 probe
                      '9a47faa82bd3daaec0c99d26cbd6db12170a01f14af0805c3a384d295e1755ee']  # 0.275.0 probe
KNOWN_BG_WINDOWS = ['38739f7d407de64e83d361848a688883de9161b6f56b36970f5e2ecdc8273401']  # 0.282.0
SERVER_ASSEMBLIES = ['GameServer', 'CoreBase', 'CoreDatabase', 'CoreServer']
LAUNCHER_FILES = ['OfflineDAoC.dll', 'OfflineDAoC.exe', 'OfflineDAoC.pdb', 'OfflineDAoC.deps.json',
                  'OfflineDAoC.runtimeconfig.json', 'Join Friend.cmd']
PROTECTED = {'runtime/account.txt', 'runtime/server/config/serverconfig.xml',
             'runtime/server/bot-goals.json', 'runtime/server/rvr-world.json'}


def sha(path):
    h = hashlib.sha256()
    with open(path, 'rb') as f:
        for block in iter(lambda: f.read(1 << 20), b''):
            h.update(block)
    return h.hexdigest()


def fork_version():
    form = (REPO / 'source/tools/OfflineDaoc.Launcher/MainForm.cs').read_text(encoding='utf-8')
    version = re.search(r'DisplayVersion = "(\d+\.\d+\.\d+)"', form).group(1)
    changelog = (REPO / 'CHANGELOG.md').read_text(encoding='utf-8')
    latest = re.search(r'^## \[(\d+\.\d+\.\d+)\]', changelog, re.M).group(1)
    if latest != version:
        sys.exit(f'DisplayVersion {version} does not match the latest changelog heading {latest}.')
    return version


def changelog_section(version):
    text = (REPO / 'CHANGELOG.md').read_text(encoding='utf-8')
    match = re.search(rf'^## \[{re.escape(version)}\][^\n]*\n(.*?)(?=^## \[|\Z)', text, re.M | re.S)
    return match.group(1).strip() + '\n'


def crlf(path):
    data = path.read_bytes().replace(b'\r\n', b'\n').replace(b'\n', b'\r\n')
    path.write_bytes(data)


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--install-root', default='/mnt/d/Games/OfflineDAoC', help='v0.3-based install (read only)')
    p.add_argument('--server-build', default=str(REPO / 'source/server/Release'))
    p.add_argument('--launcher-build', default=str(REPO / 'source/tools/OfflineDaoc.Launcher/bin/Release/net10.0-windows'))
    p.add_argument('--navmesh-dir', help='default: <install-root>/runtime/server/pathing, or <install-root>-dev/battleground-nav/meshes when the install lacks one')
    p.add_argument('--companion-stage', help=f'default: <install-root>-dev/{COMPANION_STAGE}')
    p.add_argument('--raid-stage', help='default: <install-root>-dev/raid-click-fix-stage-0.33.0')
    p.add_argument('--output', required=True, help='new folder outside the installation')
    a = p.parse_args()

    install = pathlib.Path(a.install_root).resolve()
    dev = pathlib.Path(str(install) + '-dev')
    out = pathlib.Path(a.output).resolve()
    if out == install or install in out.parents:
        sys.exit('Output must be outside the game installation.')
    if out.exists():
        sys.exit(f'Output already exists; refusing to overwrite: {out}')
    version = fork_version()
    baseline = json.loads((install / 'PACKAGE FILE HASHES.json').read_text(encoding='utf-8-sig'))
    if baseline.get('runtime/client-opendaoc/app/game.dll') != UPSTREAM_GAME_DLL:
        sys.exit('Baseline manifest is not the Offline DAoC v0.3 package.')

    server = pathlib.Path(a.server_build)
    launcher = pathlib.Path(a.launcher_build)
    navmesh = pathlib.Path(a.navmesh_dir) if a.navmesh_dir else install / 'runtime/server/pathing'
    if not a.navmesh_dir and len(list(navmesh.glob('zone*.nav'))) != 11:
        staged = dev / 'battleground-nav/meshes'
        if len(list(staged.glob('zone*.nav'))) == 11:
            navmesh = staged
    companion = pathlib.Path(a.companion_stage) if a.companion_stage else dev / COMPANION_STAGE
    raid = pathlib.Path(a.raid_stage) if a.raid_stage else dev / 'raid-click-fix-stage-0.33.0'

    entries = []  # (relative path, source, guard, allowed)

    def add(rel, src, guard='any', allowed=None):
        src = pathlib.Path(src)
        if not src.is_file():
            sys.exit(f'Missing source for {rel}: {src}')
        entries.append((rel, src, guard, allowed or []))

    for name in SERVER_ASSEMBLIES:
        for ext in ('.dll', '.pdb'):
            built = server / (name + ext)
            if not built.is_file():
                built = server / 'lib' / (name + ext)
            for rel in (f'runtime/server/{name}{ext}', f'runtime/server/lib/{name}{ext}'):
                if rel in baseline:
                    add(rel, built)
    for name in LAUNCHER_FILES:
        add(f'runtime/{name}', launcher / name)
    navs = sorted(navmesh.glob('zone*.nav'))
    if len(navs) != 11:
        sys.exit(f'Expected the eleven battleground navmeshes in {navmesh}, found {len(navs)}.')
    for nav in navs:
        add(f'runtime/server/pathing/{nav.name}', nav)
    # World data packs (docs/TOA_DATA_PACKS.md): the server applies them to the save at its next start.
    packs = server / 'data-packs'
    if packs.is_dir():
        for pack in sorted(packs.iterdir()):
            if pack.is_file() and (pack.name == 'manifest.json' or pack.name.endswith(('.sql', '.sql.gz'))):
                add(f'runtime/server/data-packs/{pack.name}', pack)

    cm = json.loads((companion / 'manifest.json').read_text(encoding='utf-8'))
    if cm['baselineSha256'] != UPSTREAM_GAME_DLL or cm.get('protocolVersion') != 3:
        sys.exit('Companion Manager stage was not built from the verified v0.3 client.')
    if not cm.get('barWindowSha256') or not cm.get('uimainManagerOnlySha256'):
        sys.exit('Companion Manager stage predates the COMPANIONS window; rebuild it first.')
    if not cm.get('gearWindowSha256') or not cm.get('uimainBarSha256'):
        sys.exit('Companion Manager stage predates the COMPANION GEAR window; rebuild it first.')
    if not cm.get('bgWindowSha256') or not cm.get('uimainGearSha256'):
        sys.exit('Companion Manager stage predates the OPEN BG window; rebuild it first.')
    client = 'runtime/client-opendaoc/app/'
    known_games = [UPSTREAM_GAME_DLL, '3b6274dc385b90bf892f27d96c9e56cb457e1e94cbf45b5892d462a9e4d70890',
                   '88530c0093b285fd38fd6759e464373baa65ebb20473a949bc3b79fccbb41fd3',
                   '49095c51e792200daf824f8a00880c70815a0cacbc37f452cf062cffacb3b63d',
                   '73223deffd8455944974b555f5bce5762c43a642c4442b5102194e337e47daf4',   # 0.251.0
                   'c938e6b50057a4a99ec39211004765dcacb3ffa47880e4ba3fc193add87e612d',   # 0.272.0
                   'ff5fb5238fbf473c10503887e66ad1a8679d72342445bfa415e889ded6e5a022',   # 0.273.0 paperdoll probe
                   'f1f5c399ab79a6966b623746059e3e57a22665929a97a5ccff3e6ca093768394',   # 0.275.0 paperdoll probe
                   'bfefe97d7f7175204ae18c6d23a6958e13819e45155e84738bc5ae9c8d90c264']   # 0.276.0 (COMPANION GEAR window)
    add(client + 'game.dll', companion / 'game.dll', 'native', known_games)
    add(client + 'ui/uimain.xml', companion / 'uimain.xml', 'native',
        [cm['uimainBaselineSha256'], cm['uimainManagerOnlySha256'], cm['uimainBarSha256'],
         cm['uimainGearSha256']])  # the last is the 0.276.0 uimain.xml
    for skin in ('atlantis', 'isles'):
        add(client + f'ui/{skin}/custom8_window.xml', companion / skin / 'custom8_window.xml', 'native', ['absent'])
    for skin in ('atlantis', 'isles'):
        add(client + f'ui/{skin}/custom7_window.xml', companion / skin / 'custom7_window.xml', 'native',
            ['absent'] + KNOWN_BAR_WINDOWS)
    for skin in ('atlantis', 'isles'):
        add(client + f'ui/{skin}/custom6_window.xml', companion / skin / 'custom6_window.xml', 'native',
            ['absent'] + KNOWN_GEAR_WINDOWS)
    for skin in ('atlantis', 'isles'):
        add(client + f'ui/{skin}/custom5_window.xml', companion / skin / 'custom5_window.xml', 'native',
            ['absent'] + KNOWN_BG_WINDOWS)
    rm = json.loads((raid / 'manifest.json').read_text(encoding='utf-8'))
    for window, info in rm['windows'].items():
        for skin in ('atlantis', 'isles'):
            add(client + f'ui/{skin}/{window}', raid / skin / window, 'native', [info['baselineSha256']])
    add('ALL SERVER COMMANDS.txt', REPO / 'ALL SERVER COMMANDS.txt')
    add('UPDATE OFFLINE DAOC.cmd', RELEASE_TOOLS / 'UPDATE OFFLINE DAOC.cmd')
    add('tools/fork-update/Update-OfflineDAoC.ps1', RELEASE_TOOLS / 'Update-OfflineDAoC.ps1')

    folder = f'OfflineDAoC-Fork-{version}-Update'
    stage = out / folder
    files = []
    for rel, src, guard, allowed in entries:
        if rel.lower() in PROTECTED or rel.startswith('runtime/data/') or rel.endswith(('.log', '.sqlite3.db')):
            sys.exit(f'Refusing to package protected path: {rel}')
        target = stage / 'payload' / rel
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(src, target)
        if target.suffix.lower() in ('.cmd', '.ps1'):
            crlf(target)
        digest = sha(target)
        if baseline.get(rel) == digest:
            target.unlink()
            continue
        if guard == 'native':
            allowed = sorted(set(allowed) | ({baseline[rel]} if rel in baseline else set()))
        files.append({'Path': rel, 'SHA256': digest, 'Bytes': target.stat().st_size, 'Guard': guard, 'Allowed': allowed})
    if cm['outputSha256'] != next(f['SHA256'] for f in files if f['Path'].endswith('/game.dll')):
        sys.exit('Staged game.dll does not match its Companion Manager manifest.')

    manifest = {'Format': 1, 'Version': version, 'BaseVersion': '0.3', 'Repository': 'stefanrows/OfflineDAoC', 'Files': files}
    (stage / 'fork-update-manifest.json').write_text(json.dumps(manifest, indent=1) + '\n', encoding='utf-8')
    shutil.copyfile(RELEASE_TOOLS / 'Apply-OfflineDAoCFork.ps1', stage / 'Apply-OfflineDAoCFork.ps1')
    crlf(stage / 'Apply-OfflineDAoCFork.ps1')

    zip_name = folder + '.zip'
    zip_path = out / zip_name
    with zipfile.ZipFile(zip_path, 'x', compression=zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for f in sorted(stage.rglob('*')):
            if f.is_file():
                z.write(f, f.relative_to(out).as_posix())
    shutil.rmtree(stage)

    release = {'Format': 1, 'Version': version, 'BaseVersion': '0.3', 'Package': zip_name, 'RootFolder': folder,
               'SHA256': sha(zip_path), 'Bytes': zip_path.stat().st_size}
    (out / 'fork-release.json').write_text(json.dumps(release, indent=1) + '\n', encoding='utf-8')
    for name, asset in (('UPDATE OFFLINE DAOC.cmd', 'UPDATE-OFFLINE-DAOC.cmd'), ('Update-OfflineDAoC.ps1', 'Update-OfflineDAoC.ps1')):
        shutil.copyfile(RELEASE_TOOLS / name, out / asset)
        crlf(out / asset)
    intro = (
        '## Install or update\n\n'
        '1. Need the base game first? Download and extract the original '
        '[Offline DAoC v0.3](https://github.com/shadowofze/OfflineDAoC/releases/tag/v0.3) release.\n'
        '2. Close the game, server and launcher. Download **UPDATE-OFFLINE-DAOC.cmd** and '
        '**Update-OfflineDAoC.ps1** into your game folder (next to START OFFLINE DAOC.cmd) and run the .cmd.\n'
        '3. It downloads the update pack, checks its SHA-256, backs up every file it replaces under '
        '`update-backups`, and installs it. Saves, account.txt and configs are never touched. '
        'After the first update, run **UPDATE OFFLINE DAOC.cmd** from the game folder to get later releases.\n\n'
        'Coming from a v0.3 game with characters? The first launch asks for a one-time Camlann world reset '
        '(full backup first, local account kept). Old characters do not carry over.\n\n'
        f'## Changes in {version}\n\n')
    (out / 'release-notes.md').write_text(intro + changelog_section(version), encoding='utf-8')
    assets = ['fork-release.json', zip_name, 'UPDATE-OFFLINE-DAOC.cmd', 'Update-OfflineDAoC.ps1']
    (out / 'SHA256SUMS.txt').write_text(''.join(f'{sha(out / n)}  {n}\n' for n in assets), encoding='utf-8')
    print(f'Fork {version}: {len(files)} files, {zip_path.stat().st_size / 1024**2:.1f} MiB -> {out}')


if __name__ == '__main__':
    main()
