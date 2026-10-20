"""Build the pinned native observer with a local Windows x64 Zig toolchain.

Dependencies and artifacts stay under ignored local/. Never edits the game.
See capture/README.md for pinned portable tool installation.
"""
import argparse
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import subprocess
import sys

from capture_session import ROOT, MODLOCK_REVISION, SessionError, prepare, _hash_file, _source_hashes

DEPENDENCIES = {
    'abseil-cpp': '4bb56e2f901742e0e9289abdc06dd6a234ad328c',
    'protobuf': '8201686529c4690225a14bd80a9418b53fedc3d2',
}
CXX_FLAGS = '-target x86_64-windows-gnu -U_LIBCPP_ENABLE_CXX17_REMOVED_UNEXPECTED_FUNCTIONS -D_LIBCPP_ENABLE_EXPERIMENTAL'


def command(args, *, cwd=ROOT, log=None):
    result = subprocess.run(list(map(str, args)), cwd=cwd, shell=False,
                            stdout=log, stderr=subprocess.STDOUT if log else None)
    if result.returncode:
        raise SessionError(f'Command failed ({result.returncode}): {args[0]}; inspect local/capture-build.log')


def git_output(directory, *args):
    return subprocess.check_output(['git', '-C', str(directory), *args], text=True).strip()


def dependency(checkout, name, revision):
    url = f'https://github.com/aperturerobotics/{name}.git'
    path = checkout / 'vendor' / 'github.com' / 'aperturerobotics' / name
    if not path.exists():
        path.parent.mkdir(parents=True, exist_ok=True)
        command(['git', 'clone', '--no-checkout', url, path])
        command(['git', 'checkout', '--detach', revision], cwd=path)
    if (git_output(path, 'rev-parse', 'HEAD') != revision or
            git_output(path, 'remote', 'get-url', 'origin') != url or
            git_output(path, 'status', '--porcelain')):
        raise SessionError(f'Dependency is not the clean pinned checkout: {path}. No reset performed.')


def tool(argument, fallback):
    path = Path(argument).resolve() if argument else ROOT / fallback
    if not path.is_file():
        raise SessionError(f'Missing portable build tool: {path}; see capture/README.md')
    return path


def build(args):
    if sys.platform != 'win32':
        raise SessionError('This build driver targets Windows x64. See upstream for other toolchains.')
    cmake = tool(args.cmake, 'local/cmake-tools/cmake/data/bin/cmake.exe')
    ninja = tool(args.ninja, 'local/cmake-tools/bin/ninja.exe')
    zig = tool(args.zig, 'local/build-tools-016/ziglang/zig.exe')
    if subprocess.check_output([str(zig), 'version'], text=True).strip() != '0.16.0':
        raise SessionError('This build recipe is pinned to Zig 0.16.0.')
    prepare()
    checkout = ROOT / 'local/modlock'
    for name, revision in DEPENDENCIES.items():
        dependency(checkout, name, revision)
    sdk = ROOT / 'local/capture-sdk'
    sdk_bin = sdk / 'bin'
    # An interrupted rebuild must never leave a previous success stamp valid.
    stamp = sdk_bin / 'capture-build.json'
    if stamp.is_file():
        stamp.unlink()
    common = ['-G', 'Ninja', '-DCMAKE_BUILD_TYPE=Release',
              f'-DCMAKE_TOOLCHAIN_FILE={(checkout / "cmake/zig-windows.cmake").as_posix()}',
              f'-DMODLOCK_ZIG_EXECUTABLE={zig.as_posix()}',
              f'-DCMAKE_MAKE_PROGRAM={ninja.as_posix()}', f'-DCMAKE_CXX_FLAGS={CXX_FLAGS}']
    sdk_build = checkout / 'build-capture-zig016'
    plugin_build = ROOT / 'local/capture-plugin-build-zig016'
    print('Building native SDK and recorder; diagnostics: local/capture-build.log', flush=True)
    with (ROOT / 'local/capture-build.log').open('w', encoding='utf-8') as log:
        command([cmake, '-S', checkout, '-B', sdk_build, *common,
                 '-DBUILD_TESTING=OFF', '-DMODLOCK_BUILD_SCRIPT_RUNTIMES=OFF'], log=log)
        command([cmake, '--build', sdk_build, '--parallel', args.jobs], log=log)
        command([cmake, '--install', sdk_build, '--prefix', sdk], log=log)
        command([cmake, '-S', ROOT / 'capture/native', '-B', plugin_build, *common,
                 f'-DCMAKE_PREFIX_PATH={sdk.as_posix()}', '-DBUILD_TESTING=ON'], log=log)
        command([cmake, '--build', plugin_build, '--parallel', args.jobs], log=log)
        command([cmake, '--install', plugin_build, '--prefix', sdk], log=log)
        command([plugin_build / 'capture_core_test.exe'], log=log)
    files = {p.name: _hash_file(p)['sha256'] for p in sorted(sdk_bin.iterdir())
             if p.suffix.lower() in ('.dll', '.exe') or p.name == 'game_signatures.txtpb'}
    payload = {'revision': MODLOCK_REVISION, 'sources': _source_hashes(ROOT), 'files': files,
               'built_utc': datetime.now(timezone.utc).isoformat(), 'dependencies': DEPENDENCIES,
               'tools': {p.name: _hash_file(p) for p in (cmake, ninja, zig)},
               'native_compatibility': 'unverified', 'script_runtimes': False}
    with stamp.open('x', encoding='utf-8', newline='\n') as output:
        json.dump(payload, output, indent=2)
        output.write('\n')
    print(f'Built: {sdk_bin}\nNext: python capture_session.py run --sdk-bin local/capture-sdk/bin --run synthetic-01 --check-only')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--cmake')
    parser.add_argument('--ninja')
    parser.add_argument('--zig')
    parser.add_argument('--jobs', type=int, default=max(1, min(4, (os.cpu_count() or 2) // 2)))
    args = parser.parse_args()
    if not 1 <= args.jobs <= 64:
        parser.error('--jobs must be between 1 and 64')
    try:
        build(args)
        return 0
    except (OSError, subprocess.CalledProcessError, SessionError) as error:
        print(str(error), file=sys.stderr)
        return 1


if __name__ == '__main__':
    sys.exit(main())
