"""Prepare the pinned observer SDK and record auditable, unverified captures.

No install/build automation or game client launch is performed. A successful host
exit and a valid JSONL transport do not establish native game compatibility.
"""

import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import stat
import subprocess
import sys
import tempfile
import time
import uuid

from capture_frames import IDENTIFIER, MARKER_EVENTS, validate_frames
from capture_trace import MAX_DETAILS, validate_capture


ROOT = Path(__file__).resolve().parent
MODLOCK_URL = 'https://github.com/paralin/modlock'
MODLOCK_REVISION = '67478b9bbc54ff1f94ccfdb5f9ce0e08dc6f6418'
HELPER_PATH = 'include/modlock/gameinterop/capture_observe_step.h'
BINARIES = ('modlock-host.exe', 'movement_capture.dll')
SDK_DLL_NAMES = ('modlock_sdk.dll', 'libmodlock_sdk.dll')
SIGNATURES = 'game_signatures.txtpb'
BUILD_MANIFEST = 'capture-build.json'


class SessionError(ValueError):
    """Preparation or launch preconditions were not satisfied."""


def _git(checkout, *arguments, env=None, accepted=(0,)):
    result = subprocess.run(['git', '-C', str(checkout), *map(str, arguments)],
                            shell=False, capture_output=True, text=True,
                            encoding='utf-8', errors='replace', env=env)
    if result.returncode not in accepted:
        raise SessionError(f'git {arguments[0]} failed ({result.returncode}): '
                           f'{result.stderr.strip() or result.stdout.strip()}')
    return result


def _require_file(path):
    if not path.is_file():
        raise SessionError(f'Required file is missing: {path}')
    return path


def _hash_file(path):
    _require_file(path)
    digest = hashlib.sha256()
    with path.open('rb') as source:
        for block in iter(lambda: source.read(1024 * 1024), b''):
            digest.update(block)
    return {'path': str(path.resolve()), 'size_bytes': path.stat().st_size,
            'sha256': digest.hexdigest()}


def _patch_paths(patch):
    paths = []
    for line in patch.read_text(encoding='utf-8').splitlines():
        if not line.startswith('+++ '):
            continue
        if not line.startswith('+++ b/'):
            raise SessionError('Observer patch must only update named repository files.')
        name = line[6:]
        path = PurePosixPath(name)
        if not name or path.is_absolute() or '..' in path.parts or '\\' in name:
            raise SessionError('Observer patch contains an unsafe path.')
        paths.append(name)
    if not paths or len(paths) != len(set(paths)):
        raise SessionError('Observer patch has no file updates or duplicate paths.')
    return paths


