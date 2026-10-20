"""Strict, offline whole-tick movement input protocol and schedule compiler.

This module never attaches to the game or writes movement state. Compiled inputs
are requests, not observations or evidence that a native command path works.
Ticks are relative to an eventual arm tick; normalized axes use positive forward
and positive right strafe. View angles are absolute degrees. Native axis scaling,
signs and button masks are not assigned here and require separate calibration.
"""

import argparse
import copy
import hashlib
import json
import math
from pathlib import Path
import re
import sys


MAX_TICKS = 4096
MAX_EVENTS = 1024
MAX_SWEEP_TRIALS = 128
MAX_SWEEP_TICKS = 65536
BUTTONS = ('crouch', 'jump', 'dash')
ABORT_REASONS = ('pawn_change', 'world_change', 'respawn', 'timeout',
                 'lost_samples', 'missing_state', 'human_interruption', 'missed_deadline')


class ProtocolError(ValueError):
    """A requested experiment is ambiguous, unsupported, or out of bounds."""


def _object(value, fields, where, optional=()):
    if not isinstance(value, dict):
        raise ProtocolError(f'{where}: expected an object')
    missing = set(fields) - set(value)
    unknown = set(value) - set(fields) - set(optional)
    if missing or unknown:
        raise ProtocolError(f'{where}: missing fields {sorted(map(str, missing))}; unknown fields {sorted(map(str, unknown))}')


def _int(value, low, high, where):
    if type(value) is not int or not low <= value <= high:
        raise ProtocolError(f'{where}: expected integer in [{low}, {high}]')


def _number(value, low, high, where):
    if type(value) not in (int, float) or not low <= value <= high or not math.isfinite(value):
        raise ProtocolError(f'{where}: expected finite number in [{low}, {high}]')


def _bool(value, where, required=None):
    if type(value) is not bool or (required is not None and value is not required):
        raise ProtocolError(f'{where}: expected {required if required is not None else "boolean"}')


def _view(value, where):
    _object(value, ('yaw', 'pitch'), where)
    _number(value['yaw'], -180, 180, where + '.yaw')
    _number(value['pitch'], -89, 89, where + '.pitch')


def neutral_release():
    """Logical release request for every controlled channel, including abort."""
    return {'axes': {'forward': 0.0, 'strafe': 0.0},
            'buttons': dict.fromkeys(BUTTONS, False)}


