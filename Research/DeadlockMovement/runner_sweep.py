"""Classify each trial of a scripted dash-jump timing sweep and report the window.

For every trial the dash press and the next jump press are located in the
movement trace by their button bits. The jump is classified from the vertical
speed after the jump step, using the human-run values as anchors: an ordinary
jump leaves with vz 287.5, a dash-jump with 412.5 (human-tests-20261020-01,
T07). Presses that leave the pawn on the ground are reported as no_air, which
is what the human early-press rejections looked like. Delay is counted in
movement steps of the pawn from the dash press to the jump press.
"""

import argparse
import json
from pathlib import Path
import sys

from runner_parity import ParityError, load_steps, rising_edges, DASH, JUMP

ORDINARY_VZ = 287.5
DASH_JUMP_VZ = 412.5


def classify(steps, jump_index):
    """steps are one pawn's movement steps; jump_index is the press step."""
    after = steps[jump_index]
    if after['ground']:
        return 'no_air'
    if after['vz'] >= (ORDINARY_VZ + DASH_JUMP_VZ) / 2:
        return 'dash_jump'
    return 'ordinary_jump'


def trial_ranges(runner_log):
    starts, ends = {}, {}
    for line in Path(runner_log).read_text(encoding='utf-8').splitlines():
        record = json.loads(line)
        if record['type'] == 'trial_start':
            starts[record['index']] = record['clock_tick']
        elif record['type'] in ('trial_end', 'runner_done'):
            ends[record['index']] = record['clock_tick']
    return [(i, starts[i], ends.get(i)) for i in sorted(starts)]


def analyze(trace, runner_log, trial_ids=None):
    steps = load_steps(trace)
    rows = []
    for index, start, end in trial_ranges(runner_log):
        scoped = [s for s in steps if s['tick'] >= start and (end is None or s['tick'] <= end)]
        dashes = rising_edges(scoped, DASH)
        jumps = rising_edges(scoped, JUMP)
        label = trial_ids[index] if trial_ids and index < len(trial_ids) else str(index)
        if not dashes or not jumps:
            rows.append({'trial': label, 'outcome': 'missing_press', 'dash': bool(dashes), 'jump': bool(jumps)})
            continue
        pawn = scoped[dashes[0]]['pawn']
        own = [s for s in scoped if s['pawn'] == pawn]
        dash_at = next(i for i, s in enumerate(own) if s is scoped[dashes[0]])
        later = [j for j in jumps if j > dashes[0]]
        if not later:
            rows.append({'trial': label, 'outcome': 'missing_press', 'dash': True, 'jump': False})
            continue
        jump_at = next(i for i, s in enumerate(own) if s is scoped[later[0]])
        rows.append({'trial': label, 'delay_steps': jump_at - dash_at,
                     'delay_ticks': own[jump_at]['tick'] - own[dash_at]['tick'],
                     'vz_after_press': own[jump_at]['vz'], 'hs_after_press': own[jump_at]['hs'],
                     'outcome': classify(own, jump_at)})
    delays = sorted(r['delay_steps'] for r in rows if r.get('outcome') == 'dash_jump')
    return {'trials': rows, 'dash_jump_delays': delays,
            'window': [delays[0], delays[-1]] if delays else None,
            'contiguous': bool(delays) and delays == list(range(delays[0], delays[-1] + 1))}


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--trace', required=True, type=Path)
    parser.add_argument('--runner-log', required=True, type=Path)
    parser.add_argument('--sweep', type=Path, help='compiled sweep JSON, only used to label trials')
    arguments = parser.parse_args(argv)
    try:
        ids = None
        if arguments.sweep:
            ids = [t['trial_id'] for t in json.loads(arguments.sweep.read_text(encoding='utf-8'))['trials']]
        result = analyze(arguments.trace, arguments.runner_log, ids)
    except (ParityError, OSError, ValueError, KeyError) as error:
        print(str(error), file=sys.stderr)
        return 2
    print(json.dumps(result, indent=1, sort_keys=True))
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