def _inspect_checkout(root):
    checkout = root / 'local' / 'modlock'
    patch = _require_file(root / 'capture' / 'modlock-observer.patch')
    helper_source = _require_file(root / 'capture' / 'native' / 'observe_step.h')
    if not checkout.is_dir():
        raise SessionError('Pinned SDK checkout is missing; run prepare first.')
    top = Path(_git(checkout, 'rev-parse', '--show-toplevel').stdout.strip()).resolve()
    if top != checkout.resolve():
        raise SessionError('SDK directory must be its own Git checkout.')
    head = _git(checkout, 'rev-parse', 'HEAD').stdout.strip()
    if head != MODLOCK_REVISION:
        raise SessionError(f'Refusing SDK HEAD {head}; expected {MODLOCK_REVISION}. No files were reset.')
    origin = _git(checkout, 'remote', 'get-url', 'origin').stdout.strip().rstrip('/')
    if origin.removesuffix('.git') != MODLOCK_URL:
        raise SessionError(f'Refusing unexpected SDK origin: {origin}')
    staged = _git(checkout, 'diff', '--cached', '--quiet', 'HEAD', '--', accepted=(0, 1))
    if staged.returncode:
        raise SessionError('Refusing staged SDK changes; no files were reset.')
    paths = _patch_paths(patch)
    changed = set(filter(None, _git(checkout, 'diff', '--name-only', '-z', 'HEAD', '--').stdout.split('\0')))
    untracked = set(filter(None, _git(checkout, 'ls-files', '--others', '--exclude-standard', '-z').stdout.split('\0')))
    unexpected = (changed - set(paths)) | (untracked - {HELPER_PATH})
    if unexpected:
        raise SessionError('Refusing unexpected SDK changes: ' + ', '.join(sorted(unexpected)))
    helper = checkout / HELPER_PATH
    if helper.exists() and (not helper.is_file() or helper.read_bytes() != helper_source.read_bytes()):
        raise SessionError('Existing observer helper differs from the capture source; refusing overwrite.')
    # A separate index computes exact expected patched blobs without touching the
    # real index, resetting the checkout, or accepting unrelated edits in a hunk.
    with tempfile.TemporaryDirectory(prefix='modlock-observer-index-') as temporary:
        environment = os.environ.copy()
        environment['GIT_INDEX_FILE'] = str(Path(temporary) / 'index')
        _git(checkout, 'read-tree', 'HEAD', env=environment)
        _git(checkout, 'apply', '--cached', str(patch.resolve()), env=environment)
        expected = {}
        for name in paths:
            expected[name] = _git(checkout, 'rev-parse', ':' + name, env=environment).stdout.strip()
        actual = {}
        original = {}
        for name in paths:
            _require_file(checkout / name)
            actual[name] = _git(checkout, 'hash-object', '--path=' + name, name).stdout.strip()
            original[name] = _git(checkout, 'rev-parse', 'HEAD:' + name).stdout.strip()
    if actual == expected:
        applied = True
    elif actual == original:
        applied = False
    else:
        raise SessionError('SDK edits are not exactly the current observer patch; refusing to alter them.')
    return {'head': head, 'patch_applied': applied, 'helper_present': helper.is_file()}


def prepare(root=ROOT):
    root = Path(root).resolve()
    _require_file(root / 'capture' / 'modlock-observer.patch')
    _require_file(root / 'capture' / 'native' / 'observe_step.h')
    checkout = root / 'local' / 'modlock'
    if not checkout.exists():
        checkout.parent.mkdir(parents=True, exist_ok=True)
        result = subprocess.run(['git', 'clone', '--no-checkout', MODLOCK_URL, str(checkout)],
                                shell=False, capture_output=True, text=True)
        if result.returncode:
            raise SessionError(f'Clone failed; existing partial files were preserved: {result.stderr.strip()}')
        _git(checkout, 'checkout', '--detach', MODLOCK_REVISION)
    state = _inspect_checkout(root)
    if not state['patch_applied']:
        _git(checkout, 'apply', '--check', str(root / 'capture' / 'modlock-observer.patch'))
        _git(checkout, 'apply', str(root / 'capture' / 'modlock-observer.patch'))
    if not state['helper_present']:
        helper = checkout / HELPER_PATH
        helper.parent.mkdir(parents=True, exist_ok=True)
        with helper.open('xb') as destination:
            destination.write((root / 'capture' / 'native' / 'observe_step.h').read_bytes())
    verified = _inspect_checkout(root)
    return {'status': 'prepared', 'native_compatibility': 'unverified',
            'checkout': str(checkout), 'repository': MODLOCK_URL, **verified}


def doctor(sdk_bin=None):
    tools = {name: shutil.which(name) for name in ('git', 'cmake', 'go')}
    compilers = {name: shutil.which(name) for name in ('cl', 'clang-cl', 'clang++', 'g++', 'zig')}
    report = {'tools': tools, 'compilers': compilers,
              'compiler_present': any(compilers.values()), 'native_compatibility': 'unverified',
              'note': 'Presence checks only; buildability, architecture and binary compatibility are unverified.'}
    report['ready_tools'] = all(tools.values()) and report['compiler_present']
    report['ready'] = report['ready_tools']
    if sdk_bin is not None:
        directory = Path(sdk_bin).expanduser().resolve()
        sdk_names = [name for name in SDK_DLL_NAMES if (directory / name).is_file()]
        report['sdk_bin'] = str(directory)
        report['binaries'] = {name: (directory / name).is_file() for name in BINARIES}
        report['binaries']['sdk_dll'] = len(sdk_names) == 1
        report['sdk_dll'] = sdk_names[0] if len(sdk_names) == 1 else None
        report['sdk_dll_candidates'] = sdk_names
        report['signatures_present'] = (directory / SIGNATURES).is_file()
        report['build_manifest_present'] = (directory / BUILD_MANIFEST).is_file()
        report['ready'] = all(report['binaries'].values()) and report['signatures_present']
    return report


