"""Check decoded Deadlock movement rules against an existing capture (TEST_TRIAGE.md).

Runs the analysis-only checks T01 (release to stop), T02 (ground dash), T02b (air drag),
T03-pre (dash-slide timing from existing trials) and T04 (slide friction regimes) on a
finalized pilot capture and its reviewed trial windows. Rules come from BEHAVIOURS.md.
Every prediction uses the decoded constants; nothing is fitted to the capture.
Writes a JSON report and refuses to overwrite it.
"""
import argparse
import json
import math
from pathlib import Path
import statistics
import sys

TICK = 1 / 64
SV_FRICTION, SV_STOPSPEED = 4.0, 100.0
AIR_DRAG_MIN = 0.2
DASH_SLIDE_FRICTION, FLAT_SLIDE_FRICTION = 0.175, 0.8
DASH_SLIDE_FRICTION_TIME = 1.0
DASH_SLIDE_SPEED, DASH_SLIDE_FAIL_SPEED = 650.0, 450.0
SUSTAIN_SPEED = 170.0
CROUCH_BIT, DASH_BIT = 1 << 2, 1 << 44


def horizontal(vector):
    return math.hypot(vector[0], vector[1])


def friction_step(speed, coefficient, dt=TICK, stop=SV_STOPSPEED):
    """Decoded ground friction (B-GND-01): zero below 0.1, Source stop-speed law above."""
    if speed < 0.1:
        return 0.0
    return max(0.0, speed - max(speed, stop) * coefficient * dt)


def load_trace(path):
    steps = {}
    with open(path, encoding='utf-8') as stream:
        for line in stream:
            record = json.loads(line)
            if record.get('type') == 'step':
                steps[record['sequence']] = record
    return steps


def window(steps, first, last):
    return [steps[s] for s in range(first, last + 1) if s in steps]


def summary(values):
    values = [v for v in values if v is not None and math.isfinite(v)]
    if not values:
        return None
    return {'n': len(values), 'median': statistics.median(values),
            'min': min(values), 'max': max(values)}


def held(record):
    return int(record['buttons'][0])


def check_release(steps, forward):
    """T01: ticks from forward release to < 1 u/s, measured against the friction model."""
    trials = []
    for burst in forward['bursts']:
        rows = window(steps, burst['first'], burst['last'] + 200)
        moving = [i for i, r in enumerate(rows) if horizontal(r['after']['velocity']) > 1]
        release = next((i for i in range(moving[0], len(rows)) if rows[i]['axes'][0] == 0), None)
        if release is None:
            continue
        start = horizontal(rows[release]['before']['velocity'])
        measured = next(i for i in range(release, len(rows))
                        if horizontal(rows[i]['after']['velocity']) < 1) - release + 1
        speed, modelled = start, 0
        while speed >= 1:
            speed = friction_step(speed, SV_FRICTION)
            modelled += 1
        ratios = []
        for r in rows[release:release + measured]:
            before, after = horizontal(r['before']['velocity']), horizontal(r['after']['velocity'])
            predicted_drop = before - friction_step(before, SV_FRICTION)
            if before >= 1 and predicted_drop > 0:
                ratios.append((before - after) / predicted_drop)
        trials.append({'release_sequence': rows[release]['sequence'], 'speed_at_release': start,
                       'measured_ticks_to_below_1': measured, 'model_ticks_to_below_1': modelled,
                       'drop_ratio_measured_over_model': summary(ratios),
                       'first_speeds': [round(horizontal(r['after']['velocity']), 3)
                                        for r in rows[release:release + 6]]})
    return trials


def check_dash(steps, dashes):
    """T02: plateau speed and length, distance, and post-dash decay against friction."""
    trials = []
    for c in dashes['candidates']:
        rows = window(steps, c['first_sequence'], c['first_below_threshold_sequence'] + 30)
        speeds = [horizontal(r['after']['velocity']) for r in rows]
        peak = max(speeds)
        plateau = [i for i, v in enumerate(speeds) if abs(v - peak) < 1.0]
        first, last = plateau[0], plateau[-1]
        distance = math.dist(rows[first]['before']['position'][:2], rows[last]['after']['position'][:2])
        decay = []
        for r in rows[last + 1:]:
            before, after = horizontal(r['before']['velocity']), horizontal(r['after']['velocity'])
            if before < 1 or abs(r['before']['velocity'][2]) > 1:
                continue
            predicted = friction_step(before, SV_FRICTION)
            decay.append(after - predicted)
        trials.append({'first_sequence': c['first_sequence'], 'terrain': c.get('terrain_label'),
                       'peak_speed': peak, 'plateau_ticks': last - first + 1,
                       'plateau_seconds': (last - first + 1) * TICK,
                       'plateau_distance_units': distance, 'plateau_distance_m': distance * 0.0254,
                       'post_dash_residual_vs_friction': summary(decay)})
    return trials


