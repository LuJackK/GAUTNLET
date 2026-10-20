"""Coarse client-console input smoke test; no tick-precision claim or motion writes.

Requires a manually verified local client endpoint and stationary private-server
pawn. The server recorder stays read-only. Output is evidence, not a pass verdict.
"""
import argparse
import json
import math
from pathlib import Path
import time
import socket
import threading

from live_console import ConsoleClient

RELEASE = '-forward;-back;-moveleft;-moveright;-duck;-in_mantle;-in_zipline;-in_innate1'


def latest_frame(path):
    with Path(path).open('rb') as f:
        size = f.seek(0, 2)
        f.seek(max(0, size - 65536))
        lines = f.read().splitlines()
    for line in reversed(lines):
        try:
            row = json.loads(line)
        except (ValueError, UnicodeError):
            continue
        if row.get('type') == 'frame':
            return row
    raise ValueError('No complete recent player frame')


class ProbeStopped(Exception):
    pass


def run_probe(run_dir, output, expected_pawn, expected_hero, port=29000):
    run_dir = Path(run_dir)
    frames = run_dir / 'frames.jsonl'
    manifest = json.loads((run_dir / 'manifest.json').read_text())
    if manifest.get('transport_status') != 'running' or manifest.get('synthetic') is True:
        raise ValueError('A running real capture is required')
    initial = latest_frame(frames)
    if initial['pawn_handle'] != expected_pawn or initial['hero_id'] != expected_hero:
        raise ValueError('Target identity mismatch')
    if not initial['grounded'] or math.sqrt(sum(v*v for v in initial['velocity'])) > 0.1:
        raise ValueError('Target must be standing still on ground')
    if not initial.get('stamina') or initial['stamina']['current'] < 1:
        raise ValueError('Insufficient observed stamina for dash probe')
    identity = tuple(initial[k] for k in ('world', 'slot', 'pawn_handle', 'session_generation', 'hero_id'))
    stop = run_dir / 'input-probe.stop'
    if stop.exists():
        raise ValueError('Stop request already exists; choose a new trial after review')
    with Path(output).open('x', encoding='utf-8') as log, ConsoleClient(port=port) as client:
        drain_stop = threading.Event()
        raw = Path(str(output) + '.console.bin').open('xb')
        def drain():
            total = 0
            try:
                while not drain_stop.is_set():
                    try:
                        block = client._socket.recv(65536)
                    except socket.timeout:
                        continue
                    except OSError:
                        break
                    if not block:
                        break
                    total += len(block)
                    if total > 64 * 1024 * 1024:
                        drain_stop.set()
                        break
                    raw.write(block)
            finally:
                raw.close()
        receiver = threading.Thread(target=drain, daemon=True)
        receiver.start()
        def record(kind, **fields):
            log.write(json.dumps({'kind': kind, 'monotonic_ns': time.monotonic_ns(), **fields}) + '\n')
            log.flush()

        def send(phase, command):
            record('command_request', phase=phase, command=command,
                   frame_tick_before=latest_frame(frames)['clock_tick'])
            result = client.send_command('echo PROBE_PHASE_' + phase + ';' + command)
            record('transport_sent', phase=phase, **result)

        def wait(seconds, check_stop=True):
            deadline = time.monotonic() + seconds
            last_tick, progressed = None, time.monotonic()
            while time.monotonic() < deadline:
                current = latest_frame(frames)
                if tuple(current[k] for k in ('world','slot','pawn_handle','session_generation','hero_id')) != identity:
                    raise ProbeStopped('target identity changed')
                if current['clock_tick'] != last_tick:
                    progressed = time.monotonic()
                    last_tick = current['clock_tick']
                elif time.monotonic() - progressed > 1:
                    raise ProbeStopped('recorder stopped advancing')
                if check_stop and stop.exists():
                    raise ProbeStopped('stop file requested')
                if drain_stop.is_set():
                    raise ProbeStopped('console output limit reached')
                time.sleep(0.03)

        record('probe_start', initial_frame=initial, backend='client_console_coarse',
               warning='Send timestamps are not engine input timestamps; no automatic motion reset.')
        try:
            for phase, command, seconds in [
                ('stand', RELEASE, 1.5),
                ('walk', '+forward', 0.65),
                ('walk_release', '-forward', 1.5),
                ('jump', '+in_mantle', 0.12),
                ('jump_release', '-in_mantle;-in_zipline', 2.0),
                ('dash_direction_prepare', '+forward', 0.3),
                ('dash', '+in_innate1', 0.12),
                ('dash_release', '-in_innate1', 0.7),
                ('all_release', RELEASE, 2.0),
                ('abort_while_forward_held', '+forward', 0.35),
            ]:
                send(phase, command)
                wait(seconds)
            # Exercise the same cleanup branch as a user stop.
            raise ProbeStopped('deliberate stop while forward held')
        except ProbeStopped as error:
            record('stop', reason=str(error))
        finally:
            try:
                send('finally_release', RELEASE)
                wait(1.5, check_stop=False)
                record('release_check_sample', frame=latest_frame(frames),
                       status='requires_raw_input_verification')
            except Exception as error:
                record('release_unconfirmed', reason=str(error))
                raise
            finally:
                drain_stop.set()
                receiver.join(timeout=2.5)
        record('probe_end', status='recorded_not_yet_validated')


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--run-dir', type=Path, required=True)
    p.add_argument('--output', type=Path, required=True)
    p.add_argument('--pawn', type=int, required=True)
    p.add_argument('--hero', type=int, required=True)
    p.add_argument('--port', type=int, default=29000)
    args = p.parse_args()
    run_probe(args.run_dir, args.output, args.pawn, args.hero, args.port)


if __name__ == '__main__':
    main()
