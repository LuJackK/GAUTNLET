"""Compare a scripted run's movement steps with a human reference, step for step.

Both traces are aligned on the movement step where an action's button bits
first appear (its rising edge), then compared over a window around it:
horizontal speed, vertical speed, grounded flag and whether a ground-dash
vector is active. Steps, not wall time, are the unit; the server records every
native movement call. Positions are not compared because the view yaw differs
between runs. A passing report shows the traces agree on those signals; it does
not by itself prove the scripted input path matches the human one elsewhere.
"""

import argparse
import json
import math
from pathlib import Path
import sys

from runner_schedule import DASH, JUMP, CROUCH

ACTIONS = {'dash': DASH, 'jump': JUMP, 'crouch': CROUCH}
NO_GROUND = 4294967295


class ParityError(ValueError):
    pass


def load_steps(trace):
    steps = []
    for line in Path(trace).read_text(encoding='utf-8').splitlines():
        record = json.loads(line)
        if record.get('type') != 'step':
            continue
        velocity = record['after']['velocity']
        steps.append({'tick': record['clock_tick'], 'pawn': record['pawn_handle'],
                      'held': int(record['buttons'][0]),
                      'hs': math.hypot(velocity[0], velocity[1]), 'vz': velocity[2],
                      'ground': record['after']['ground_entity'] != NO_GROUND,
                      'dash': record['before']['dash_vector'] is not None})
    return steps


def rising_edges(steps, mask, pawn=None):
    edges, previous = [], {}
    for index, step in enumerate(steps):
        if pawn is not None and step['pawn'] != pawn:
            continue
        down = step['held'] & mask == mask
        if down and not previous.get(step['pawn'], False):
            edges.append(index)
        previous[step['pawn']] = down
    return edges


def runner_trial_start_tick(runner_log, trial=0):
    for line in Path(runner_log).read_text(encoding='utf-8').splitlines():
        record = json.loads(line)
        if record['type'] == 'trial_start' and record['index'] == trial:
            return record['clock_tick']
    raise ParityError('runner log has no trial_start for trial %d' % trial)


def window(steps, index, pre, post):
    pawn = steps[index]['pawn']
    same = [s for s in steps if s['pawn'] == pawn]
    at = next(i for i, s in enumerate(same) if s is steps[index])
    if at < pre or at + post >= len(same):
        raise ParityError('window leaves the trace')
    return same[at - pre:at + post + 1]


def compare(reference, candidate, tolerance=0.5):
    fields = ('hs', 'vz', 'ground', 'dash')
    worst = dict.fromkeys(('hs', 'vz'), 0.0)
    mismatches = []
    for offset, (a, b) in enumerate(zip(reference, candidate)):
        for field in fields:
            if field in worst:
                difference = abs(a[field] - b[field])
                worst[field] = max(worst[field], difference)
                bad = difference > tolerance
            else:
                bad = a[field] != b[field]
            if bad:
                mismatches.append({'step': offset, field: [a[field], b[field]]})
    return {'steps': min(len(reference), len(candidate)), 'max_abs_diff': worst,
            'mismatches': mismatches[:20], 'mismatch_count': len(mismatches),
            'tolerance': tolerance, 'pass': not mismatches}


def parity(human_trace, human_tick, scripted_trace, runner_log, action, *,
           trial=0, occurrence=0, pre=8, post=60, tolerance=0.5):
    mask = ACTIONS[action]
    human = load_steps(human_trace)
    reference_index = next((i for i, s in enumerate(human)
                            if s['tick'] == human_tick and s['held'] & mask == mask), None)
    if reference_index is None:
        raise ParityError('no human step at tick %s with %s down' % (human_tick, action))
    scripted = load_steps(scripted_trace)
    start = runner_trial_start_tick(runner_log, trial)
    later = [i for i in rising_edges(scripted, mask) if scripted[i]['tick'] >= start]
    if len(later) <= occurrence:
        raise ParityError('scripted trace has no %s press number %d after trial start' % (action, occurrence + 1))
    result = compare(window(human, reference_index, pre, post),
                     window(scripted, later[occurrence], pre, post), tolerance)
    result.update(action=action, human_tick=human_tick, scripted_tick=scripted[later[occurrence]]['tick'],
                  window={'pre': pre, 'post': post})
    return result


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--human-trace', required=True, type=Path)
    parser.add_argument('--human-tick', required=True, type=int, help='clock tick of the human press')
    parser.add_argument('--scripted-trace', required=True, type=Path)
    parser.add_argument('--runner-log', required=True, type=Path)
    parser.add_argument('--action', required=True, choices=sorted(ACTIONS))
    parser.add_argument('--trial', type=int, default=0)
    parser.add_argument('--occurrence', type=int, default=0, help='which scripted press, from 0')
    parser.add_argument('--pre', type=int, default=8)
    parser.add_argument('--post', type=int, default=60)
    parser.add_argument('--tolerance', type=float, default=0.5)
    arguments = parser.parse_args(argv)
    try:
        result = parity(arguments.human_trace, arguments.human_tick, arguments.scripted_trace,
                        arguments.runner_log, arguments.action, trial=arguments.trial,
                        occurrence=arguments.occurrence, pre=arguments.pre, post=arguments.post,
                        tolerance=arguments.tolerance)
    except (ParityError, OSError, ValueError, KeyError) as error:
        print(str(error), file=sys.stderr)
        return 2
    print(json.dumps(result, sort_keys=True))
    return 0 if result['pass'] else 1


if __name__ == '__main__':
    raise SystemExit(main())