def _validate_structure(protocol):
    _object(protocol, ('schema_version', 'trial_id', 'target_slot', 'tick_rate_hz',
                      'max_ticks', 'settle_ticks', 'initial_conditions', 'initial_view',
                      'abort_policy', 'events', 'assertions'), 'protocol')
    _int(protocol['schema_version'], 1, 1, 'schema_version')
    trial_id = protocol['trial_id']
    if not isinstance(trial_id, str) or not re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9_.-]{0,95}', trial_id):
        raise ProtocolError('trial_id: expected a 1-96 character identifier')
    _int(protocol['target_slot'], 0, 63, 'target_slot')
    _int(protocol['tick_rate_hz'], 64, 64, 'tick_rate_hz')
    _int(protocol['max_ticks'], 2, MAX_TICKS, 'max_ticks')
    _int(protocol['settle_ticks'], 1, protocol['max_ticks'] - 1, 'settle_ticks')
    _view(protocol['initial_view'], 'initial_view')
    initial = protocol['initial_conditions']
    _object(initial, ('hero_id', 'grounded', 'rest_speed_max', 'min_stamina',
                      'stable_ticks', 'alive', 'configuration_id',
                      'require_configuration_confirmation', 'inactive_states'),
            'initial_conditions')
    _int(initial['hero_id'], 1, 2147483647, 'initial_conditions.hero_id')
    _bool(initial['grounded'], 'initial_conditions.grounded')
    _number(initial['rest_speed_max'], 0, 1, 'initial_conditions.rest_speed_max')
    _number(initial['min_stamina'], 0, 100, 'initial_conditions.min_stamina')
    _int(initial['stable_ticks'], 1, MAX_TICKS, 'initial_conditions.stable_ticks')
    _bool(initial['alive'], 'initial_conditions.alive', True)
    configuration = initial['configuration_id']
    if not isinstance(configuration, str) or not configuration.strip() or len(configuration) > 128:
        raise ProtocolError('initial_conditions.configuration_id: expected a nonempty label of at most 128 characters')
    _bool(initial['require_configuration_confirmation'],
          'initial_conditions.require_configuration_confirmation', True)
    inactive = initial['inactive_states']
    required_states = ['sliding', 'dashing', 'climbing', 'mantling']
    if not isinstance(inactive, list) or inactive != required_states:
        raise ProtocolError('initial_conditions.inactive_states: expected ' + repr(required_states))
    abort = protocol['abort_policy']
    _object(abort, (*ABORT_REASONS, 'release_all', 'max_prepare_ticks'), 'abort_policy')
    for name in (*ABORT_REASONS, 'release_all'):
        _bool(abort[name], 'abort_policy.' + name, True)
    _int(abort['max_prepare_ticks'], 1, MAX_TICKS, 'abort_policy.max_prepare_ticks')
    events = protocol['events']
    if not isinstance(events, list) or len(events) > MAX_EVENTS:
        raise ProtocolError(f'events: expected list of at most {MAX_EVENTS} events')
    previous_tick = -1
    same_tick_channels = set()
    for index, event in enumerate(events):
        where = f'events[{index}]'
        if not isinstance(event, dict) or event.get('kind') not in ('button', 'axes', 'view'):
            raise ProtocolError(where + ': kind must be button, axes or view')
        kind = event['kind']
        fields = {'button': ('button', 'action'), 'axes': ('forward', 'strafe'),
                  'view': ('yaw', 'pitch')}[kind]
        _object(event, ('tick', 'kind', *fields), where)
        _int(event['tick'], 0, protocol['max_ticks'] - protocol['settle_ticks'] - 1,
             where + '.tick')
        if event['tick'] < previous_tick:
            raise ProtocolError(where + ': events must be ordered by tick')
        if event['tick'] != previous_tick:
            same_tick_channels.clear()
        previous_tick = event['tick']
        if kind == 'button':
            if event['button'] not in BUTTONS or type(event['button']) is not str:
                raise ProtocolError(where + ': unsupported logical button')
            if event['action'] not in ('press', 'release'):
                raise ProtocolError(where + ': action must be press or release')
            channel = 'button.' + event['button']
        elif kind == 'axes':
            for axis in ('forward', 'strafe'):
                _number(event[axis], -1, 1, where + '.' + axis)
            channel = kind
        else:
            _view({key: event[key] for key in ('yaw', 'pitch')}, where)
            channel = kind
        if channel in same_tick_channels:
            raise ProtocolError(where + ': conflicting writes to ' + channel + ' on the same tick')
        same_tick_channels.add(channel)
    assertions = protocol['assertions']
    if not isinstance(assertions, list) or len(assertions) > MAX_EVENTS:
        raise ProtocolError('assertions: expected a bounded list')
    assertion_channels = set()
    for index, assertion in enumerate(assertions):
        where = f'assertions[{index}]'
        if not isinstance(assertion, dict) or assertion.get('kind') not in ('grounded', 'speed', 'stamina'):
            raise ProtocolError(where + ': kind must be grounded, speed or stamina')
        kind = assertion['kind']
        _object(assertion, ('tick', 'kind', *(('equals',) if kind == 'grounded' else ('min', 'max'))), where)
        _int(assertion['tick'], 0, protocol['max_ticks'] - 1, where + '.tick')
        if kind == 'grounded':
            _bool(assertion['equals'], where + '.equals')
        else:
            ceiling = 100 if kind == 'stamina' else 100000
            _number(assertion['min'], 0, ceiling, where + '.min')
            _number(assertion['max'], assertion['min'], ceiling, where + '.max')
        channel = (assertion['tick'], kind)
        if channel in assertion_channels:
            raise ProtocolError(where + ': duplicate assertion for tick and kind')
        assertion_channels.add(channel)


def _schedule(protocol):
    state = neutral_release()
    state['view'] = copy.deepcopy(protocol['initial_view'])
    events = protocol['events']
    event_index = 0
    rows = []
    settle_start = protocol['max_ticks'] - protocol['settle_ticks']
    for tick in range(protocol['max_ticks']):
        indices = []
        while event_index < len(events) and events[event_index]['tick'] == tick:
            event = events[event_index]
            kind = event['kind']
            if kind == 'button':
                held = event['action'] == 'press'
                if state['buttons'][event['button']] == held:
                    raise ProtocolError(f'events[{event_index}]: duplicate press or release without a held input')
                state['buttons'][event['button']] = held
            elif kind == 'axes':
                state['axes'] = {key: float(event[key]) for key in ('forward', 'strafe')}
            else:
                state['view'] = {key: float(event[key]) for key in ('yaw', 'pitch')}
            indices.append(event_index)
            event_index += 1
        if tick >= settle_start and (any(state['buttons'].values()) or any(state['axes'].values())):
            raise ProtocolError('events: unresolved held inputs at end; explicitly release buttons and neutralize axes before settling')
        rows.append({'tick': tick, 'phase': 'settle' if tick >= settle_start else 'execute',
                     **copy.deepcopy(state), 'event_indices': indices})
    return rows


def validate_protocol(protocol):
    """Return an independent validated protocol; reject implicit releases."""
    _validate_structure(protocol)
    _schedule(protocol)
    return copy.deepcopy(protocol)


def _canonical(value):
    return json.dumps(value, sort_keys=True, separators=(',', ':'), allow_nan=False)