def _source_hashes(root):
    native = root / 'capture' / 'native'
    required = [root / 'capture' / 'modlock-observer.patch',
                *(native / name for name in ('observe_step.h', 'observer_plugin.cc',
                                             'recorder_core.h', 'CMakeLists.txt'))]
    for path in required:
        _require_file(path)
    paths = set(required)
    paths.update(path for path in native.rglob('*')
                 if path.is_file() and path.suffix in ('.h', '.cc', '.txt'))
    paths.update(path for path in (root / 'capture_session.py', root / 'capture_trace.py',
                                  root / 'capture_frames.py', root / 'capture_build.py') if path.is_file())
    return {path.relative_to(root).as_posix(): _hash_file(path) for path in sorted(paths)}


def _runtime_hashes(directory):
    for name in BINARIES:
        _require_file(directory / name)
    sdk_names = [name for name in SDK_DLL_NAMES if (directory / name).is_file()]
    if len(sdk_names) != 1:
        raise SessionError('SDK bin must contain exactly one of ' + ' or '.join(SDK_DLL_NAMES) + '.')
    return {path.name: _hash_file(path) for path in sorted(directory.iterdir())
            if path.is_file() and path.suffix.lower() in ('.exe', '.dll')}


def _verify_build_manifest(directory, sources, binaries, signatures):
    path = _require_file(directory / BUILD_MANIFEST)
    try:
        build = json.loads(path.read_text(encoding='utf-8'))
    except (ValueError, UnicodeError) as error:
        raise SessionError(f'Invalid capture build manifest: {error}') from error
    if not isinstance(build, dict) or build.get('revision') != MODLOCK_REVISION:
        raise SessionError('Capture build manifest does not match the pinned SDK revision; rebuild.')
    if build.get('sources') != sources:
        raise SessionError('Capture sources changed since the recorded build; rebuild before capture.')
    files = build.get('files')
    if not isinstance(files, dict) or not files:
        raise SessionError('Capture build manifest must record executable, DLL and signature hashes.')
    current = {name: value['sha256'] for name, value in binaries.items()}
    current[SIGNATURES] = signatures['sha256']
    if set(files) != set(current):
        raise SessionError('SDK executable/DLL/signatures inventory differs from the recorded build; rebuild.')
    if any(not isinstance(value, str) or not re.fullmatch(r'[0-9a-fA-F]{64}', value)
           or value.lower() != current[name] for name, value in files.items()):
        raise SessionError('SDK executable/DLL/signatures bytes differ from the recorded build; rebuild.')
    return _hash_file(path)


def _steam_build_id(game_dir):
    if len(game_dir.parents) < 2:
        return None
    manifest = game_dir.parents[1] / 'appmanifest_1422450.acf'
    if not manifest.is_file():
        return None
    try:
        match = re.search(r'"buildid"\s*"([0-9]+)"', manifest.read_text(encoding='utf-8-sig'), re.IGNORECASE)
    except (OSError, UnicodeError):
        return None
    return match.group(1) if match else None


def _save_manifest(path, manifest, initial=False):
    with path.open('x' if initial else 'w', encoding='utf-8', newline='\n') as destination:
        json.dump(manifest, destination, indent=2, allow_nan=False)
        destination.write('\n')


def _running_live_directory(run, root):
    if not re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9_-]{0,79}', run):
        raise SessionError('Run slug must be 1-80 letters, digits, underscores or hyphens.')
    destination = Path(root).resolve() / 'local' / 'captures' / run
    manifest_path = _require_file(destination / 'manifest.json')
    try:
        manifest = json.loads(manifest_path.read_text(encoding='utf-8'))
    except (ValueError, UnicodeError) as error:
        raise SessionError(f'Invalid capture run manifest: {error}') from error
    if (not isinstance(manifest, dict) or manifest.get('transport_status') != 'running'
            or manifest.get('synthetic') is not False):
        raise SessionError('This request requires a running, non-synthetic capture.')
    return destination


