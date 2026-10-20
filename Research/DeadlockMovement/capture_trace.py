"""Stream-check recorder JSONL without claiming native layout or timing compatibility.

Only callback engine-clock spans can describe duration. Raw command ticks,
fractions and diagnostic wall times do not establish simulation step duration.
Synthetic traces exercise this file format and are never real-game evidence.
"""

import argparse
import json
import math
from pathlib import Path
import re
import sys


UINT32_MAX = (1 << 32) - 1
UINT64_MAX = (1 << 64) - 1
INT32_MIN = -(1 << 31)
INT32_MAX = (1 << 31) - 1
MAX_DETAILS = 100


class CaptureError(ValueError):
    """A malformed JSON value or capture record."""


def _integer(value, name, minimum=None, maximum=None, nullable=False):
    if nullable and value is None:
        return
    if type(value) is not int:
        raise CaptureError(f'{name} must be an integer' + (' or null' if nullable else ''))
    if ((minimum is not None and value < minimum)
            or (maximum is not None and value > maximum)):
        raise CaptureError(f'{name} is outside its allowed integer range')


def _number(value, name, nullable=False):
    if nullable and value is None:
        return
    if type(value) not in (int, float):
        raise CaptureError(f'{name} must be a finite number' + (' or null' if nullable else ''))
    try:
        finite = math.isfinite(value)
    except OverflowError:
        finite = False
    if not finite:
        raise CaptureError(f'{name} must be finite')


def _vector(value, name, length, integer=False):
    if not isinstance(value, list) or len(value) != length:
        raise CaptureError(f'{name} must be an array of length {length}')
    for index, element in enumerate(value):
        if integer:
            _integer(element, f'{name}[{index}]', INT32_MIN, INT32_MAX)
        else:
            _number(element, f'{name}[{index}]')


def _required(record, names, context):
    if not isinstance(record, dict):
        raise CaptureError(f'{context} must be an object')
    missing = [name for name in names if name not in record]
    if missing:
        raise CaptureError(f'{context} missing required fields: {", ".join(missing)}')


def _state(record, name):
    _required(record, ('position', 'velocity', 'ground_entity', 'move_type'), name)
    _vector(record['position'], f'{name}.position', 3)
    _vector(record['velocity'], f'{name}.velocity', 3)
    _integer(record['ground_entity'], f'{name}.ground_entity', 0, UINT32_MAX, nullable=True)
    _integer(record['move_type'], f'{name}.move_type', 0, 255, nullable=True)


def _session(record):
    _required(record, ('schema_version', 'module', 'units', 'timing_status', 'synthetic'), 'session')
    _integer(record['schema_version'], 'schema_version', 1, 1)
    for key, expected in (('module', 'server'), ('units', 'source'), ('timing_status', 'unverified')):
        if record[key] != expected:
            raise CaptureError(f'{key} must be {expected!r}')
    if type(record['synthetic']) is not bool:
        raise CaptureError('synthetic must be a boolean')


def _step(record):
    _required(record, ('sequence', 'world', 'pawn_handle', 'command_raw', 'ticks', 'fractions',
                       'buttons', 'angles', 'axes', 'before', 'after', 'wall_ns', 'clock_tick',
                       'clock_time_s'), 'step')
    _integer(record['sequence'], 'sequence', 0)
    _integer(record['world'], 'world', 1)
    _integer(record['pawn_handle'], 'pawn_handle', 0, UINT32_MAX, nullable=True)
    _integer(record['command_raw'], 'command_raw', INT32_MIN, INT32_MAX)
    _vector(record['ticks'], 'ticks', 2, integer=True)
    _vector(record['fractions'], 'fractions', 2)
    _vector(record['angles'], 'angles', 3)
    _vector(record['axes'], 'axes', 3)
    buttons = record['buttons']
    if not isinstance(buttons, list) or len(buttons) != 3:
        raise CaptureError('buttons must be an array of three decimal uint64 strings')
    for button in buttons:
        if (not isinstance(button, str) or not re.fullmatch(r'[0-9]{1,20}', button)
                or int(button) > UINT64_MAX):
            raise CaptureError('buttons must contain decimal uint64 strings')
    _state(record['before'], 'before')
    _state(record['after'], 'after')
    _integer(record['wall_ns'], 'wall_ns', 0, UINT64_MAX)
    _integer(record['clock_tick'], 'clock_tick', 0, UINT64_MAX, nullable=True)
    _number(record['clock_time_s'], 'clock_time_s', nullable=True)


def _end(record):
    _required(record, ('attempted', 'written', 'dropped', 'worlds'), 'end')
    for name in ('attempted', 'written', 'dropped', 'worlds'):
        _integer(record[name], name, 0)


def _unique_object(pairs):
    value = {}
    for name, element in pairs:
        if name in value:
            raise CaptureError(f'duplicate JSON key: {name}')
        value[name] = element
    return value


def _reject_constant(value):
    raise CaptureError(f'non-finite JSON constant: {value}')