def compile_protocol(protocol):
    """Compile requested states for ticks 0..max_ticks-1; no observations exist."""
    validated = validate_protocol(protocol)
    metadata = {key: copy.deepcopy(validated[key]) for key in (
        'trial_id', 'target_slot', 'tick_rate_hz', 'max_ticks', 'settle_ticks',
        'initial_conditions', 'initial_view', 'abort_policy', 'assertions')}
    return {'schema_version': 1, 'kind': 'requested_input_schedule',
            'execution_status': 'offline_only_unverified',
            'protocol_sha256': hashlib.sha256(_canonical(validated).encode('utf-8')).hexdigest(),
            **metadata, 'ticks': _schedule(validated), 'neutral_release': neutral_release()}


def compile_sweep(protocol, event_indices, offset_ticks):
    """Shift selected events together; sort by resulting tick, then validate.

    Offsets are whole ticks and may be negative. Each generated schedule must
    retain explicit releases and pass all ordinary protocol checks. The caller
    chooses pulse press/release indices together when preserving pulse length.
    """
    base = validate_protocol(protocol)
    for name, values, maximum in (('event_indices', event_indices, MAX_EVENTS),
                                  ('offset_ticks', offset_ticks, MAX_SWEEP_TRIALS)):
        if not isinstance(values, list) or not 1 <= len(values) <= maximum:
            raise ProtocolError(f'{name}: expected a nonempty bounded list')
        low, high = (0, len(base['events']) - 1) if name == 'event_indices' else (-MAX_TICKS, MAX_TICKS)
        for value in values:
            _int(value, low, high, name)
        if len(set(values)) != len(values):
            raise ProtocolError(name + ': duplicate values')
    if len(offset_ticks) * base['max_ticks'] > MAX_SWEEP_TICKS:
        raise ProtocolError('sweep exceeds total tick limit')
    trials = []
    for offset in offset_ticks:
        variant = copy.deepcopy(base)
        suffix = f'.shift{offset:+d}'
        variant['trial_id'] = base['trial_id'][:96 - len(suffix)] + suffix.replace('+', 'p')
        for index in event_indices:
            variant['events'][index]['tick'] += offset
        variant['events'].sort(key=lambda event: event['tick'])
        try:
            trial = compile_protocol(variant)
        except ProtocolError as error:
            raise ProtocolError(f'sweep offset {offset}: {error}') from error
        trial['sweep'] = {'base_trial_id': base['trial_id'], 'event_indices': list(event_indices),
                          'offset_ticks': offset}
        trials.append(trial)
    return {'schema_version': 1, 'kind': 'requested_input_sweep',
            'execution_status': 'offline_only_unverified', 'trials': trials}


def _unique_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ProtocolError('duplicate JSON field: ' + key)
        result[key] = value
    return result


def load_protocol(path):
    try:
        value = json.loads(Path(path).read_text(encoding='utf-8-sig'), object_pairs_hook=_unique_object,
                           parse_constant=lambda token: (_ for _ in ()).throw(
                               ProtocolError('nonfinite JSON number: ' + token)))
    except (OSError, UnicodeError, json.JSONDecodeError) as error:
        raise ProtocolError(f'Cannot read protocol: {error}') from error
    return validate_protocol(value)


def write_new(output, value):
    """Serialize before opening exclusively, so an existing file is untouched."""
    payload = json.dumps(value, indent=2, sort_keys=True, allow_nan=False) + '\n'
    with Path(output).open('x', encoding='utf-8', newline='\n') as destination:
        destination.write(payload)


def compile_file(source, output):
    result = compile_protocol(load_protocol(source))
    write_new(output, result)
    return result


def _integers(value):
    try:
        return [int(part) for part in value.split(',')]
    except ValueError as error:
        raise argparse.ArgumentTypeError('expected comma-separated integers') from error


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest='command', required=True)
    for name in ('validate', 'compile', 'sweep'):
        command = commands.add_parser(name)
        command.add_argument('protocol', type=Path)
        if name != 'validate':
            command.add_argument('--output', required=True, type=Path)
        if name == 'sweep':
            command.add_argument('--event-indices', required=True, type=_integers)
            command.add_argument('--offset-ticks', required=True, type=_integers)
    arguments = parser.parse_args(argv)
    try:
        protocol = load_protocol(arguments.protocol)
        if arguments.command == 'validate':
            result = {'valid': True, 'trial_id': protocol['trial_id'], 'execution_status': 'offline_only_unverified'}
        else:
            result = (compile_sweep(protocol, arguments.event_indices, arguments.offset_ticks)
                      if arguments.command == 'sweep' else compile_protocol(protocol))
            write_new(arguments.output, result)
            result = {'written': str(arguments.output.resolve()), 'execution_status': 'offline_only_unverified'}
    except (ProtocolError, OSError) as error:
        print(str(error), file=sys.stderr)
        return 2
    print(json.dumps(result, sort_keys=True))
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