def stop_capture(run, *, root=ROOT):
    destination = _running_live_directory(run, root)
    request = destination / 'stop.request'
    already_requested = False
    try:
        with request.open('xb'):
            pass
    except OSError as error:
        # Windows reports an existing directory as PermissionError rather than
        # FileExistsError. Inspect the existing entry without following links.
        try:
            mode = request.lstat().st_mode
        except FileNotFoundError:
            raise error
        if not stat.S_ISREG(mode):
            raise SessionError('Existing stop.request must be a regular file.') from error
        already_requested = True
    return {'status': 'stop_requested', 'run_directory': str(destination),
            'request_file': str(request), 'already_requested': already_requested,
            'note': 'The plugin will request graceful server shutdown; no process was killed.'}


def mark_capture(run, trial, event, *, root=ROOT):
    if not isinstance(trial, str) or not IDENTIFIER.fullmatch(trial):
        raise SessionError('Trial ID must be 1-64 ASCII letters, digits, underscores or hyphens.')
    if event not in MARKER_EVENTS:
        raise SessionError('Marker event must be arm, start, end or abort.')
    directory = _running_live_directory(run, root) / 'marker-requests'
    directory.mkdir(exist_ok=True)
    if not stat.S_ISDIR(directory.lstat().st_mode):
        raise SessionError('Marker inbox must be a regular directory, not a link.')
    request_id = f'{time.time_ns():020d}_{uuid.uuid4().hex}'
    target = directory / (request_id + '.request')
    if target.exists() or target.is_symlink():
        raise SessionError('Marker request ID already exists; refusing overwrite.')
    temporary = directory / (request_id + '.tmp')
    with temporary.open('xb') as output:
        output.write(f'{request_id}\n{trial}\n{event}\n'.encode('ascii'))
        output.flush()
        os.fsync(output.fileno())
    if os.name == 'nt':
        # Windows rename publishes complete bytes atomically and refuses an
        # existing target. The POSIX link equivalent retains that no-overwrite
        # guarantee, where ordinary rename would replace an existing request.
        os.rename(temporary, target)
    else:
        os.link(temporary, target)
        temporary.unlink()
    return {'status': 'queued', 'acknowledged': False, 'request_id': request_id,
            'trial_id': trial, 'event': event, 'request_file': str(target),
            'note': 'Queued only; confirm the matching request_id in frames.jsonl. '
                    'Marker clock timestamps describe native consumption, not action time.'}


def _check_session_mode(validation, check_only):
    if validation['synthetic'] is not None and validation['synthetic'] != check_only:
        validation['valid'] = False
        validation['error_count'] += 1
        if len(validation['errors']) < MAX_DETAILS:
            validation['errors'].append({'code': 'session_mode_mismatch', 'line': None,
                                         'message': 'Capture synthetic flag does not match requested run mode.'})
        validation['errors_truncated'] = validation['error_count'] > len(validation['errors'])


