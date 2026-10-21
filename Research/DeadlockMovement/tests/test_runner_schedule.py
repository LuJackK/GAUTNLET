import json
from pathlib import Path
import shutil
import struct
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
import input_runner
import runner_parity as parity
import runner_schedule as schedule
import runner_sweep as sweep


def compiled(name):
    return input_runner.compile_protocol(input_runner.load_protocol(ROOT / 'examples' / name))


def f32(value):
    return struct.unpack('<f', struct.pack('<f', value))[0]


class MaskTests(unittest.TestCase):
    def test_human_observed_bits(self):
        self.assertEqual(schedule.JUMP, 72339069014638592)
        self.assertEqual(schedule.DASH, 17592186044416)
        self.assertEqual((schedule.FORWARD, schedule.BACK, schedule.CROUCH, schedule.LEFT, schedule.RIGHT),
                         (8, 16, 4, 512, 1024))

    def test_axes_and_strafe_sign(self):
        row = {'axes': {'forward': -1.0, 'strafe': 1.0}, 'buttons': {'crouch': True, 'jump': False, 'dash': False},
               'view': {'yaw': 1.0, 'pitch': 2.0}}
        held, forward, left, pitch, yaw = schedule.native_tick(row)
        self.assertEqual(held, schedule.BACK | schedule.RIGHT | schedule.CROUCH)
        self.assertEqual((forward, left, pitch, yaw), (-1.0, -1.0, 2.0, 1.0))


class ConvertTests(unittest.TestCase):
    def test_parity_protocols_follow_human_offsets(self):
        trials = schedule.build_trials(compiled('parity_dash_slide.json'), rest_ticks=0)
        ticks = trials[0]['ticks']
        dash = next(i for i, t in enumerate(ticks) if t[0] & schedule.DASH)
        crouch = next(i for i, t in enumerate(ticks) if t[0] & schedule.CROUCH)
        self.assertEqual(crouch - dash, 27)  # human-tests-20261020-01: 67282 - 67255
        self.assertEqual(ticks[8][1], 0.75)
        jump = schedule.build_trials(compiled('parity_air_jump.json'), rest_ticks=0)[0]['ticks']
        presses = [i for i in range(len(jump)) if jump[i][0] & schedule.JUMP and not jump[i - 1][0] & schedule.JUMP]
        self.assertEqual(presses[1] - presses[0], 40)

    def test_native_round_trip_with_start_state_and_rest(self):
        trials = schedule.build_trials(compiled('parity_ground_jump.json'),
                                       start_state=[1, 2, 3, 0, 0, 0], rest_ticks=5)
        text = schedule.format_native(trials)
        self.assertTrue(text.startswith('GRS1\ntrial parity-ground-jump 0 125 start 1.0 2.0 3.0 0.0 0.0 0.0\n'))
        parsed = schedule.parse_native(text)
        self.assertEqual(parsed[0]['ticks'], trials[0]['ticks'])
        self.assertEqual(parsed[0]['start'], [1, 2, 3, 0, 0, 0])
        self.assertEqual(parsed[0]['ticks'][-1][0], 0)

    def test_sweep_becomes_one_trial_per_offset(self):
        protocol = input_runner.load_protocol(ROOT / 'examples' / 'runner_dash_jump.json')
        document = input_runner.compile_sweep(protocol, [3, 4], list(range(11, 37)))
        trials = schedule.build_trials(document, rest_ticks=0)
        self.assertEqual(len(trials), 26)
        delays = []
        for trial in trials:
            held = [t[0] for t in trial['ticks']]
            dash = next(i for i, h in enumerate(held) if h & schedule.DASH)
            jump = next(i for i, h in enumerate(held) if h & schedule.JUMP)
            delays.append(jump - dash)
        self.assertEqual(delays, list(range(15, 41)))

    def test_cli_runs_several_protocols_as_consecutive_trials(self):
        directory = Path(tempfile.mkdtemp())
        self.addCleanup(lambda: shutil.rmtree(directory, ignore_errors=True))
        output = directory / 'parity.grs'
        code = schedule.main(['convert', str(ROOT / 'examples' / 'parity_ground_jump.json'),
                              str(ROOT / 'examples' / 'parity_dash_slide_25.json'), '--output', str(output),
                              '--start-state=-442,-3155,376,0,0,0', '--rest-ticks', '3'])
        self.assertEqual(code, 0)
        trials = schedule.parse_native(output.read_text(encoding='utf-8'))
        self.assertEqual([t['id'] for t in trials], ['parity-ground-jump', 'parity-dash-slide-25'])
        self.assertEqual(trials[1]['start'], [-442, -3155, 376, 0, 0, 0])
        held = [t[0] for t in trials[1]['ticks']]
        dash = next(i for i, h in enumerate(held) if h & schedule.DASH)
        self.assertEqual(next(i for i, h in enumerate(held) if h & schedule.CROUCH) - dash, 25)

    def test_rejects_truncated_native_file(self):
        with self.assertRaises(schedule.ScheduleError):
            schedule.parse_native('GRS1\ntrial a 0 2\nt 0 0.0 0.0 0.0 0.0\nend\n')


