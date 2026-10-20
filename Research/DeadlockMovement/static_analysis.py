"""Build-pinned inputs and headless runs for static analysis of Deadlock binaries.

Everything is written under local/binaries/<steam build>/ and local/ghidra/.
Nothing here edits the installed game. Function names come from Modlock's
signature file as resolved by modlock-sigcheck; struct layouts come from a
schema dump of the same build. Both are evidence locators, not verified
semantics: confirm every reading against captured behaviour.
"""
import argparse
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import re
import shutil
import struct
import subprocess
import sys

from bootstrap import ROOT, ghidra_paths, sha256

LOCAL = ROOT / 'local'
DEFAULT_SDK = LOCAL / 'capture-sdk' / 'bin'
DEFAULT_SIGNATURES = LOCAL / 'modlock' / 'data' / 'game_signatures.txtpb'
MODULES = {'server': 'game/citadel/bin/win64/server.dll',
           'client': 'game/citadel/bin/win64/client.dll'}
GHIDRA_SCRIPTS = ROOT / 'ghidra_scripts'

# Movement, traversal-ability and stamina classes. Bases are flattened in.
DEFAULT_CLASSES = (
    r'C(Citadel)?Player_MovementServices(_Humanoid)?',
    r'CCitadel_Ability_(Slide|Dash|Jump|Mantle|PowerJump)',
    r'CAbility(Slide|Dash|Jump|Mantle|PowerJump)VData',
    r'CCitadelBaseAbility', r'CitadelAbilityVData',
    r'C(CitadelPlayerPawn(Base)?|BasePlayerPawn)',
    r'CitadelHeroData_t',
    r'CCitadel_ArmorUpgrade_DoubleJump', r'CCitadel_Stamina_Regen_Jump_Reduction',
)
# Classes whose field offsets are distinctive enough to search for in code.
DEFAULT_PROBES = r'CAbility(Slide|Dash|Jump|Mantle|PowerJump)VData'

SCALARS = {'float32': ('float', 4), 'float64': ('double', 8), 'GameTime_t': ('float', 4),
           'bool': ('bool', 1), 'int8': ('int8', 1), 'uint8': ('uint8', 1),
           'int16': ('int16', 2), 'uint16': ('uint16', 2), 'int32': ('int32', 4),
           'uint32': ('uint32', 4), 'int64': ('int64', 8), 'uint64': ('uint64', 8),
           'GameTick_t': ('int32', 4), 'CUtlStringToken': ('uint32', 4), 'HeroID_t': ('int32', 4)}
VECTORS = {'Vector': 3, 'VectorWS': 3, 'QAngle': 3, 'Vector2D': 2, 'Vector4D': 4,
           'CNetworkVelocityVector': 3}

# CMoveData has no schema. Offsets from Modlock's movement_hook.cc, observed on
# its September 18 2026 build. Re-verify against captures on each new build.
CMOVEDATA = {'name': 'CMoveData', 'size': 0xe4, 'provenance': 'modlock movement_hook.cc (2026-09-18 build), unverified here',
             'fields': [{'name': 'm_vecViewAngles', 'offset': 0x08, 'kind': 'float', 'count': 3, 'size': 12},
                        {'name': 'm_vecMoveInput', 'offset': 0x20, 'kind': 'float', 'count': 3, 'size': 12},
                        {'name': 'm_vecVelocity', 'offset': 0x38, 'kind': 'float', 'count': 3, 'size': 12},
                        {'name': 'm_vecAbsOrigin', 'offset': 0xc8, 'kind': 'float', 'count': 3, 'size': 12},
                        {'name': 'm_nTicks', 'offset': 0xd4, 'kind': 'int32', 'count': 2, 'size': 8},
                        {'name': 'm_flFractions', 'offset': 0xdc, 'kind': 'float', 'count': 2, 'size': 8}]}
FUNCTION_PARAMS = {'movement.process': [['services', 'CCitadelPlayer_MovementServices*'],
                                        ['mv', 'CMoveData*']]}


class AnalysisError(Exception):
    pass


