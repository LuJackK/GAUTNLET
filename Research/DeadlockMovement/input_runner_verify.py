"""Compare an offline requested schedule with independently recorded native inputs.

This checks input fidelity only, not action eligibility, trajectory parity or
native compatibility. Native button masks must be supplied from calibration.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path


def load_json(path):
    return json.loads(Path(path).read_text(encoding='utf-8'))


def verify_inputs(schedule, records, *, start_tick, world, pawn_handle,
                  button_masks, strafe_to_left, tolerance=1e-4):
    """Check every movement callback in each scheduled tick, including repeats.

    start_tick is the actual arm tick from the executor, not a chat-marker time.
    button_masks maps logical actions to explicit calibrated held-mask integers.
    strafe_to_left is +1 or -1, according to the adapter's calibrated convention.
    """
    if any(type(v) is not int or v < 0 for v in (start_tick, world, pawn_handle)):
        raise ValueError('start tick, world and pawn must be nonnegative integers')
    if type(strafe_to_left) is not int or strafe_to_left not in (-1, 1):
        raise ValueError('explicit strafe_to_left must be -1 or 1')
    if type(tolerance) not in (int, float) or not math.isfinite(tolerance) or not 0 <= tolerance <= 0.01:
        raise ValueError('tolerance must be finite and between 0 and 0.01')
    if set(button_masks) != {'jump', 'dash', 'crouch'}:
        raise ValueError('provide explicit jump, dash and crouch button masks')
    used = 0
    for mask in button_masks.values():
        if type(mask) is not int or not 0 < mask < 2**64 or mask & used:
            raise ValueError('button masks must be nonzero, disjoint uint64 values')
        used |= mask
    ticks = schedule.get('ticks')
    if schedule.get('kind') != 'requested_input_schedule' or not isinstance(ticks, list) or not ticks:
        raise ValueError('expected a compiled requested_input_schedule')
    if len(ticks) > 100000:
        raise ValueError('schedule is too large')
    for index, tick in enumerate(ticks):
        if type(tick.get('tick')) is not int or tick['tick'] != index:
            raise ValueError('compiled ticks must be contiguous from zero')
        if set(tick.get('buttons', {})) != set(button_masks) or any(type(v) is not bool for v in tick['buttons'].values()):
            raise ValueError('compiled buttons must be explicit booleans')
        values = [tick['axes']['forward'], tick['axes']['strafe'], tick['view']['pitch'], tick['view']['yaw']]
        if any(type(v) not in (int, float) or not math.isfinite(v) for v in values):
            raise ValueError('compiled axes and view must be finite')
    last = ticks[-1]
    if any(last['buttons'].values()) or any(last['axes'].values()):
        raise ValueError('compiled schedule must end in neutral input')

    errors, seen = [], set()
    issue_count = 0
    checked = 0
    previous_sequence = None
    previous_tick = None

    def issue(code, tick, detail):
        nonlocal issue_count
        issue_count += 1
        if len(errors) < 100:
            errors.append({'code': code, 'tick': tick, 'detail': detail})

    def near(actual, expected):
        return type(actual) in (int, float) and math.isfinite(actual) and abs(actual - expected) <= tolerance

    for row in records:
        if row.get('type') != 'step':
            continue
        tick = row.get('clock_tick')
        if type(tick) is not int or not start_tick <= tick < start_tick + len(ticks):
            continue
        sequence = row.get('sequence')
        if type(sequence) is not int:
            issue('sequence_missing', tick, 'movement record has no integer sequence')
        elif previous_sequence is not None and sequence != previous_sequence + 1:
            issue('sequence_gap', tick, 'global movement sequence is discontinuous')
        previous_sequence = sequence
        if row.get('world') != world:
            issue('world_change', tick, 'unexpected world in execution interval')
            continue
        if row.get('pawn_handle') != pawn_handle:
            continue  # Another pawn is not evidence for the selected target.
        if previous_tick is not None and tick < previous_tick:
            issue('clock_regression', tick, 'target movement clock went backwards')
        previous_tick = tick
        desired = ticks[tick - start_tick]
        seen.add(tick)
        checked += 1
        try:
            held_text = row['buttons'][0]
            if not isinstance(held_text, str) or not held_text.isdecimal():
                raise ValueError()
            held = int(held_text)
            if not 0 <= held < 2**64:
                raise ValueError()
            for name, mask in button_masks.items():
                expected = mask if desired['buttons'][name] else 0
                if held & mask != expected:
                    issue('button_mismatch', tick, name)
            axes = row['axes']
            expected_axes = [desired['axes']['forward'], desired['axes']['strafe'] * strafe_to_left, 0]
            if len(axes) != 3 or not all(near(v, e) for v, e in zip(axes, expected_axes)):
                issue('axes_mismatch', tick, 'forward/left/up differs from request')
            angles = row['angles']
            if len(angles) != 3 or not near(angles[0], desired['view']['pitch']):
                issue('view_mismatch', tick, 'pitch differs from request')
            yaw = angles[1]
            if type(yaw) not in (int, float) or not math.isfinite(yaw) or abs((yaw - desired['view']['yaw'] + 180) % 360 - 180) > tolerance:
                issue('view_mismatch', tick, 'yaw differs from request')
        except (KeyError, IndexError, TypeError, ValueError):
            issue('malformed_observation', tick, 'invalid recorded input fields')
    missing = sorted(set(range(start_tick, start_tick + len(ticks))) - seen)
    for tick in missing:
        issue('missing_tick', tick, 'no target movement callback observed')
    return {
        'report_version': 1, 'input_fidelity_passed': issue_count == 0,
        'native_compatibility': 'unverified', 'action_success': 'not_assessed',
        'initial_conditions': 'not_assessed', 'capture_integrity': 'requires_full_stream_validators',
        'start_tick': start_tick, 'world': world, 'pawn_handle': pawn_handle,
        'checked_callbacks': checked, 'expected_ticks': len(ticks),
        'issue_count': issue_count, 'issues': errors,
        'release_observed': (start_tick + len(ticks) - 1 in seen and not any(
            e['tick'] == start_tick + len(ticks) - 1 for e in errors)) if issue_count <= 100 else False,
        'note': 'Held masks/axes/view only. Changed/scroll semantics and ability-phase parity require separate calibration.',
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('schedule', type=Path)
    parser.add_argument('trace', type=Path)
    parser.add_argument('--binding', type=Path, required=True,
                        help='JSON with start_tick, world, pawn_handle, button_masks, strafe_to_left')
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    binding = load_json(args.binding)
    required = {'start_tick', 'world', 'pawn_handle', 'button_masks', 'strafe_to_left'}
    if set(binding) != required:
        parser.error('binding fields must be exactly: ' + ', '.join(sorted(required)))
    with args.trace.open(encoding='utf-8') as source:
        report = verify_inputs(load_json(args.schedule), (json.loads(line) for line in source), **binding)
    report['schedule_sha256'] = hashlib.sha256(args.schedule.read_bytes()).hexdigest()
    # Refuse replacement: evidence reports are immutable artifacts.
    with args.output.open('x', encoding='utf-8') as target:
        json.dump(report, target, indent=2)
        target.write('\n')
    print(json.dumps(report, indent=2))
    return 0 if report['input_fidelity_passed'] else 1


if __name__ == '__main__':
    raise SystemExit(main())