def _check_finite(value):
    if isinstance(value, float) and not math.isfinite(value):
        raise CaptureError('non-finite JSON number')
    if isinstance(value, dict):
        for element in value.values():
            _check_finite(element)
    elif isinstance(value, list):
        for element in value:
            _check_finite(element)


def validate_capture(path):
    """Return a JSON-safe report; retain summaries, never the raw sample stream.

    ``valid`` means the file is structurally valid, complete, has no sample loss,
    and has no callback-clock regressions. It does not assess native compatibility.
    Report details are capped; total issue counts are not.
    """
    report = {
        'report_version': 1, 'path': str(Path(path)), 'valid': False,
        'format_valid': False, 'complete': False, 'synthetic': None,
        'evidence_status': 'unknown', 'native_compatibility': 'unassessed',
        'session': None, 'footer': None, 'step_records': 0, 'valid_step_records': 0,
        'world_count': 0, 'pawn_count': 0, 'unknown_pawn_steps': 0,
        'worlds': [], 'pawns': [],
        'sequence': {'first': None, 'last': None, 'missing_samples': 0,
                     'gap_count': 0, 'gaps': []},
        'metrics': {'max_step_displacement_source_units': None,
                    'max_observed_speed_source_units_per_s': None},
        'timing': {'status': 'unverified', 'source': 'callback engine clock',
                   'exact_step_duration_supported': False,
                   'clock_tick_regressions': 0, 'clock_time_regressions': 0,
                   'duplicate_clock_times': 0,
                   'note': 'Clock spans are callback engine-clock observations; raw ticks, '
                           'fractions and wall_ns do not establish exact step cadence.'},
        'fraction_anomaly_steps': 0, 'error_count': 0, 'errors': [],
        'warnings': ['Native layout, units and timing compatibility remain unverified.'],
    }
    format_errors = 0
    worlds = {}
    pawns = {}
    previous_sequence = -1
    header_seen = False
    footer_seen = False
    line_number = 0

    def issue(code, message, line=None, structural=True):
        nonlocal format_errors
        report['error_count'] += 1
        if structural:
            format_errors += 1
        if len(report['errors']) < MAX_DETAILS:
            report['errors'].append({'code': code, 'line': line, 'message': message})

    def gap(first, last):
        if last < first:
            return
        sequence = report['sequence']
        sequence['gap_count'] += 1
        sequence['missing_samples'] += last - first + 1
        if len(sequence['gaps']) < MAX_DETAILS:
            sequence['gaps'].append({'first': first, 'last': last, 'count': last - first + 1})

    try:
        with Path(path).open('r', encoding='utf-8') as source:
            for line_number, line in enumerate(source, 1):
                try:
                    record = json.loads(line, object_pairs_hook=_unique_object,
                                        parse_constant=_reject_constant)
                    _check_finite(record)
                    _required(record, ('type',), 'record')
                    kind = record['type']
                    if line_number == 1 and kind != 'session':
                        issue('missing_header', 'First record must be a session header.', line_number)
                    if footer_seen:
                        issue('after_footer', 'No records are allowed after the end footer.', line_number)
                        continue
                    if kind == 'session':
                        if header_seen or line_number != 1:
                            issue('unexpected_header', 'Exactly one session header must be first.', line_number)
                            continue
                        header_seen = True
                        _session(record)
                        report['session'] = record
                        report['synthetic'] = record['synthetic']
                        report['evidence_status'] = ('synthetic_only_not_real_game_evidence'
                                                     if record['synthetic'] else
                                                     'declared_non_synthetic_compatibility_unassessed')
                        if record['synthetic']:
                            report['warnings'].insert(0, 'SYNTHETIC: format test only; never real-game evidence.')
                    elif kind == 'end':
                        footer_seen = True
                        _end(record)
                        report['footer'] = record
                    elif kind == 'step':
                        report['step_records'] += 1
                        _step(record)
                        report['valid_step_records'] += 1
                        sequence = record['sequence']
                        if sequence <= previous_sequence:
                            issue('sequence_order', 'Sequence must strictly increase; duplicates and reversals are invalid.', line_number)
                        else:
                            gap(previous_sequence + 1, sequence - 1)
                            previous_sequence = sequence
                        if report['sequence']['first'] is None:
                            report['sequence']['first'] = sequence
                        report['sequence']['last'] = sequence
                        world = worlds.setdefault(record['world'], {
                            'world': record['world'], 'steps': 0, 'pawns': set(),
                            'clock_time_samples': 0, 'clock_tick_samples': 0,
                            'first_clock_time_s': None, 'last_clock_time_s': None,
                            'last_clock_tick': None, 'clock_tick_regressions': 0,
                            'clock_time_regressions': 0, 'duplicate_clock_times': 0,
                        })
                        world['steps'] += 1
                        pawn = record['pawn_handle']
                        if pawn is None:
                            report['unknown_pawn_steps'] += 1
                        else:
                            world['pawns'].add(pawn)
                            pawns[pawn] = pawns.get(pawn, 0) + 1
                        for field, prefix in (('clock_tick', 'clock_tick'), ('clock_time_s', 'clock_time')):
                            value = record[field]
                            if value is None:
                                continue
                            previous = world['last_clock_tick' if prefix == 'clock_tick' else 'last_clock_time_s']
                            if previous is not None and value < previous:
                                key = prefix + '_regressions'
                                world[key] += 1
                                report['timing'][key] += 1
                                issue(key, f'{field} regressed within world {record["world"]}.', line_number, structural=False)
                            if prefix == 'clock_time':
                                if previous is not None and value == previous:
                                    world['duplicate_clock_times'] += 1
                                    report['timing']['duplicate_clock_times'] += 1
                                if world['first_clock_time_s'] is None:
                                    world['first_clock_time_s'] = value
                                world['last_clock_time_s'] = value
                                world['clock_time_samples'] += 1
                            else:
                                world['last_clock_tick'] = value
                                world['clock_tick_samples'] += 1
                        if any(value < 0 or value > 1 for value in record['fractions']):
                            report['fraction_anomaly_steps'] += 1
                        displacement = math.dist(record['before']['position'], record['after']['position'])
                        speed = max(math.hypot(*record[state]['velocity']) for state in ('before', 'after'))
                        for name, value in (('max_step_displacement_source_units', displacement),
                                            ('max_observed_speed_source_units_per_s', speed)):
                            if not math.isfinite(value):
                                issue('metric_overflow', f'{name} exceeds finite reporting range.', line_number, structural=False)
                            elif report['metrics'][name] is None or value > report['metrics'][name]:
                                report['metrics'][name] = value
                    else:
                        issue('unknown_record', f'Unknown record type: {kind!r}.', line_number)
                except (ValueError, TypeError, RecursionError, OverflowError) as error:
                    issue('invalid_record', str(error), line_number)
    except (OSError, UnicodeError) as error:
        issue('read_error', str(error))

    if not header_seen:
        issue('missing_header', 'Capture is missing a session header.')
    if not footer_seen:
        issue('missing_footer', 'Capture is incomplete: missing end footer.')
    if not report['valid_step_records']:
        issue('empty_capture', 'At least one valid step is required.')
    footer = report['footer']
    if footer is not None:
        if footer['attempted'] != footer['written'] + footer['dropped']:
            issue('footer_counts', 'Footer attempted must equal written + dropped.')
        if footer['written'] != report['step_records']:
            issue('footer_written', 'Footer written does not match the number of step records.')
        if worlds and footer['worlds'] < max(worlds):
            issue('footer_worlds', 'Footer worlds is smaller than an observed world index.')
        if previous_sequence >= footer['attempted']:
            issue('footer_sequence', 'A sequence is outside the footer attempted range.')
        else:
            gap(previous_sequence + 1, footer['attempted'] - 1)
        if report['sequence']['missing_samples'] != footer['dropped']:
            issue('footer_drops', 'Sequence gaps do not match footer dropped.')
        if footer['dropped']:
            issue('dropped_samples', f'Capture lost {footer["dropped"]} attempted samples.', structural=False)
    elif report['sequence']['missing_samples']:
        issue('sequence_gaps', 'Capture contains sequence gaps; loss totals cannot be reconciled without a footer.', structural=False)

    if report['fraction_anomaly_steps']:
        report['warnings'].append('Raw fractions outside [0, 1] were observed; the unverified layout requires investigation.')
    for world in sorted(worlds.values(), key=lambda item: item['world']):
        world['pawns'] = sorted(world['pawns'])
        world['pawn_count'] = len(world['pawns'])
        duration = None
        if world['clock_time_samples'] >= 2 and not world['clock_time_regressions']:
            candidate = float(world['last_clock_time_s']) - float(world['first_clock_time_s'])
            if math.isfinite(candidate):
                duration = candidate
        world['callback_clock_span_s'] = duration
        world['duration_source'] = 'callback engine clock' if duration is not None else None
        report['worlds'].append(world)
    report['world_count'] = len(worlds)
    report['pawn_count'] = len(pawns)
    report['pawns'] = [{'pawn_handle': pawn, 'steps': count} for pawn, count in sorted(pawns.items())]
    report['format_valid'] = format_errors == 0
    report['complete'] = report['format_valid'] and footer is not None and footer['dropped'] == 0
    report['valid'] = report['error_count'] == 0
    report['errors_truncated'] = report['error_count'] > len(report['errors'])
    return report


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest='command', required=True)
    validate = commands.add_parser('validate', help='Validate capture JSONL and print a JSON report.')
    validate.add_argument('path', type=Path)
    validate.add_argument('--report', type=Path, help='Also create a report file; existing files are never overwritten.')
    args = parser.parse_args(argv)
    report = validate_capture(args.path)
    payload = json.dumps(report, indent=2, allow_nan=False) + '\n'
    print(payload, end='')
    if args.report is not None:
        try:
            with args.report.open('x', encoding='utf-8', newline='\n') as destination:
                destination.write(payload)
        except OSError as error:
            print(f'Cannot create report: {error}', file=sys.stderr)
            return 2
    return 0 if report['valid'] else 1


if __name__ == '__main__':
    sys.exit(main())