def steam_build_id(game_dir):
    manifest = game_dir.parents[1] / 'appmanifest_1422450.acf' if len(game_dir.parents) > 1 else None
    if manifest is None or not manifest.is_file():
        raise AnalysisError(f'No Steam app manifest beside {game_dir}; cannot label the build')
    match = re.search(r'"buildid"\s*"([0-9]+)"', manifest.read_text(encoding='utf-8-sig'), re.IGNORECASE)
    if not match:
        raise AnalysisError(f'No buildid in {manifest}')
    return match.group(1)


def parse_signatures(text):
    """Return {id: {'shape', 'note'}} from a Modlock game_signatures.txtpb."""
    result = {}
    for match in re.finditer(r'((?:^#[^\n]*\n)*)signatures \{(.*?)\n\}', text, re.MULTILINE | re.DOTALL):
        body = match.group(2)
        ident = re.search(r'\bid: "([^"]+)"', body)
        if not ident:
            continue
        shape = re.search(r'\bshape: "([^"]*)"', body)
        note = ' '.join(line.lstrip('# ').strip() for line in match.group(1).splitlines()).strip()
        result[ident.group(1)] = {'shape': shape.group(1) if shape else None, 'note': note}
    return result


def parse_sigcheck(text):
    """Return (resolved, failed) from modlock-sigcheck output."""
    resolved, failed = [], []
    for line in text.splitlines():
        match = re.match(r'(\w+)\s+(\S+\.dll)\s+(\S+)\s*(?:rva=0x([0-9a-fA-F]+))?(.*)$', line.strip())
        if not match:
            continue
        status, module, ident, rva, rest = match.groups()
        if status == 'ok' and rva:
            resolved.append({'module': module.lower(), 'id': ident, 'rva': int(rva, 16)})
        elif status != 'ok':
            failed.append({'module': module.lower(), 'id': ident, 'status': status, 'detail': rest.strip()})
    return resolved, failed


def build_labels(sigcheck_text, signatures_text):
    resolved, failed = parse_sigcheck(sigcheck_text)
    if not resolved:
        raise AnalysisError('modlock-sigcheck resolved no signatures')
    shapes = parse_signatures(signatures_text)
    labels = []
    for entry in resolved:
        info = shapes.get(entry['id'], {})
        labels.append({**entry, 'name': 'ml_' + re.sub(r'[^A-Za-z0-9]', '_', entry['id']),
                       'shape': info.get('shape'), 'note': info.get('note') or None,
                       'params': FUNCTION_PARAMS.get(entry['id'])})
    return {'labels': labels, 'failed': failed}


def field_layout(schema_type):
    """Map a schema type string to (kind, count, size), or None when unknown."""
    array = re.fullmatch(r'(.+)\[(\d+)\]', schema_type)
    base, count = (array.group(1), int(array.group(2))) if array else (schema_type, 1)
    if base.endswith('*'):
        return 'pointer', count, 8 * count
    if base in SCALARS:
        kind, size = SCALARS[base]
        return kind, count, size * count
    if base in VECTORS:
        return 'float', VECTORS[base] * count, 4 * VECTORS[base] * count
    if re.fullmatch(r'C(Weak)?Handle< .+ >|CEntityHandle', base):
        return 'uint32', count, 4 * count
    return None


def flattened_fields(by_name, name, seen=()):
    """Fields of a class and its first base chain, base first."""
    if name in seen:
        raise AnalysisError(f'Schema inheritance cycle at {name}')
    entry = by_name[name]
    fields = []
    bases = entry.get('bases') or []
    if bases and bases[0]['name'] in by_name:
        fields.extend(flattened_fields(by_name, bases[0]['name'], (*seen, name)))
    fields.extend({**field, 'declared_in': name} for field in entry.get('fields') or [])
    return fields


