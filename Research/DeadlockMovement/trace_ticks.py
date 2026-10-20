"""Print per-tick movement tables around events in a capture trace.

Each EVENT is CLOCK_TICK[:BEFORE:AFTER] (default 2 before, 10 after). Columns: offset, input axes,
held buttons, view yaw, incoming velocity and horizontal speed, outgoing horizontal speed and vz,
ground (G) or air (A) after the step, and D while the ground-dash vector is set. Read only.

    python trace_ticks.py local/captures/human-tests-20261020-04 40028:2:30 60505
"""
import argparse
import json
import math
from pathlib import Path

NO_GROUND = 4294967295


def parse_event(text):
    parts = [int(p) for p in text.split(':')]
    tick, before, after = (parts + [2, 10])[:3] if len(parts) == 1 else (parts + [10])[:3]
    return tick, before, after


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('capture')
    parser.add_argument('events', nargs='+', type=parse_event)
    args = parser.parse_args()
    wanted = {}
    for tick, before, after in args.events:
        for k in range(tick - before, tick + after + 1):
            wanted.setdefault(k, tick)
    rows = {}
    with open(Path(args.capture) / 'trace.jsonl', encoding='utf-8') as stream:
        for line in stream:
            if '"step"' not in line:
                continue
            record = json.loads(line)
            if record.get('type') == 'step' and record.get('clock_tick') in wanted:
                rows[record['clock_tick']] = record
    horizontal = lambda v: math.hypot(v[0], v[1])
    for tick, _, _ in args.events:
        print(f'== event {tick}')
        for k in sorted(t for t in rows if wanted[t] == tick):
            r = rows[k]
            vin, vout = r['before']['velocity'], r['after']['velocity']
            ground = 'A' if r['after']['ground_entity'] in (None, NO_GROUND) else 'G'
            dash = 'D' if r['after'].get('dash_vector') else '-'
            print(f"{k - tick:+4d} axes={r['axes'][:2]} buttons={r['buttons'][0]:>17} yaw={r['angles'][1]:7.1f} "
                  f"in=({vin[0]:7.1f},{vin[1]:7.1f},{vin[2]:6.1f}) hs={horizontal(vin):6.1f} "
                  f"out hs={horizontal(vout):6.1f} vz={vout[2]:6.1f} {ground} {dash}")


if __name__ == '__main__':
    main()
