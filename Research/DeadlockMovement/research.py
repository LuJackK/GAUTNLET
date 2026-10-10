"""Offline Deadlock resource research and movement trace comparison."""
import argparse
import csv
from datetime import datetime, timezone
import json
import math
from pathlib import Path
import re
import subprocess
import sys

from bootstrap import ROOT, sha256

LOCAL = ROOT / 'local'


def cli():
    lock = json.loads((ROOT / 'tools.lock.json').read_text(encoding='utf-8-sig'))
    name = 'Source2Viewer-CLI.exe' if sys.platform == 'win32' else 'Source2Viewer-CLI'
    matches = list((ROOT / '.tools' / lock['version'] / 'cli').rglob(name))
    if len(matches) != 1:
        raise ValueError('Run python bootstrap.py first; expected one installed CLI')
    return matches[0]


def run_cli(arguments):
    LOCAL.mkdir(exist_ok=True)
    result = subprocess.run([str(cli()), *map(str, arguments)], cwd=LOCAL,
                            capture_output=True, encoding='utf-8', errors='replace')
    if result.returncode:
        raise ValueError(f'CLI failed ({result.returncode}):\n{result.stdout}\n{result.stderr}')
    return result


def archive_path(value):
    path = Path(value).expanduser().resolve()
    if not path.is_file() or not path.name.endswith('_dir.vpk'):
        raise ValueError('Supply an existing *_dir.vpk; keep its numbered chunks alongside it')
    return path


def list_resources(args):
    archive = archive_path(args.vpk)
    result = run_cli(['-i', archive, '--vpk_list', '--vpk_filepath', args.filter])
    print(result.stdout, end='')
    if result.stderr:
        print(result.stderr, file=sys.stderr)


def extract(args):
    archive = archive_path(args.vpk)
    if not re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9_-]{0,79}', args.run):
        raise ValueError('Run name must be 1-80 letters, numbers, underscores or hyphens')
    destination = LOCAL / 'runs' / args.run
    destination.mkdir(parents=True, exist_ok=False)
    export = destination / 'exports'
    export.mkdir()
    arguments = ['-i', archive, '--vpk_filepath', args.filter, '-o', export, '--vpk_decompile']
    # Hash all archive members, not only the directory index: changes to chunks matter.
    members = sorted(archive.parent.glob(archive.name.removesuffix('_dir.vpk') + '_*.vpk'))
    print(f'Hashing {len(members)} archive members; this can take a few minutes.', flush=True)
    sources = [{'name': p.name, 'bytes': p.stat().st_size, 'sha256': sha256(p)} for p in members]
    manifest = {'format_version': 1, 'build_label': args.build,
                'created_utc': datetime.now(timezone.utc).isoformat(),
                'status': 'started', 'filter': args.filter, 'sources': sources,
                'tool_version': run_cli(['--version']).stdout.strip(),
                'tool_sha256': sha256(cli()), 'exports': []}
    manifest_path = destination / 'manifest.json'
    manifest_path.write_text(json.dumps(manifest, indent=2), encoding='utf-8')
    # Keep logs on failure too. Never overwrite an earlier run.
    result = subprocess.run([str(cli()), *map(str, arguments)], cwd=destination,
                            capture_output=True, encoding='utf-8', errors='replace')
    (destination / 'stdout.log').write_text(result.stdout, encoding='utf-8')
    (destination / 'stderr.log').write_text(result.stderr, encoding='utf-8')
    files = sorted(p for p in export.rglob('*') if p.is_file())
    manifest['exports'] = [{'path': p.relative_to(export).as_posix(), 'sha256': sha256(p)} for p in files]
    manifest['status'] = 'complete' if result.returncode == 0 and files else 'failed'
    manifest['exit_code'] = result.returncode
    manifest_path.write_text(json.dumps(manifest, indent=2), encoding='utf-8')
    if manifest['status'] != 'complete':
        raise ValueError(f'Extraction failed or matched no resources; inspect {destination}')
    print(f'Exported {len(files)} files to {export}')