def check_air_drag(steps, jumps):
    """T02b: per-tick horizontal speed ratio while airborne against (1 - 0.2/64)."""
    trials = []
    expected = 1 - AIR_DRAG_MIN * TICK
    for c in jumps['airborne_candidates']:
        ratios = []
        for r in window(steps, c['first_sequence'] + 1, c['landing_sequence'] - 1):
            before, after = horizontal(r['before']['velocity']), horizontal(r['after']['velocity'])
            if before > SV_STOPSPEED:
                ratios.append(after / before)
        trials.append({'first_sequence': c['first_sequence'], 'expected_ratio': expected,
                       'measured_ratio': summary(ratios),
                       'implied_drag': None if not ratios else (1 - statistics.median(ratios)) / TICK})
    return trials


def check_dash_slides(steps, slides):
    """T03-pre and T04: entry speed by crouch timing, then friction regime by time since entry."""
    trials = []
    for c in slides['candidates']:
        rows = window(steps, c['first_sequence'], c['window_last_sequence'])
        dash_start = rows[0]['clock_time_s']
        crouch = next((r for r in rows if held(r) & CROUCH_BIT), None)
        if crouch is None:
            continue
        crouch_index = rows.index(crouch)
        flat = (c['z_range_raw'][1] - c['z_range_raw'][0]) < 0.25
        record = {'trial': c['trial'], 'flat': flat,
                  'crouch_after_dash_s': crouch['clock_time_s'] - dash_start}
        # Entry: the slide sets speed outside the hooked step, so compare each step's
        # incoming velocity with the previous step's result, as well as within the step.
        entry = None
        for i in range(max(crouch_index, 1), min(crouch_index + 12, len(rows))):
            previous = horizontal(rows[i - 1]['after']['velocity'])
            before, after = horizontal(rows[i]['before']['velocity']), horizontal(rows[i]['after']['velocity'])
            if abs(before - previous) > 5 or abs(after - before) > 5:
                entry = i
                break
        if entry is not None:
            record['speed_entering_entry_step'] = horizontal(rows[entry]['before']['velocity'])
        if entry is None:
            record['entry'] = None
            trials.append(record)
            continue
        entry_speed = horizontal(rows[entry]['after']['velocity'])
        record.update({'entry_sequence': rows[entry]['sequence'], 'entry_speed_after_one_tick': entry_speed,
                       'nearest_target': 'dash_slide' if abs(entry_speed - DASH_SLIDE_SPEED) <
                       abs(entry_speed - DASH_SLIDE_FAIL_SPEED) else 'fail'})
        regimes = {'0_to_1s': [], '1_to_2s': []}
        record['speeds_after_entry'] = [round(horizontal(r['after']['velocity']), 2)
                                        for r in rows[entry:entry + 120]]
        stop_speed = None
        for r in rows[entry + 1:]:
            if not held(r) & CROUCH_BIT:
                break
            before, after = horizontal(r['before']['velocity']), horizontal(r['after']['velocity'])
            if before < 1:
                break
            t = r['clock_time_s'] - rows[entry]['clock_time_s']
            drop = before - after
            k = drop / (max(before, SV_STOPSPEED) * TICK * SV_FRICTION)
            if drop < -1:  # a speed gain means a state change, not friction
                if stop_speed is None and t > 0.2:
                    stop_speed = before
                break
            if t < DASH_SLIDE_FRICTION_TIME:
                regimes['0_to_1s'].append(k)
            elif t < 2:
                regimes['1_to_2s'].append(k)
        record['slide_coefficient'] = {name: summary(v) for name, v in regimes.items()}
        trials.append(record)
    return trials


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('capture', help='Finalized capture directory, e.g. local/captures/human-20261018-02')
    parser.add_argument('--output', required=True)
    args = parser.parse_args()
    capture, output = Path(args.capture), Path(args.output)
    if output.exists():
        sys.exit(f'Refusing to overwrite {output}')
    steps = load_trace(capture / 'trace.jsonl')
    read = lambda name: json.loads((capture / name).read_text(encoding='utf-8'))
    report = {
        'capture': str(capture),
        'rules': 'BEHAVIOURS.md, build 26059233 decoding; capture build may differ',
        'T01_release_to_stop': check_release(steps, read('forward-trials-01-analysis.json')),
        'T02_ground_dash': check_dash(steps, read('forward-dash-trials-01-analysis.json')),
        'T02b_air_drag': check_air_drag(steps, read('running-jump-trials-01-analysis.json')),
        'T03pre_T04_dash_slide': check_dash_slides(steps, read('dash-slide-trials-01-analysis.json')),
    }
    output.write_text(json.dumps(report, indent=1), encoding='utf-8')
    print(f'Wrote {output}')


if __name__ == '__main__':
    main()
