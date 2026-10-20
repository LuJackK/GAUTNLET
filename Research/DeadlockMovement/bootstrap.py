"""Install checksum-pinned research tools locally; Python 3.10+, no packages."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import shutil
import stat
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parent


def sha256(path):
    digest = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            digest.update(block)
    return digest.hexdigest()


def host_asset():
    system = {'Windows': 'windows', 'Linux': 'linux', 'Darwin': 'macos'}.get(platform.system())
    arch = {'amd64': 'x64', 'x86_64': 'x64', 'arm64': 'arm64', 'aarch64': 'arm64'}.get(platform.machine().lower())
    if not system or not arch:
        raise ValueError(f'Unsupported host: {platform.system()} {platform.machine()}')
    return f'cli-{system}-{arch}.zip'


def download(name, url, digest, cache):
    """Return a cached archive whose SHA-256 matches the pinned digest."""
    cache.parent.mkdir(parents=True, exist_ok=True)
    if not cache.exists() or sha256(cache) != digest:
        print(f'Downloading {name}', flush=True)
        temporary = cache.with_suffix('.part')
        request = urllib.request.Request(url, headers={'User-Agent': 'DeadlockMovementResearch'})
        with urllib.request.urlopen(request, timeout=120) as source, temporary.open('wb') as dest:
            shutil.copyfileobj(source, dest)
        if sha256(temporary) != digest:
            raise ValueError(f'Checksum mismatch: {name}; download not installed')
        temporary.replace(cache)
    return cache


def extract_zip(cache, target):
    with zipfile.ZipFile(cache) as archive:
        for entry in archive.infolist():
            resolved = (target / entry.filename).resolve()
            if not resolved.is_relative_to(target.resolve()):
                raise ValueError(f'Unsafe archive path: {entry.filename}')
        archive.extractall(target)


def install(name, lock):
    asset = lock['assets'][name]
    cache = download(f'{name} ({lock["version"]})', asset['url'], asset['sha256'],
                     ROOT / '.downloads' / lock['version'] / name)
    target = ROOT / '.tools' / lock['version'] / ('gui' if name.endswith('.exe') else 'cli')
    target.mkdir(parents=True, exist_ok=True)
    if name.endswith('.zip'):
        extract_zip(cache, target)
        if os.name != 'nt':
            for executable in target.rglob('Source2Viewer-CLI'):
                executable.chmod(executable.stat().st_mode | stat.S_IXUSR | stat.S_IXGRP | stat.S_IXOTH)
    else:
        shutil.copy2(cache, target / name)
    print(f'Installed: {target}', flush=True)


def ghidra_paths(lock):
    """Installed Ghidra and JDK roots for ghidra.lock.json; the JDK is None off Windows x64."""
    ghidra = ROOT / '.tools' / 'ghidra' / lock['ghidra']['version'] / lock['ghidra']['root']
    jdk = None
    if host_asset() == 'cli-windows-x64.zip':
        jdk = ROOT / '.tools' / 'jdk' / lock['jdk']['version'] / lock['jdk']['root']
    return ghidra, jdk


def install_ghidra():
    lock = json.loads((ROOT / 'ghidra.lock.json').read_text(encoding='utf-8-sig'))
    ghidra, jdk = ghidra_paths(lock)
    for tool, root, asset in (('ghidra', ghidra, lock['ghidra']['asset']),
                              ('jdk', jdk, lock['jdk']['assets'].get('windows-x64'))):
        if root is None:
            print('No pinned JDK for this host; Ghidra needs JDK 21 on JAVA_HOME.', flush=True)
            continue
        if root.is_dir():
            print(f'Already installed: {root}', flush=True)
            continue
        version = lock[tool]['version']
        cache = download(f'{asset["name"]}', asset['url'], asset['sha256'],
                         ROOT / '.downloads' / tool / version / asset['name'])
        extract_zip(cache, root.parent)
        if not root.is_dir():
            raise ValueError(f'Archive did not contain the expected root: {root}')
        if os.name != 'nt':
            for script in root.rglob('*'):
                if script.is_file() and (script.suffix == '' or script.suffix == '.sh'):
                    script.chmod(script.stat().st_mode | stat.S_IXUSR)
        print(f'Installed: {root}', flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--gui', action='store_true', help='Also install the Windows viewer')
    parser.add_argument('--ghidra', action='store_true',
                        help='Also install pinned Ghidra and, on Windows x64, its JDK (about 700 MB)')
    args = parser.parse_args()
    if args.gui and platform.system() != 'Windows':
        parser.error('--gui is Windows-only; the CLI works on all supported hosts')
    lock = json.loads((ROOT / 'tools.lock.json').read_text(encoding='utf-8-sig'))
    install(host_asset(), lock)
    if args.gui:
        install('Source2Viewer.exe', lock)
    if args.ghidra:
        install_ghidra()


if __name__ == '__main__':
    main()