def runner_log(trials, mode='apply', tamper=None):
    lines = [{'type': 'runner_session', 'scripted': True, 'mode': mode, 'trials': len(trials)}]
    for index, trial in enumerate(trials):
        lines.append({'type': 'trial_start', 'index': index, 'trial': trial['id'], 'clock_tick': 100})
        previous = 0
        for tick, (held, forward, left, pitch, yaw) in enumerate(trial['ticks']):
            applied = {'held': str(held), 'changed': str(held ^ previous), 'scroll': '0',
                       'angles': [f32(pitch), f32(yaw), 0], 'axes': [f32(forward), f32(left), 0],
                       'command_number': tick, 'client_tick': tick}
            lines.append({'type': 'cmd', 'trial': index, 'tick': tick, 'clock_tick': 100 + tick,
                          'observed': applied, 'applied': applied, 'readback': applied if mode == 'apply' else None})
            previous = held
        lines.append({'type': 'runner_done' if index == len(trials) - 1 else 'trial_end', 'index': index,
                      'clock_tick': 100 + len(trial['ticks'])})
    if tamper:
        tamper(lines)
    return [json.dumps(line) for line in lines]


class CheckTests(unittest.TestCase):
    def setUp(self):
        self.trials = schedule.parse_native(schedule.format_native(
            schedule.build_trials(compiled('parity_ground_jump.json'), rest_ticks=2)))

    def test_clean_log_passes(self):
        self.assertEqual(schedule.check_runner_log(self.trials, runner_log(self.trials)), (True, []))
        self.assertTrue(schedule.check_runner_log(self.trials, runner_log(self.trials, 'observe'))[0])

    def test_missing_command_and_changed_readback_fail(self):
        ok, problems = schedule.check_runner_log(
            self.trials, runner_log(self.trials, tamper=lambda lines: lines.pop(10)))
        self.assertFalse(ok)
        self.assertIn('consecutive', problems[0])

        def tamper(lines):
            lines[20]['readback'] = dict(lines[20]['readback'], held='0')
        ok, problems = schedule.check_runner_log(self.trials, runner_log(self.trials, tamper=tamper))
        self.assertFalse(ok)
        self.assertIn('readback', problems[0])

    def test_runner_error_is_reported(self):
        ok, problems = schedule.check_runner_log(
            self.trials, runner_log(self.trials, tamper=lambda lines: lines.append(
                {'type': 'runner_error', 'error': 'rewrite failed'})))
        self.assertFalse(ok)
        self.assertIn('rewrite failed', problems[0])


def step(tick, held, hs, vz, ground=True, dash=False, pawn=1):
    return {'type': 'step', 'clock_tick': tick, 'pawn_handle': pawn, 'buttons': [str(held), '0', '0'],
            'after': {'velocity': [hs, 0, vz], 'ground_entity': 5 if ground else 4294967295},
            'before': {'dash_vector': [1, 0, 0] if dash else None}}


def write_trace(path, steps):
    Path(path).write_text(''.join(json.dumps(s) + '\n' for s in steps), encoding='utf-8')


class ParityTests(unittest.TestCase):
    def run_pair(self, scripted_hs=500.0):
        directory = Path(tempfile.mkdtemp())
        self.addCleanup(lambda: shutil.rmtree(directory, ignore_errors=True))

        def trace(offset, hs):
            steps = [step(offset + i, 0, 0, 0) for i in range(10)]
            steps += [step(offset + 10, schedule.DASH, 0.2, 0, dash=True)]
            steps += [step(offset + 11 + i, 0, hs, 0, dash=True) for i in range(20)]
            return steps
        write_trace(directory / 'human.jsonl', trace(1000, 500.0))
        write_trace(directory / 'scripted.jsonl', trace(50, scripted_hs))
        (directory / 'runner.jsonl').write_text(json.dumps(
            {'type': 'trial_start', 'index': 0, 'clock_tick': 40}) + '\n', encoding='utf-8')
        return parity.parity(directory / 'human.jsonl', 1010, directory / 'scripted.jsonl',
                             directory / 'runner.jsonl', 'dash', pre=5, post=15)

    def test_matching_traces_pass_regardless_of_absolute_tick(self):
        result = self.run_pair()
        self.assertTrue(result['pass'])
        self.assertEqual(result['scripted_tick'], 60)
        self.assertEqual(result['steps'], 21)

    def test_speed_difference_is_reported(self):
        result = self.run_pair(scripted_hs=510.0)
        self.assertFalse(result['pass'])
        self.assertEqual(result['mismatch_count'], 15)


class SweepTests(unittest.TestCase):
    def test_window_is_the_contiguous_dash_jump_delays(self):
        directory = Path(tempfile.mkdtemp())
        self.addCleanup(lambda: shutil.rmtree(directory, ignore_errors=True))
        steps, log, tick = [], [], 0
        for index, delay in enumerate((15, 16, 17, 18, 19)):
            log.append({'type': 'trial_start', 'index': index, 'clock_tick': tick})
            steps.append(step(tick, schedule.DASH, 0, 0, dash=True))
            for i in range(1, delay):
                steps.append(step(tick + i, 0, 600, 0, dash=True))
            vz = 412.5 if delay >= 17 else 0.0
            steps.append(step(tick + delay, schedule.JUMP, 700, vz, ground=vz == 0.0))
            steps.append(step(tick + delay + 1, 0, 700, vz, ground=vz == 0.0))
            tick += 300
            log.append({'type': 'trial_end', 'index': index, 'clock_tick': tick - 1})
        write_trace(directory / 'trace.jsonl', steps)
        (directory / 'runner.jsonl').write_text(''.join(json.dumps(r) + '\n' for r in log), encoding='utf-8')
        result = sweep.analyze(directory / 'trace.jsonl', directory / 'runner.jsonl')
        self.assertEqual(result['window'], [17, 19])
        self.assertTrue(result['contiguous'])
        self.assertEqual([r['outcome'] for r in result['trials']],
                         ['no_air', 'no_air', 'dash_jump', 'dash_jump', 'dash_jump'])


if __name__ == '__main__':
    unittest.main()
