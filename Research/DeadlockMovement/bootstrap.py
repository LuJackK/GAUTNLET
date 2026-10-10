"""Install checksum-pinned Source 2 tools locally; Python 3.10+, no packages."""
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


def install(name, lock):
    asset = lock['assets'][name]
    cache = ROOT / '.downloads' / lock['version'] / name
    cache.parent.mkdir(parents=True, exist_ok=True)
    if not cache.exists() or sha256(cache) != asset['sha256']:
        print(f'Downloading {name} ({lock["version"]})', flush=True)
        temporary = cache.with_suffix('.part')
        request = urllib.request.Request(asset['url'], headers={'User-Agent': 'DeadlockMovementResearch'})
        with urllib.request.urlopen(request, timeout=120) as source, temporary.open('wb') as dest:
            shutil.copyfileobj(source, dest)
        if sha256(temporary) != asset['sha256']:
            raise ValueError(f'Checksum mismatch: {name}; download not installed')
        temporary.replace(cache)
    target = ROOT / '.tools' / lock['version'] / ('gui' if name.endswith('.exe') else 'cli')
    target.mkdir(parents=True, exist_ok=True)
    if name.endswith('.zip'):
        with zipfile.ZipFile(cache) as archive:
            for entry in archive.infolist():
                resolved = (target / entry.filename).resolve()
                if not resolved.is_relative_to(target.resolve()):
                    raise ValueError(f'Unsafe archive path: {entry.filename}')
            archive.extractall(target)
        if os.name != 'nt':
            for executable in target.rglob('Source2Viewer-CLI'):
                executable.chmod(executable.stat().st_mode | stat.S_IXUSR | stat.S_IXGRP | stat.S_IXOTH)
    else:
        shutil.copy2(cache, target / name)
    print(f'Installed: {target}', flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--gui', action='store_true', help='Also install the Windows viewer')
    args = parser.parse_args()
    if args.gui and platform.system() != 'Windows':
        parser.error('--gui is Windows-only; the CLI works on all supported hosts')
    lock = json.loads((ROOT / 'tools.lock.json').read_text(encoding='utf-8-sig'))
    install(host_asset(), lock)
    if args.gui:
        install('Source2Viewer.exe', lock)


if __name__ == '__main__':
    main()
