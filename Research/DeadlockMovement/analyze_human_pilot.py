"""Reproduce descriptive fits for the manually labeled first human pilot.

These are callback-clock empirical fits, not recovered native equations.
Consumes reviewed *-analysis.json interval labels; never rewrites raw evidence.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path
from statistics import mean

from capture_trace import validate_capture


def regression(points):
    xbar = mean(x for x, y in points)
    ybar = mean(y for x, y in points)
    denominator = sum((x - xbar) ** 2 for x, y in points)
    slope = sum((x - xbar) * (y - ybar) for x, y in points) / denominator
    return ybar - slope * xbar, slope


def quadratic(points):
    # Small least-squares system for z = c0 + c1*t + c2*t*t.
    matrix = [[sum(x ** (i + j) for x, y in points) for j in range(3)] +
              [sum(y * x ** i for x, y in points)] for i in range(3)]
    for i in range(3):
        pivot = max(range(i, 3), key=lambda k: abs(matrix[k][i]))
        matrix[i], matrix[pivot] = matrix[pivot], matrix[i]
        scale = matrix[i][i]
        if abs(scale) < 1e-12:
            raise ValueError('Insufficient independent times for fit')
        matrix[i] = [v / scale for v in matrix[i]]
        for k in range(3):
            if k != i:
                factor = matrix[k][i]
                matrix[k] = [a - factor * b for a, b in zip(matrix[k], matrix[i])]
    return [matrix[i][3] for i in range(3)]


def errors(values):
    return {'rmse': math.sqrt(mean(v * v for v in values)), 'max_abs': max(map(abs, values))}


def horizontal(row):
    return math.hypot(*row['after']['velocity'][:2])


def analyze(run):
    trace = run / 'trace.jsonl'
    validation = validate_capture(trace)
    if not validation['valid'] or validation['synthetic']:
        raise ValueError('Requires a complete validated real capture')
    records = [json.loads(line) for line in trace.open(encoding='utf-8')]
    steps = {r['sequence']: r for r in records if r['type'] == 'step'}
    sources = {}

    def labels(name):
        path = run / name
        sources[name] = hashlib.sha256(path.read_bytes()).hexdigest()
        return json.loads(path.read_text())

    def window(a, b):
        return [steps[i] for i in range(a, b + 1)]

    jumps = []
    for item in labels('standing-jump-trials-01-analysis.json')['airborne_candidates']:
        jumps.append(('standing', item['first'], item['last_airborne']))
    for item in labels('running-jump-trials-01-analysis.json')['airborne_candidates']:
        jumps.append(('running', item['first_sequence'], item['landing_sequence'] - 1))
    for item in labels('dash-slide-jump-trials-01-analysis.json')['candidates']:
        for flight in item['airborne_intervals']:
            jumps.append(('dash_crouch_jump', flight['takeoff_sequence'], flight['landing_sequence'] - 1))

    def jump_points(a, b, field):
        chunk = window(a, b)
        t0 = chunk[0]['clock_time_s']
        baseline = chunk[0]['before']['position'][2] if field == 'position' else 0
        return [(r['clock_time_s'] - t0, r['after'][field][2] - baseline) for r in chunk]

    train_position = [p for _, a, b in jumps[:2] for p in jump_points(a, b, 'position')]
    train_velocity = [p for _, a, b in jumps[:2] for p in jump_points(a, b, 'velocity')]
    position_coefficients = quadratic(train_position)
    velocity_intercept, velocity_slope = regression(train_velocity)
    jump_results = []
    for index, (kind, a, b) in enumerate(jumps):
        zp = jump_points(a, b, 'position'); vp = jump_points(a, b, 'velocity')
        jump_results.append({'kind': kind, 'first_sequence': a, 'last_airborne_sequence': b,
                             'split': 'fit' if index < 2 else 'held_out',
                             'position_error_raw': errors([y - sum(c * x ** i for i, c in enumerate(position_coefficients)) for x, y in zp]),
                             'velocity_error_raw': errors([y - velocity_intercept - velocity_slope * x for x, y in vp])})

    walks = []
    for trial in labels('forward-trials-01-analysis.json')['bursts']:
        chunk = window(trial['first'], trial['last'])
        peak = max(map(horizontal, chunk)); first = chunk[0]
        at95 = next(r for r in chunk if horizontal(r) >= peak * .95)
        release = next((r for r in chunk if r['axes'][0] == 0), None)
        walks.append({'first_sequence': first['sequence'], 'peak_speed_raw': peak,
                      'observed_motion_to_95pct_peak_s': at95['clock_time_s'] - first['clock_time_s'],
                      'release_to_below_1_speed_s': steps[trial['last'] + 1]['clock_time_s'] - release['clock_time_s'] if release else None})

    slides = []
    for trial in labels('dash-slide-trials-01-analysis.json')['candidates']:
        chunk = window(trial['first_sequence'], trial['window_last_sequence'])
        start = next((i for i, r in enumerate(chunk) if int(r['buttons'][0]) & 4), None)
        if start is None:
            continue
        end = next((i for i in range(start + 1, len(chunk)) if not int(chunk[i]['buttons'][0]) & 4 or chunk[i]['axes'][0] <= 0), len(chunk))
        segment = chunk[start:end]
        # Use after-peak motion, while both forward and crouch inputs remain held.
        peak_index = max(range(len(segment)), key=lambda i: horizontal(segment[i]))
        tail = segment[peak_index:]
        zspan = max(r['after']['position'][2] for r in segment) - min(r['after']['position'][2] for r in segment)
        points = [(r['clock_time_s'] - tail[0]['clock_time_s'], math.log(horizontal(r))) for r in tail if horizontal(r) > 50]
        fit = regression(points) if len(points) >= 5 else None
        slides.append({'trial': trial['trial'], 'first_sequence': segment[0]['sequence'], 'last_sequence': segment[-1]['sequence'],
                       'heading_deg': trial['heading_deg'], 'crouch_held_z_span_raw': zspan,
                       'flat_candidate': zspan < .25, 'peak_speed_raw': horizontal(segment[peak_index]),
                       'last_speed_raw': horizontal(segment[-1]), 'post_peak_samples': len(points),
                       'log_speed_intercept': fit[0] if fit else None,
                       'decay_rate_per_callback_second': -fit[1] if fit else None})
    eligible = [r for r in slides if r['flat_candidate'] and r['post_peak_samples'] >= 20 and r['peak_speed_raw'] > 600]
    fit_slides = eligible[::2]; test_slides = eligible[1::2]
    rate = mean(r['decay_rate_per_callback_second'] for r in fit_slides) if fit_slides else None
    slide_validation = []
    for trial in test_slides:
        chunk = window(trial['first_sequence'], trial['last_sequence'])
        peak = max(range(len(chunk)), key=lambda i: horizontal(chunk[i])); tail = chunk[peak:]
        initial = horizontal(tail[0]); t0 = tail[0]['clock_time_s']
        residuals = [horizontal(r) - initial * math.exp(-rate * (r['clock_time_s'] - t0)) for r in tail if horizontal(r) > 50]
        slide_validation.append({'trial': trial['trial'], 'error_raw': errors(residuals)})

    return {'trace_sha256': hashlib.sha256(trace.read_bytes()).hexdigest(), 'label_sha256': sources,
            'sample_count': len(steps), 'footer': records[-1], 'clock_units': 'callback engine seconds; per-command integration unverified',
            'jump_fit': {'training': 'first two standing jumps; t=0 is first airborne AFTER sample',
                         'z_relative_polynomial_coefficients': position_coefficients,
                         'vz_intercept': velocity_intercept, 'vz_slope': velocity_slope,
                         'trials': jump_results}, 'walking': walks, 'crouch_after_dash': slides,
            'slide_candidate_holdout': {'model': 'speed = measured peak * exp(-rate*t); ignores transition and termination',
                                        'fit_trial_ids': [r['trial'] for r in fit_slides], 'rate': rate,
                                        'held_out': slide_validation},
            'limitations': ['Post-hoc empirical models; no causal equation recovery claim.',
                            'No direct slide state, stamina or modifiers recorded.',
                            'Same-session held-out trials are not independent-session validation.',
                            'Jump horizontal behavior and collision transitions are not modeled.']}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('run', type=Path)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    report = analyze(args.run)
    with args.output.open('x', encoding='utf-8') as f:
        json.dump(report, f, indent=2, allow_nan=False)
        f.write('\n')
    print(args.output)
