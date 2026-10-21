"""Convert compiled input schedules into the native user-command runner format.

The native plugin (capture/native/observer_plugin.cc, --input-schedule) rewrites
each of the target player's user commands in order, one schedule tick per
command. This module turns the offline ``requested_input_schedule`` / sweep
documents from input_runner.py into the plugin's line format and back, and
checks a finished run's runner.jsonl against what was requested.

Button bits are the ones observed in human captures (see capture/INPUT_RUNNER.md):
the jump key is the bit-48 (mantle) plus bit-56 (zipline) composite, dash is
bit 44 (IN_INNATE_1). The movement axes are +1 forward and +1 *left*, so the
protocol's positive strafe (right) is a negative left axis.
"""

import argparse
import json
from pathlib import Path
import sys

from input_runner import compile_protocol, load_protocol, write_new, ProtocolError

FORWARD, BACK, CROUCH, LEFT, RIGHT = 8, 16, 4, 512, 1024
DASH = 1 << 44
JUMP = (1 << 48) | (1 << 56)
MASKS = {'crouch': CROUCH, 'dash': DASH, 'jump': JUMP}
HEADER = 'GRS1'
DEFAULT_REST_TICKS = 640


class ScheduleError(ValueError):
    """The requested schedule cannot be expressed for the native runner."""


def held_mask(row):
    held = 0
    for name, down in row['buttons'].items():
        if down:
            held |= MASKS[name]
    forward, strafe = row['axes']['forward'], row['axes']['strafe']
    if forward > 0:
        held |= FORWARD
    elif forward < 0:
        held |= BACK
    if strafe > 0:
        held |= RIGHT
    elif strafe < 0:
        held |= LEFT
    return held


def native_tick(row):
    """(held, forward_axis, left_axis, pitch, yaw) for one compiled tick row."""
    return (held_mask(row), float(row['axes']['forward']), -float(row['axes']['strafe']),
            float(row['view']['pitch']), float(row['view']['yaw']))


def compiled_trials(document):
    kind = document.get('kind')
    if kind == 'requested_input_schedule':
        return [document]
    if kind == 'requested_input_sweep':
        return list(document['trials'])
    raise ScheduleError('expected a compiled schedule or sweep, got ' + repr(kind))


def build_trials(document, *, start_state=None, rest_ticks=DEFAULT_REST_TICKS, slot=None):
    """Native trial dicts. rest_ticks of neutral input follow every trial so
    stamina and cooldowns recover before the next trial's start state."""
    if start_state is not None and (len(start_state) != 6 or any(v != v for v in start_state)):
        raise ScheduleError('start state is origin xyz then velocity xyz')
    if type(rest_ticks) is not int or not 0 <= rest_ticks <= 65536:
        raise ScheduleError('rest_ticks must be an integer in [0, 65536]')
    trials = []
    for compiled in compiled_trials(document):
        rows = [native_tick(row) for row in compiled['ticks']]
        neutral = (0, 0.0, 0.0, rows[-1][3], rows[-1][4])
        rows.extend([neutral] * rest_ticks)
        trials.append({'id': compiled['trial_id'],
                       'slot': compiled['target_slot'] if slot is None else slot,
                       'start': list(start_state) if start_state else None,
                       'ticks': rows,
                       'requested_ticks': len(compiled['ticks'])})
    slots = {trial['slot'] for trial in trials}
    if len(slots) != 1:
        raise ScheduleError('all trials of one run must target the same slot')
    return trials


def format_native(trials):
    lines = [HEADER]
    for trial in trials:
        head = f"trial {trial['id']} {trial['slot']} {len(trial['ticks'])}"
        if trial['start']:
            head += ' start ' + ' '.join(repr(float(v)) for v in trial['start'])
        lines.append(head)
        for held, forward, left, pitch, yaw in trial['ticks']:
            lines.append(f't {held} {forward!r} {left!r} {pitch!r} {yaw!r}')
        lines.append('end')
    return '\n'.join(lines) + '\n'


def parse_native(text):
    lines = text.splitlines()
    if not lines or lines[0] != HEADER:
        raise ScheduleError('missing ' + HEADER)
    trials, at = [], 1
    while at < len(lines):
        if not lines[at]:
            at += 1
            continue
        words = lines[at].split()
        if words[0] != 'trial' or len(words) not in (4, 11):
            raise ScheduleError('bad trial line: ' + lines[at])
        start = [float(v) for v in words[5:]] if len(words) == 11 else None
        count = int(words[3])
        ticks = []
        for line in lines[at + 1:at + 1 + count]:
            parts = line.split()
            if len(parts) != 6 or parts[0] != 't':
                raise ScheduleError('bad or missing tick line in trial ' + words[1] + ': ' + line)
            _, held, forward, left, pitch, yaw = parts
            ticks.append((int(held), float(forward), float(left), float(pitch), float(yaw)))
        if len(ticks) != count or lines[at + 1 + count:at + 2 + count] != ['end']:
            raise ScheduleError('truncated trial ' + words[1])
        trials.append({'id': words[1], 'slot': int(words[2]), 'start': start, 'ticks': ticks})
        at += 2 + count
    return trials


