"""Validate after-GameFrame observations and acknowledged trial markers.

Nullable observations stay unavailable; this tool never infers grounded state,
resource availability, ability readiness or exact native movement-step timing.
Synthetic files are format tests, never real-game evidence.
"""

import argparse
import json
import math
from pathlib import Path
import re
import sys

from capture_trace import (CaptureError, INT32_MAX, INT32_MIN, MAX_DETAILS,
                           UINT32_MAX, UINT64_MAX, _check_finite, _integer,
                           _number, _reject_constant, _required, _unique_object,
                           _vector, validate_capture)


MARKER_EVENTS = ('arm', 'start', 'end', 'abort')
IDENTIFIER = re.compile(r'[A-Za-z0-9_-]{1,64}')
FLAGS = ('grounded', 'sliding', 'dashing', 'climbing', 'mantling')


def _identifier(value, name):
    if not isinstance(value, str) or not IDENTIFIER.fullmatch(value):
        raise CaptureError(f'{name} must be 1-64 ASCII letters, digits, underscores or hyphens')


def _header(record):
    _required(record, ('schema_version', 'synthetic', 'phase'), 'frame_session')
    _integer(record['schema_version'], 'schema_version', 1, 1)
    if type(record['synthetic']) is not bool:
        raise CaptureError('synthetic must be a boolean')
    if record['phase'] != 'after_game_frame':
        raise CaptureError('phase must be after_game_frame')


def _row(record):
    _required(record, ('sequence', 'world', 'clock_tick', 'clock_time_s',
                       'movement_sequence'), 'frame or marker')
    _integer(record['sequence'], 'sequence', 0, UINT64_MAX)
    _integer(record['world'], 'world', 1)
    _integer(record['clock_tick'], 'clock_tick', 0, UINT64_MAX, nullable=True)
    _number(record['clock_time_s'], 'clock_time_s', nullable=True)
    _integer(record['movement_sequence'], 'movement_sequence', 0, UINT64_MAX, nullable=True)
    if record['type'] == 'marker':
        _required(record, ('request_id', 'trial_id', 'event'), 'marker')
        _identifier(record['request_id'], 'request_id')
        _identifier(record['trial_id'], 'trial_id')
        if record['event'] not in MARKER_EVENTS:
            raise CaptureError('event must be arm, start, end or abort')
        return
    _required(record, ('slot', 'pawn_handle', 'hero_id', 'session_generation', 'position',
                       'velocity', 'ground_entity', *FLAGS, 'stamina', 'abilities'), 'frame')
    _integer(record['slot'], 'slot', 0, 63)
    for field in ('pawn_handle', 'hero_id', 'session_generation'):
        _integer(record[field], field, 0, UINT32_MAX)
    _vector(record['position'], 'position', 3)
    if record['velocity'] is not None:
        _vector(record['velocity'], 'velocity', 3)
    _integer(record['ground_entity'], 'ground_entity', 0, UINT32_MAX, nullable=True)
    for flag in FLAGS:
        if record[flag] is not None and type(record[flag]) is not bool:
            raise CaptureError(f'{flag} must be a boolean or null')
    # Optional since the 20 October recorder: pawn and surface friction coefficients.
    for field in ('friction', 'surface_friction'):
        if record.get(field) is not None:
            _number(record[field], field)
    # Optional since the 23 October recorder: walls, slopes and landings.
    if record.get('fall_velocity') is not None:
        _number(record['fall_velocity'], 'fall_velocity')
    if record.get('ground_normal') is not None:
        _vector(record['ground_normal'], 'ground_normal', 3)
    for flag in ('wall_sliding', 'colliding', 'landed_on_ground'):
        if record.get(flag) is not None and type(record[flag]) is not bool:
            raise CaptureError(f'{flag} must be a boolean or null')
    stamina = record['stamina']
    if stamina is not None:
        _required(stamina, ('current', 'max'), 'stamina')
        _number(stamina['current'], 'stamina.current')
        _number(stamina['max'], 'stamina.max')
    abilities = record['abilities']
    if abilities is not None:
        if not isinstance(abilities, list):
            raise CaptureError('abilities must be an array or null')
        for ability in abilities:
            _required(ability, ('handle', 'slot', 'charges', 'cooldown_start', 'cooldown_end',
                                'recharge_start', 'recharge_end'), 'ability')
            _integer(ability['handle'], 'ability.handle', 0, UINT32_MAX)
            _integer(ability['slot'], 'ability.slot', 0, 65535)
            _integer(ability['charges'], 'ability.charges', INT32_MIN, INT32_MAX)
            for field in ('cooldown_start', 'cooldown_end', 'recharge_start', 'recharge_end'):
                _number(ability[field], 'ability.' + field)
            # Optional since the 20 October recorder: RTTI class and schema-named fields.
            if ability.get('class') is not None and not (
                    isinstance(ability['class'], str) and 0 < len(ability['class']) <= 128):
                raise CaptureError('ability.class must be a short string or null')
            fields = ability.get('fields')
            if fields is not None:
                if not isinstance(fields, dict):
                    raise CaptureError('ability.fields must be an object')
                for key, value in fields.items():
                    if not key.startswith('m_') or len(key) > 64:
                        raise CaptureError('ability.fields keys must be schema field names')
                    _number(value, 'ability.fields.' + key)