def build_struct(by_name, name):
    entry = by_name[name]
    raw = sorted(flattened_fields(by_name, name), key=lambda field: field['offset'])
    size = entry['size']
    fields = []
    for index, field in enumerate(raw):
        following = raw[index + 1]['offset'] if index + 1 < len(raw) else size
        gap = following - field['offset']
        if gap <= 0:
            continue  # duplicate/overlapping offset (unions); keep the first
        layout = field_layout(field['type'])
        if layout is None or layout[2] > gap:
            layout = ('bytes', gap, gap)
        kind, count, width = layout
        fields.append({'name': field['name'], 'offset': field['offset'], 'kind': kind,
                       'count': count, 'size': width, 'schema_type': field['type'],
                       'declared_in': field['declared_in']})
    return {'name': name.replace('::', '__'), 'size': size, 'provenance': 'schema dump', 'fields': fields}


def build_types(schemas, class_patterns, probe_pattern):
    classes = [entry for entry in schemas['classes'] if entry.get('module') == 'server']
    by_name = {entry['name']: entry for entry in classes}
    chosen = sorted(name for name in by_name if any(re.fullmatch(p, name) for p in class_patterns))
    missing = [p for p in class_patterns if not any(re.fullmatch(p, name) for name in by_name)]
    if missing:
        raise AnalysisError(f'No server schema class matches: {", ".join(missing)}')
    structs = [build_struct(by_name, name) for name in chosen]
    probes = [s['name'] for s in structs if re.fullmatch(probe_pattern, s['name'])]
    return {'schema_build': schemas.get('build'), 'structs': [*structs, CMOVEDATA], 'probes': probes}


def snapshot_dir(build):
    return LOCAL / 'binaries' / build


def write_new(path, text):
    if path.exists():
        raise AnalysisError(f'Refusing to overwrite {path}')
    path.write_text(text, encoding='utf-8')