def search(args):
    directory = Path(args.directory).resolve()
    if not directory.is_dir():
        raise ValueError(f'Not a directory: {directory}')
    pattern = re.compile(args.pattern, re.IGNORECASE)
    hits = 0
    for path in sorted(directory.rglob('*')):
        if not path.is_file():
            continue
        try:
            content = path.read_text(encoding='utf-8-sig')
        except (UnicodeError, OSError):
            continue
        for number, line in enumerate(content.splitlines(), 1):
            if pattern.search(line):
                print(f'{path.relative_to(directory)}:{number}: {line.strip()}')
                hits += 1
    print(f'{hits} matching lines', file=sys.stderr)


def read_trace(path):
    with Path(path).open(newline='', encoding='utf-8-sig') as stream:
        reader = csv.DictReader(stream)
        if not {'time_s', 'speed_mps'} <= set(reader.fieldnames or []):
            raise ValueError('Trace requires time_s,speed_mps columns (seconds, metres/second)')
        rows = [(float(row['time_s']), float(row['speed_mps'])) for row in reader]
    if len(rows) < 2 or any(not math.isfinite(t) or not math.isfinite(v) or v < 0 for t, v in rows):
        raise ValueError('Trace needs at least two finite samples with nonnegative speed')
    if any(b[0] <= a[0] for a, b in zip(rows, rows[1:])):
        raise ValueError('Trace timestamps must strictly increase')
    return rows


def compare_traces(reference, candidate):
    if candidate[0][0] > reference[0][0] or candidate[-1][0] < reference[-1][0]:
        raise ValueError('Candidate must cover the full reference time range; align event timestamps first')
    j = 0
    errors = []
    for time, expected in reference:
        while j + 1 < len(candidate) - 1 and candidate[j + 1][0] < time:
            j += 1
        ta, va = candidate[j]
        tb, vb = candidate[j + 1]
        actual = va + (vb - va) * ((time - ta) / (tb - ta))
        errors.append(actual - expected)
    return {'samples': len(errors), 'rmse_mps': math.sqrt(sum(e * e for e in errors) / len(errors)),
            'max_abs_error_mps': max(abs(e) for e in errors),
            'mean_error_mps': sum(errors) / len(errors),
            'alignment': 'explicit input timestamps; candidate linearly interpolated; no extrapolation'}


def compare(args):
    report = compare_traces(read_trace(args.reference), read_trace(args.candidate))
    report['reference_sha256'] = sha256(Path(args.reference))
    report['candidate_sha256'] = sha256(Path(args.candidate))
    print(json.dumps(report, indent=2))


def doctor(args):
    print(f'Python: {sys.version.split()[0]}')
    print(f'CLI: {cli()}')
    print(run_cli(['--version']).stdout.strip())
    print('Ready for offline archive listing and extraction.')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest='command', required=True)
    commands.add_parser('doctor').set_defaults(func=doctor)
    listing = commands.add_parser('list', help='List matching archive paths without extracting')
    listing.add_argument('--vpk', required=True)
    listing.add_argument('--filter', default='scripts/')
    listing.set_defaults(func=list_resources)
    extraction = commands.add_parser('extract', help='Export filtered resources into an immutable local run')
    extraction.add_argument('--vpk', required=True)
    extraction.add_argument('--filter', required=True, help='Exact paths or prefixes found using list, comma-separated')
    extraction.add_argument('--run', required=True)
    extraction.add_argument('--build', required=True, help='Steam build ID or clearly marked unknown label')
    extraction.set_defaults(func=extract)
    searching = commands.add_parser('search', help='Search text exports; preserve file and line evidence')
    searching.add_argument('directory')
    searching.add_argument('--pattern', default='slide|friction|acceleration|sprint|airdrag')
    searching.set_defaults(func=search)
    comparison = commands.add_parser('compare', help='Compare measured speed traces with explicit SI units')
    comparison.add_argument('reference')
    comparison.add_argument('candidate')
    comparison.set_defaults(func=compare)
    args = parser.parse_args()
    try:
        args.func(args)
    except (ValueError, OSError, re.error) as error:
        parser.exit(1, f'Error: {error}\n')


if __name__ == '__main__':
    main()
