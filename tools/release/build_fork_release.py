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
    p.add_argument('--navmesh-dir', help='default: <install-root>/runtime/server/pathing')
    p.add_argument('--companion-stage', help='default: <install-root>-dev/companion-manager-stage-0.169.0')
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
    companion = pathlib.Path(a.companion_stage) if a.companion_stage else dev / 'companion-manager-stage-0.169.0'
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
    if len(navs) != 10:
        sys.exit(f'Expected the ten battleground navmeshes in {navmesh}, found {len(navs)}.')
    for nav in navs:
        add(f'runtime/server/pathing/{nav.name}', nav)

    cm = json.loads((companion / 'manifest.json').read_text(encoding='utf-8'))
    if cm['baselineSha256'] != UPSTREAM_GAME_DLL or cm.get('protocolVersion') != 3:
        sys.exit('Companion Manager stage was not built from the verified v0.3 client.')
    client = 'runtime/client-opendaoc/app/'
    known_games = [UPSTREAM_GAME_DLL, '3b6274dc385b90bf892f27d96c9e56cb457e1e94cbf45b5892d462a9e4d70890',
                   '88530c0093b285fd38fd6759e464373baa65ebb20473a949bc3b79fccbb41fd3']
    add(client + 'game.dll', companion / 'game.dll', 'native', known_games)
    add(client + 'ui/uimain.xml', companion / 'uimain.xml', 'native', [cm['uimainBaselineSha256']])
    for skin in ('atlantis', 'isles'):
        add(client + f'ui/{skin}/custom8_window.xml', companion / skin / 'custom8_window.xml', 'native', ['absent'])
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