def snapshot(args):
    game = Path(args.game_dir).resolve()
    build = steam_build_id(game)
    destination = snapshot_dir(build)
    if destination.exists():
        raise AnalysisError(f'Snapshot exists; refusing overwrite: {destination}')
    sigcheck = Path(args.sdk_bin) / 'modlock-sigcheck.exe'
    signatures = Path(args.signatures)
    for required in (sigcheck, signatures):
        if not required.is_file():
            raise AnalysisError(f'Missing {required}')
    destination.mkdir(parents=True)
    binaries = {}
    for module, relative in MODULES.items():
        source = game / relative
        target = destination / source.name
        shutil.copy2(source, target)
        digest = sha256(target)
        if sha256(source) != digest:
            raise AnalysisError(f'{source} changed during copy; finish Steam updates and retry')
        binaries[module] = {'file': target.name, 'source': relative, 'bytes': target.stat().st_size,
                            'sha256': digest}
    print('Resolving Modlock signatures against the installed build...', flush=True)
    check = subprocess.run([str(sigcheck), str(game), '--signatures', str(signatures)],
                           capture_output=True, encoding='utf-8', errors='replace')
    log = check.stdout + check.stderr
    (destination / 'sigcheck.log').write_text(log, encoding='utf-8')
    labels = build_labels(log, signatures.read_text(encoding='utf-8'))
    (destination / 'labels.json').write_text(json.dumps(labels, indent=1), encoding='utf-8')
    manifest = {'format_version': 1, 'steam_build_id': build,
                'created_utc': datetime.now(timezone.utc).isoformat(), 'binaries': binaries,
                'sigcheck': {'exit_code': check.returncode, 'sha256': sha256(sigcheck),
                             'signatures_sha256': sha256(signatures),
                             'resolved': len(labels['labels']), 'failed': labels['failed']}}
    (destination / 'manifest.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
    print(f'Snapshot {destination}: {len(labels["labels"])} labels, {len(labels["failed"])} failed signatures')


def load_manifest(build):
    path = snapshot_dir(build) / 'manifest.json'
    if not path.is_file():
        raise AnalysisError(f'No snapshot for build {build}; run snapshot first')
    return json.loads(path.read_text(encoding='utf-8'))


def dump(args):
    game = Path(args.game_dir).resolve()
    build = steam_build_id(game)
    manifest = load_manifest(build)
    for module, entry in manifest['binaries'].items():
        if sha256(game / entry['source']) != entry['sha256']:
            raise AnalysisError(f'Installed {module} differs from the snapshot; take a new snapshot')
    destination = snapshot_dir(build) / 'dump'
    if destination.exists():
        raise AnalysisError(f'Dump exists; refusing overwrite: {destination}')
    host = Path(args.sdk_bin) / 'modlock-host.exe'
    command = [str(host), '--dump', str(destination), '--game-dir', str(game),
               '--engine-args', '+ip 127.0.0.1 +sv_hibernate_when_empty 0']
    print('Starting a local server to dump schemas; it quits on its own.', flush=True)
    result = subprocess.run(command, cwd=host.parent, capture_output=True, encoding='utf-8',
                            errors='replace', timeout=args.timeout)
    snapshot_dir(build).joinpath('dump.log').write_text(result.stdout + result.stderr, encoding='utf-8')
    if result.returncode or not (destination / 'schemas.json').is_file():
        raise AnalysisError(f'Dump failed ({result.returncode}); see {snapshot_dir(build) / "dump.log"}')
    for module, entry in manifest['binaries'].items():
        if sha256(game / entry['source']) != entry['sha256']:
            raise AnalysisError(f'{module} changed during the dump; discard {destination}')
    manifest['dump'] = {'created_utc': datetime.now(timezone.utc).isoformat(),
                        'files': {p.name: sha256(p) for p in sorted(destination.glob('*.json'))}}
    (snapshot_dir(build) / 'manifest.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
    print(result.stdout.strip().splitlines()[-1] if result.stdout.strip() else 'Dump complete')


def types(args):
    load_manifest(args.build)
    schemas_path = snapshot_dir(args.build) / 'dump' / 'schemas.json'
    if not schemas_path.is_file():
        raise AnalysisError(f'No dump for build {args.build}; run dump first')
    schemas = json.loads(schemas_path.read_text(encoding='utf-8'))
    patterns = list(DEFAULT_CLASSES) + list(args.extra_class or [])
    result = build_types(schemas, patterns, args.probes)
    result['schemas_sha256'] = sha256(schemas_path)
    output = snapshot_dir(args.build) / 'types.json'
    output.write_text(json.dumps(result, indent=1), encoding='utf-8')
    print(f'{output}: {len(result["structs"])} structs, probes: {", ".join(result["probes"])}')


def ghidra_env():
    lock = json.loads((ROOT / 'ghidra.lock.json').read_text(encoding='utf-8-sig'))
    ghidra, jdk = ghidra_paths(lock)
    if not ghidra.is_dir():
        raise AnalysisError('Ghidra is not installed; run python bootstrap.py --ghidra')
    env = dict(os.environ)
    # The 2 GB default is too small for the ~60 MB game modules.
    env.setdefault('GHIDRA_HEADLESS_MAXMEM', '8G')
    if jdk is not None:
        if not jdk.is_dir():
            raise AnalysisError('Pinned JDK is not installed; run python bootstrap.py --ghidra')
        env['JAVA_HOME'] = str(jdk)
        env['PATH'] = str(jdk / 'bin') + os.pathsep + env.get('PATH', '')
    return ghidra, env


def headless(build, module, mode, scripts):
    manifest = load_manifest(build)
    ghidra, env = ghidra_env()
    binary = snapshot_dir(build) / manifest['binaries'][module]['file']
    if sha256(binary) != manifest['binaries'][module]['sha256']:
        raise AnalysisError(f'{binary} no longer matches its manifest hash')
    project = LOCAL / 'ghidra' / build
    project.mkdir(parents=True, exist_ok=True)
    launcher = ghidra / 'support' / ('analyzeHeadless.bat' if os.name == 'nt' else 'analyzeHeadless')
    command = [str(launcher), str(project), f'deadlock-{build}']
    if mode == 'import':
        command += ['-import', str(binary)]
    else:
        command += ['-process', binary.name, '-noanalysis']
        if mode == 'read':
            command += ['-readOnly']
    command += ['-scriptPath', str(GHIDRA_SCRIPTS)]
    for script, script_args in scripts:
        command += ['-postScript', script, *map(str, script_args)]
    log = project / f'{module}-{mode}-{datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")}.log'
    command += ['-log', str(log)]
    print(f'Running Ghidra headless ({mode}); log: {log}', flush=True)
    result = subprocess.run(command, env=env)
    if result.returncode:
        raise AnalysisError(f'Ghidra exited with {result.returncode}; see {log}')


def annotation_scripts(build):
    base = snapshot_dir(build)
    if not (base / 'types.json').is_file():
        raise AnalysisError(f'No types.json for build {build}; run types first')
    report = base / 'probe-report.json'
    return [('ApplyResearchAnnotations.java', [base / 'labels.json', base / 'types.json', build]),
            ('ProbeFieldUses.java', [base / 'types.json', base / 'labels.json', report])]


def analyze(args):
    headless(args.build, args.module, 'import', annotation_scripts(args.build))


def annotate(args):
    headless(args.build, args.module, 'process', annotation_scripts(args.build))


def script(args):
    if not (GHIDRA_SCRIPTS / args.name).is_file():
        raise AnalysisError(f'No script {args.name} in {GHIDRA_SCRIPTS}')
    headless(args.build, args.module, 'read', [(args.name, args.args)])


class PeImage:
    """Minimal x64 PE reader: sections, RVA/file-offset mapping, little-endian reads."""

    def __init__(self, data):
        self.data = data
        pe = struct.unpack_from('<I', data, 0x3c)[0]
        count = struct.unpack_from('<H', data, pe + 6)[0]
        optional = struct.unpack_from('<H', data, pe + 20)[0]
        self.base = struct.unpack_from('<Q', data, pe + 24 + 24)[0]
        self.sections = []
        for index in range(count):
            header = pe + 24 + optional + 40 * index
            name = data[header:header + 8].rstrip(b'\0').decode('ascii', 'replace')
            virtual_size, rva, raw_size, raw = struct.unpack_from('<IIII', data, header + 8)
            self.sections.append((name, rva, virtual_size, raw_size, raw))

    def offset(self, rva):
        for _, start, _, raw_size, raw in self.sections:
            if start <= rva < start + raw_size:
                return raw + rva - start
        return None

    def rva(self, offset):
        for _, start, _, raw_size, raw in self.sections:
            if raw <= offset < raw + raw_size:
                return start + offset - raw
        return None

    def in_code(self, rva):
        return any(name == '.text' and start <= rva < start + size
                   for name, start, size, _, _ in self.sections)


def class_vtables(image, name):
    """[(subobject offset, vtable RVA, [slot RVAs])] from MSVC x64 RTTI for one class."""
    found = image.data.find(b'.?AV' + name.encode() + b'@@\0')
    if found < 0:
        raise AnalysisError(f'No RTTI type descriptor for {name}')
    descriptor = image.rva(found) - 16  # TypeDescriptor: vftable ptr, spare, name
    needle = struct.pack('<I', descriptor)
    tables = []
    position = image.data.find(needle)
    while position != -1:
        locator = position - 12  # CompleteObjectLocator.pTypeDescriptor is at +12
        locator_rva = image.rva(locator) if locator >= 0 else None
        if (locator_rva is not None and struct.unpack_from('<I', image.data, locator)[0] == 1
                and struct.unpack_from('<I', image.data, locator + 20)[0] == locator_rva):
            subobject = struct.unpack_from('<I', image.data, locator + 4)[0]
            meta = image.data.find(struct.pack('<Q', image.base + locator_rva))
            if meta != -1:
                table = image.rva(meta) + 8
                slots, cursor = [], image.offset(table)
                while cursor is not None and cursor + 8 <= len(image.data):
                    target = struct.unpack_from('<Q', image.data, cursor)[0] - image.base
                    if not image.in_code(target):
                        break
                    slots.append(target)
                    cursor += 8
                tables.append((subobject, table, slots))
        position = image.data.find(needle, position + 1)
    return sorted(tables)


def vtables(args):
    manifest = load_manifest(args.build)
    binary = snapshot_dir(args.build) / manifest['binaries'][args.module]['file']
    image = PeImage(binary.read_bytes())
    report = {name: [{'subobject_offset': offset, 'rva': hex(table), 'slots': [hex(s) for s in slots]}
                     for offset, table, slots in class_vtables(image, name)] for name in args.classes}
    if args.base:
        base = {offset: slots for offset, _, slots in class_vtables(image, args.base)}
        for name in args.classes:
            for table in report[name]:
                reference = base.get(table['subobject_offset'], [])
                table['overrides_vs_' + args.base] = [
                    [index, slot] for index, slot in enumerate(table['slots'])
                    if index >= len(reference) or int(slot, 16) != reference[index]]
    output = snapshot_dir(args.build) / f'vtables-{args.module}.json'
    output.write_text(json.dumps(report, indent=1), encoding='utf-8')
    for name in args.classes:
        summary = ', '.join(f'+{t["subobject_offset"]}: {len(t["slots"])} slots'
                            + (f' ({len(t["overrides_vs_" + args.base])} overridden)' if args.base else '')
                            for t in report[name])
        print(f'{name}: {summary}')
    print(f'Wrote {output}')


def gui(args):
    ghidra, env = ghidra_env()
    launcher = ghidra / ('ghidraRun.bat' if os.name == 'nt' else 'ghidraRun')
    subprocess.Popen([str(launcher)], env=env)
    print(f'Ghidra started. Open project local/ghidra/<build>/deadlock-<build>.gpr')


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    commands = parser.add_subparsers(dest='command', required=True)
    game = argparse.ArgumentParser(add_help=False)
    game.add_argument('--game-dir', required=True, help='Deadlock installation directory')
    game.add_argument('--sdk-bin', default=str(DEFAULT_SDK), help='Directory with modlock-host/sigcheck')
    built = argparse.ArgumentParser(add_help=False)
    built.add_argument('--build', required=True, help='Steam build ID of an existing snapshot')
    module = argparse.ArgumentParser(add_help=False)
    module.add_argument('--module', choices=sorted(MODULES), default='server')

    item = commands.add_parser('snapshot', parents=[game], help='Copy binaries, hash them, resolve labels')
    item.add_argument('--signatures', default=str(DEFAULT_SIGNATURES))
    item.set_defaults(func=snapshot)
    item = commands.add_parser('dump', parents=[game], help='Dump schemas/console from a local server')
    item.add_argument('--timeout', type=int, default=600)
    item.set_defaults(func=dump)
    item = commands.add_parser('types', parents=[built], help='Build Ghidra struct definitions')
    item.add_argument('--extra-class', action='append', help='Additional class-name regex')
    item.add_argument('--probes', default=DEFAULT_PROBES, help='Regex of structs to search code for')
    item.set_defaults(func=types)
    item = commands.add_parser('analyze', parents=[built, module], help='Import, auto-analyse and annotate')
    item.set_defaults(func=analyze)
    item = commands.add_parser('annotate', parents=[built, module], help='Reapply annotations without reanalysis')
    item.set_defaults(func=annotate)
    item = commands.add_parser('script', parents=[built, module],
                               help='Run one ghidra_scripts/ script read-only against the saved project')
    item.add_argument('name', help='Script file name, e.g. DecompileFunctions.java')
    item.add_argument('args', nargs=argparse.REMAINDER, help='Script arguments')
    item.set_defaults(func=script)
    item = commands.add_parser('vtables', parents=[built, module],
                               help='Read class vtables from RTTI, optionally diffed against a base class')
    item.add_argument('classes', nargs='+', help='Class names, e.g. CCitadel_Ability_Slide')
    item.add_argument('--base', help='Base class to diff against, e.g. CCitadelBaseAbility')
    item.set_defaults(func=vtables)
    item = commands.add_parser('gui', help='Launch Ghidra with the pinned JDK')
    item.set_defaults(func=gui)
    args = parser.parse_args()
    try:
        args.func(args)
    except (AnalysisError, OSError, subprocess.SubprocessError, ValueError) as error:
        print(f'error: {error}', file=sys.stderr)
        sys.exit(1)


if __name__ == '__main__':
    main()