def check_runner_log(trials, log_lines):
    """Compare runner.jsonl with the schedule; returns (ok, problems).

    Every scheduled command must appear once, in order, with the requested
    input (applied) and, in apply mode, the same values read back from the
    engine's own message after the rewrite."""
    problems, seen = [], {}
    mode = None
    for line in log_lines:
        record = json.loads(line)
        if record['type'] == 'runner_session':
            mode = record['mode']
        elif record['type'] in ('runner_error', 'runner_unfinished'):
            problems.append(record['type'] + ': ' + str(record.get('error', '')))
        elif record['type'] == 'cmd':
            seen.setdefault(record['trial'], []).append(record)
    if mode is None:
        problems.append('no runner_session record')
    for index, trial in enumerate(trials):
        records = seen.get(index, [])
        if [r['tick'] for r in records] != list(range(len(trial['ticks']))):
            problems.append(f"trial {trial['id']}: expected {len(trial['ticks'])} consecutive commands, "
                            f"saw {len(records)}")
            continue
        previous = 0
        for record, (held, forward, left, pitch, yaw) in zip(records, trial['ticks']):
            applied = record['applied']
            if (int(applied['held']), int(applied['changed'])) != (held, held ^ previous):
                problems.append(f"trial {trial['id']} tick {record['tick']}: applied buttons differ")
                break
            got = [_f32(v) for v in applied['angles'][:2] + applied['axes'][:2]]
            if got != [_f32(v) for v in (pitch, yaw, forward, left)]:
                problems.append(f"trial {trial['id']} tick {record['tick']}: applied view or axes differ")
                break
            if mode == 'apply' and record['readback'] != applied:
                problems.append(f"trial {trial['id']} tick {record['tick']}: engine readback differs")
                break
            previous = held
    return not problems, problems


def _f32(value):
    import struct
    return struct.unpack('<f', struct.pack('<f', value))[0]


def _floats(value):
    try:
        parts = [float(v) for v in value.split(',')]
    except ValueError as error:
        raise argparse.ArgumentTypeError('expected comma-separated numbers') from error
    if len(parts) != 6:
        raise argparse.ArgumentTypeError('expected x,y,z,vx,vy,vz')
    return parts


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest='command', required=True)
    convert = commands.add_parser('convert', help='compiled schedule/sweep JSON, or a protocol JSON, to native format')
    convert.add_argument('source', type=Path)
    convert.add_argument('--output', required=True, type=Path)
    convert.add_argument('--start-state', type=_floats, help='x,y,z,vx,vy,vz applied before every trial')
    convert.add_argument('--rest-ticks', type=int, default=DEFAULT_REST_TICKS)
    convert.add_argument('--slot', type=int)
    check = commands.add_parser('check', help='verify runner.jsonl against a native schedule')
    check.add_argument('schedule', type=Path)
    check.add_argument('runner_log', type=Path)
    arguments = parser.parse_args(argv)
    try:
        if arguments.command == 'convert':
            document = json.loads(arguments.source.read_text(encoding='utf-8-sig'))
            if 'kind' not in document:
                document = compile_protocol(load_protocol(arguments.source))
            trials = build_trials(document, start_state=arguments.start_state,
                                  rest_ticks=arguments.rest_ticks, slot=arguments.slot)
            text = format_native(trials)
            with arguments.output.open('x', encoding='utf-8', newline='\n') as destination:
                destination.write(text)
            result = {'written': str(arguments.output.resolve()), 'trials': len(trials),
                      'ticks': sum(len(t['ticks']) for t in trials)}
        else:
            trials = parse_native(arguments.schedule.read_text(encoding='utf-8'))
            ok, problems = check_runner_log(trials, arguments.runner_log.read_text(encoding='utf-8').splitlines())
            result = {'ok': ok, 'problems': problems}
            print(json.dumps(result, sort_keys=True))
            return 0 if ok else 1
    except (ProtocolError, ScheduleError, OSError, ValueError) as error:
        print(str(error), file=sys.stderr)
        return 2
    print(json.dumps(result, sort_keys=True))
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