def run_capture(sdk_bin, run, *, check_only=False, game_dir=None, map_name='dl_midtown',
                port=27067, root=ROOT, solo_probe=False, solo_drop=False):
    root = Path(root).resolve()
    if not re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9_-]{0,79}', run):
        raise SessionError('Run slug must be 1-80 letters, digits, underscores or hyphens.')
    if not re.fullmatch(r'[A-Za-z0-9_]{1,80}', map_name):
        raise SessionError('Map must be 1-80 letters, digits or underscores.')
    if type(port) is not int or not 1 <= port <= 65535:
        raise SessionError('Port must be an integer from 1 through 65535.')
    if type(check_only) is not bool or check_only == (game_dir is not None):
        raise SessionError('Choose exactly one of check-only or an absolute game directory.')
    if type(solo_probe) is not bool or (solo_probe and check_only):
        raise SessionError('Solo probe requires a live game directory.')
    if type(solo_drop) is not bool or (solo_drop and not solo_probe):
        raise SessionError('Solo drop requires --solo-probe.')
    state = _inspect_checkout(root)
    if not state['patch_applied'] or not state['helper_present']:
        raise SessionError('Observer SDK is not fully prepared; run prepare first.')
    sdk_bin = Path(sdk_bin).expanduser().resolve()
    binaries = _runtime_hashes(sdk_bin)
    signatures = _hash_file(sdk_bin / SIGNATURES)
    sources = _source_hashes(root)
    build_manifest = _verify_build_manifest(sdk_bin, sources, binaries, signatures)
    game_binaries = {}
    steam_build_id = None
    if game_dir is not None:
        game_dir = Path(game_dir).expanduser()
        if not game_dir.is_absolute():
            raise SessionError('Game directory must be absolute.')
        game_dir = game_dir.resolve()
        game_binaries = {name: _hash_file(game_dir / relative) for name, relative in (
            ('server.dll', 'game/citadel/bin/win64/server.dll'),
            ('engine2.dll', 'game/bin/win64/engine2.dll'),
            ('schemasystem.dll', 'game/bin/win64/schemasystem.dll'))}
        steam_build_id = _steam_build_id(game_dir)
    captures = root / 'local' / 'captures'
    captures.mkdir(parents=True, exist_ok=True)
    destination = captures / run
    try:
        destination.mkdir()
    except FileExistsError as error:
        raise SessionError(f'Run already exists; refusing overwrite: {destination}') from error
    trace = destination / 'trace.jsonl'
    frames = destination / 'frames.jsonl'
    arguments = [str(sdk_bin / 'modlock-host.exe'), '--plugin', str(sdk_bin / 'movement_capture.dll')]
    if check_only:
        arguments.append('--check-plugin')
    else:
        arguments.extend(['--game-dir', str(game_dir), '--map', map_name,
                          '--hostport', str(port), '--engine-args', '+ip 127.0.0.1 +sv_hibernate_when_empty 0'])
    arguments.extend(['--', '--capture-output', str(trace)])
    if solo_probe:
        arguments.append('--solo-probe')
    if solo_drop:
        arguments.append('--solo-drop')
    manifest = {'manifest_version': 1, 'created_utc': datetime.now(timezone.utc).isoformat(),
                'run': run, 'status': 'unverified', 'native_compatibility': 'unverified',
                'synthetic': check_only,
                'solo_probe': solo_probe,
                'solo_drop': solo_drop,
                'evidence_status': 'synthetic_only_not_real_game_evidence' if check_only else
                                   'live_attempt_compatibility_unverified',
                'repository': MODLOCK_URL, 'revision': state['head'],
                'sdk_binaries': binaries, 'signatures': signatures, 'sources': sources,
                'build_manifest': build_manifest, 'build_provenance_status': 'hashes_verified',
                'game_binaries': game_binaries, 'steam_build_id': steam_build_id,
                'game_dir': str(game_dir) if game_dir is not None else None,
                'map': map_name if not check_only else None, 'port': port if not check_only else None,
                'argv': arguments, 'cwd': str(sdk_bin), 'trace': str(trace),
                'frames': str(frames), 'frame_schema_version': 1,
                'transport_status': 'running', 'host_returncode': None,
                'validation_report': str(destination / 'validation.json'),
                'frame_validation_report': str(destination / 'frame-validation.json'),
                'note': 'Host return code and file validation assess transport only; '
                        'native layout, units and timing compatibility remain unverified.'}
    manifest_path = destination / 'manifest.json'
    _save_manifest(manifest_path, manifest, initial=True)
    print(f'Capture run directory: {destination}', file=sys.stderr, flush=True)
    if check_only:
        print('Starting synthetic plugin lifecycle check.', file=sys.stderr, flush=True)
    else:
        stop_command = subprocess.list2cmdline([sys.executable, str(root / 'capture_session.py'),
                                               'stop', '--run', run])
        print('Starting local server capture; native compatibility remains unverified.', file=sys.stderr)
        print(f'Stop gracefully: {stop_command}', file=sys.stderr, flush=True)
    launch_error = None
    try:
        with (destination / 'stdout.log').open('xb') as stdout, (destination / 'stderr.log').open('xb') as stderr:
            result = subprocess.run(arguments, shell=False, cwd=str(sdk_bin), stdout=stdout, stderr=stderr)
        manifest['host_returncode'] = result.returncode
    except (OSError, KeyboardInterrupt) as error:
        launch_error = 'Interrupted by user.' if isinstance(error, KeyboardInterrupt) else str(error)
        manifest['launch_error'] = launch_error
    validation = validate_capture(trace)
    _check_session_mode(validation, check_only)
    frame_validation = validate_frames(frames, trace, movement_report=validation)
    _check_session_mode(frame_validation, check_only)
    with (destination / 'validation.json').open('x', encoding='utf-8', newline='\n') as output:
        json.dump(validation, output, indent=2, allow_nan=False)
        output.write('\n')
    with (destination / 'frame-validation.json').open('x', encoding='utf-8', newline='\n') as output:
        json.dump(frame_validation, output, indent=2, allow_nan=False)
        output.write('\n')
    both_valid = validation['valid'] and frame_validation['valid']
    okay = launch_error is None and manifest['host_returncode'] == 0 and both_valid
    manifest['transport_status'] = 'validated' if okay else 'failed'
    manifest['validation_valid'] = both_valid
    manifest['movement_validation_valid'] = validation['valid']
    manifest['frame_validation_valid'] = frame_validation['valid']
    manifest['finished_utc'] = datetime.now(timezone.utc).isoformat()
    _save_manifest(manifest_path, manifest)
    return {'exit_code': 0 if okay else 1, 'run_directory': str(destination),
            'manifest': str(manifest_path), 'transport_status': manifest['transport_status'],
            'synthetic': check_only, 'native_compatibility': 'unverified'}


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest='command', required=True)
    commands.add_parser('prepare', help='Clone the pinned SDK and apply the exact observer additions.')
    inspect = commands.add_parser('doctor', help='Report tool and optional binary presence only.')
    inspect.add_argument('--sdk-bin', type=Path)
    stop = commands.add_parser('stop', help='Request graceful shutdown of a running live capture.')
    stop.add_argument('--run', required=True)
    mark = commands.add_parser('mark', help='Queue a trial marker; acknowledgement appears in frames.jsonl.')
    mark.add_argument('--run', required=True)
    mark.add_argument('--trial', required=True)
    mark.add_argument('--event', choices=MARKER_EVENTS, required=True)
    run = commands.add_parser('run', help='Record a new synthetic check or a local server capture.')
    run.add_argument('--sdk-bin', type=Path, required=True)
    run.add_argument('--run', required=True)
    mode = run.add_mutually_exclusive_group(required=True)
    mode.add_argument('--check-only', action='store_true')
    mode.add_argument('--game-dir', type=Path)
    run.add_argument('--map', dest='map_name', default='dl_midtown')
    run.add_argument('--port', type=int, default=27067)
    run.add_argument('--solo-probe', action='store_true', help='Create one native bot for an automatic 20-second spawn/idle probe.')
    run.add_argument('--solo-drop', action='store_true', help='Raise the probe bot once by 256 raw units after five seconds; record its native fall.')
    args = parser.parse_args(argv)
    try:
        if args.command == 'prepare':
            report = prepare()
            code = 0
        elif args.command == 'doctor':
            report = doctor(args.sdk_bin)
            code = 0 if report.get('ready', report['ready_tools']) else 1
        elif args.command == 'stop':
            report = stop_capture(args.run)
            code = 0
        elif args.command == 'mark':
            report = mark_capture(args.run, args.trial, args.event)
            code = 0
        else:
            report = run_capture(args.sdk_bin, args.run, check_only=args.check_only,
                                 game_dir=args.game_dir, map_name=args.map_name, port=args.port,
                                 solo_probe=args.solo_probe, solo_drop=args.solo_drop)
            code = report['exit_code']
        print(json.dumps(report, indent=2, allow_nan=False))
        return code
    except (SessionError, OSError) as error:
        print(json.dumps({'status': 'error', 'error': str(error),
                          'native_compatibility': 'unverified'}, indent=2), file=sys.stderr)
        return 2


if __name__ == '__main__':
    sys.exit(main())