def validate_frames(path, movement_trace=None, *, movement_report=None):
    """Stream-validate a frame capture, optionally bounding movement references.

    Movement references name callbacks, including ones dropped by the movement
    queue, so their upper bound comes from the movement footer's attempted count.
    ``movement_report`` permits orchestration to reuse an already computed report.
    """
    report = {
        'report_version': 1, 'path': str(Path(path)), 'valid': False,
        'format_valid': False, 'complete': False, 'synthetic': None,
        'phase': 'after_game_frame', 'native_compatibility': 'unassessed',
        'evidence_status': 'unknown', 'session': None, 'footer': None,
        'data_records': 0, 'valid_data_records': 0, 'frame_count': 0, 'marker_count': 0,
        'world_count': 0, 'pawn_count': 0, 'markers': [], 'markers_truncated': False,
        'sequence': {'first': None, 'last': None, 'missing_samples': 0, 'gap_count': 0, 'gaps': []},
        'worlds': [], 'clock_tick_regressions': 0, 'clock_time_regressions': 0,
        'movement_correlation': {'checked': movement_trace is not None or movement_report is not None,
                                 'trace': str(movement_trace) if movement_trace is not None else None,
                                 'attempted': None, 'references': 0, 'out_of_bounds': 0},
        'error_count': 0, 'errors': [], 'errors_truncated': False,
        'warnings': ['After-GameFrame states do not establish exact movement-step timing; '
                     'native layout and timing compatibility remain unverified.',
                     'Null fields mean unavailable observations; no state or readiness is inferred.'],
    }
    format_errors = 0

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
        sequence['missing_samples'] += last - first + 1
        sequence['gap_count'] += 1
        if len(sequence['gaps']) < MAX_DETAILS:
            sequence['gaps'].append({'first': first, 'last': last, 'count': last - first + 1})

    correlation = report['movement_correlation']
    if correlation['checked']:
        if movement_report is None:
            movement_report = validate_capture(movement_trace)
        if not movement_report['valid']:
            issue('movement_trace_invalid', 'Referenced movement trace is invalid, incomplete or lost samples.', structural=False)
        if movement_report['footer'] is not None:
            correlation['attempted'] = movement_report['footer']['attempted']

    worlds = {}
    pawns = set()
    previous_sequence = -1
    previous_movement = None
    header_seen = footer_seen = False
    try:
        with Path(path).open('r', encoding='utf-8') as source:
            for line_number, line in enumerate(source, 1):
                try:
                    record = json.loads(line, object_pairs_hook=_unique_object, parse_constant=_reject_constant)
                    _check_finite(record)
                    _required(record, ('type',), 'record')
                    kind = record['type']
                    if line_number == 1 and kind != 'frame_session':
                        issue('missing_header', 'First record must be frame_session.', line_number)
                    if footer_seen:
                        issue('after_footer', 'No records are allowed after frame_end.', line_number)
                        continue
                    if kind == 'frame_session':
                        if header_seen or line_number != 1:
                            issue('unexpected_header', 'Exactly one frame_session must be first.', line_number)
                            continue
                        header_seen = True
                        _header(record)
                        report['session'] = record
                        report['synthetic'] = record['synthetic']
                        report['evidence_status'] = ('synthetic_only_not_real_game_evidence'
                                                     if record['synthetic'] else
                                                     'declared_non_synthetic_compatibility_unassessed')
                        if record['synthetic']:
                            report['warnings'].insert(0, 'SYNTHETIC: format test only; never real-game evidence.')
                        if (movement_report is not None and movement_report['synthetic'] is not None
                                and record['synthetic'] != movement_report['synthetic']):
                            issue('synthetic_mismatch', 'Frame and movement session synthetic flags differ.', line_number)
                    elif kind == 'frame_end':
                        footer_seen = True
                        _required(record, ('attempted', 'written', 'dropped'), 'frame_end')
                        for field in ('attempted', 'written', 'dropped'):
                            _integer(record[field], field, 0, UINT64_MAX)
                        report['footer'] = record
                    elif kind in ('frame', 'marker'):
                        report['data_records'] += 1
                        _row(record)
                        report['valid_data_records'] += 1
                        sequence = record['sequence']
                        if sequence <= previous_sequence:
                            issue('sequence_order', 'Frame and marker sequence must strictly increase.', line_number)
                        else:
                            gap(previous_sequence + 1, sequence - 1)
                            previous_sequence = sequence
                        if report['sequence']['first'] is None:
                            report['sequence']['first'] = sequence
                        report['sequence']['last'] = sequence
                        world = worlds.setdefault(record['world'], {
                            'world': record['world'], 'frames': 0, 'markers': 0,
                            'first_clock_time_s': None, 'last_clock_time_s': None,
                            'last_clock_tick': None, 'clock_time_samples': 0,
                            'clock_tick_regressions': 0, 'clock_time_regressions': 0,
                        })
                        for field, last, counter in (
                                ('clock_tick', 'last_clock_tick', 'clock_tick_regressions'),
                                ('clock_time_s', 'last_clock_time_s', 'clock_time_regressions')):
                            value = record[field]
                            if value is None:
                                continue
                            if world[last] is not None and value < world[last]:
                                world[counter] += 1
                                report[counter] += 1
                                issue(counter, f'{field} regressed within world {record["world"]}.', line_number, structural=False)
                            world[last] = value
                            if field == 'clock_time_s':
                                if world['first_clock_time_s'] is None:
                                    world['first_clock_time_s'] = value
                                world['clock_time_samples'] += 1
                        movement = record['movement_sequence']
                        if movement is not None:
                            correlation['references'] += 1
                            if previous_movement is not None and movement < previous_movement:
                                issue('movement_sequence_regression', 'Latest global movement callback reference regressed.', line_number, structural=False)
                            previous_movement = movement
                            if (correlation['checked'] and correlation['attempted'] is not None
                                    and movement >= correlation['attempted']):
                                correlation['out_of_bounds'] += 1
                                issue('movement_sequence_bounds', 'movement_sequence is outside the movement callback range.', line_number, structural=False)
                        if kind == 'frame':
                            report['frame_count'] += 1
                            world['frames'] += 1
                            pawns.add(record['pawn_handle'])
                        else:
                            report['marker_count'] += 1
                            world['markers'] += 1
                            if len(report['markers']) < MAX_DETAILS:
                                report['markers'].append({key: record[key] for key in (
                                    'sequence', 'world', 'clock_tick', 'clock_time_s',
                                    'movement_sequence', 'request_id', 'trial_id', 'event')})
                    else:
                        issue('unknown_record', f'Unknown record type: {kind!r}.', line_number)
                except (ValueError, TypeError, RecursionError, OverflowError) as error:
                    issue('invalid_record', str(error), line_number)
    except (OSError, UnicodeError) as error:
        issue('read_error', str(error))

    if not header_seen:
        issue('missing_header', 'Frame capture is missing frame_session.')
    if not footer_seen:
        issue('missing_footer', 'Frame capture is incomplete: missing frame_end.')
    if not report['frame_count']:
        issue('empty_frames', 'At least one valid frame row is required.')
    footer = report['footer']
    if footer is not None:
        if footer['attempted'] != footer['written'] + footer['dropped']:
            issue('footer_counts', 'Frame attempted must equal written + dropped.')
        if footer['written'] != report['data_records']:
            issue('footer_written', 'Frame written must count all frame and marker rows.')
        if previous_sequence >= footer['attempted']:
            issue('footer_sequence', 'A data sequence is outside the frame attempted range.')
        else:
            gap(previous_sequence + 1, footer['attempted'] - 1)
        if report['sequence']['missing_samples'] != footer['dropped']:
            issue('footer_drops', 'Frame sequence gaps do not match dropped.')
        if footer['dropped']:
            issue('dropped_samples', f'Frame capture lost {footer["dropped"]} attempted rows.', structural=False)
    elif report['sequence']['missing_samples']:
        issue('sequence_gaps', 'Frame capture has sequence gaps without a reconciling footer.', structural=False)
    for world in sorted(worlds.values(), key=lambda value: value['world']):
        span = None
        if world['clock_time_samples'] >= 2 and not world['clock_time_regressions']:
            candidate = float(world['last_clock_time_s']) - float(world['first_clock_time_s'])
            if math.isfinite(candidate):
                span = candidate
        world['callback_clock_span_s'] = span
        report['worlds'].append(world)
    report['world_count'] = len(worlds)
    report['pawn_count'] = len(pawns)
    report['format_valid'] = format_errors == 0
    report['complete'] = report['format_valid'] and footer is not None and footer['dropped'] == 0
    report['valid'] = report['error_count'] == 0
    report['errors_truncated'] = report['error_count'] > len(report['errors'])
    report['markers_truncated'] = report['marker_count'] > len(report['markers'])
    return report


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest='command', required=True)
    validate = commands.add_parser('validate', help='Validate frames.jsonl and acknowledged trial markers.')
    validate.add_argument('path', type=Path)
    validate.add_argument('--movement-trace', type=Path)
    validate.add_argument('--report', type=Path, help='Create a JSON report without overwriting an existing file.')
    args = parser.parse_args(argv)
    report = validate_frames(args.path, args.movement_trace)
    payload = json.dumps(report, indent=2, allow_nan=False) + '\n'
    print(payload, end='')
    if args.report is not None:
        try:
            with args.report.open('x', encoding='utf-8', newline='\n') as destination:
                destination.write(payload)
        except OSError as error:
            print(f'Cannot create frame report: {error}', file=sys.stderr)
            return 2
    return 0 if report['valid'] else 1


if __name__ == '__main__':
    sys.exit(main())
