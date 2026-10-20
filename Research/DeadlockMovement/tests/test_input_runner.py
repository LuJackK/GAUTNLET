import contextlib
import copy
import io
import json
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import input_runner as runner


def protocol():
    return {
        'schema_version': 1, 'trial_id': 'walk-jump', 'target_slot': 0,
        'tick_rate_hz': 64, 'max_ticks': 12, 'settle_ticks': 3,
        'initial_conditions': {
            'hero_id': 1, 'grounded': True, 'rest_speed_max': 0.1,
            'min_stamina': 3, 'stable_ticks': 64, 'alive': True,
            'configuration_id': 'mina-superior-stamina', 'require_configuration_confirmation': True,
            'inactive_states': ['sliding', 'dashing', 'climbing', 'mantling']},
        'initial_view': {'yaw': 15, 'pitch': 0},
        'abort_policy': {**dict.fromkeys(runner.ABORT_REASONS, True),
                         'release_all': True, 'max_prepare_ticks': 512},
        'events': [
            {'tick': 0, 'kind': 'axes', 'forward': 1, 'strafe': 0},
            {'tick': 2, 'kind': 'button', 'button': 'jump', 'action': 'press'},
            {'tick': 3, 'kind': 'button', 'button': 'jump', 'action': 'release'},
            {'tick': 6, 'kind': 'axes', 'forward': 0, 'strafe': 0}],
        'assertions': [{'tick': 11, 'kind': 'grounded', 'equals': True}]}


class InputRunnerTests(unittest.TestCase):
    def test_requested_states_apply_at_tick_and_persist_until_release(self):
        source = protocol()
        original = copy.deepcopy(source)
        result = runner.compile_protocol(source)
        self.assertEqual(source, original)
        self.assertEqual(result['execution_status'], 'offline_only_unverified')
        self.assertEqual(len(result['ticks']), 12)
        rows = result['ticks']
        self.assertEqual([row['tick'] for row in rows], list(range(12)))
        self.assertFalse(rows[1]['buttons']['jump'])
        self.assertTrue(rows[2]['buttons']['jump'])
        self.assertFalse(rows[3]['buttons']['jump'])
        self.assertEqual(rows[2]['event_indices'], [1])
        self.assertEqual(rows[5]['axes']['forward'], 1)
        self.assertEqual(rows[6]['axes']['forward'], 0)
        self.assertEqual(rows[8]['phase'], 'execute')
        self.assertTrue(all(row['phase'] == 'settle' for row in rows[9:]))
        self.assertEqual(rows[11]['view'], {'yaw': 15, 'pitch': 0})
        self.assertEqual(result['neutral_release'], {'axes': {'forward': 0, 'strafe': 0},
                                                   'buttons': dict.fromkeys(runner.BUTTONS, False)})
        rows[0]['buttons']['jump'] = True
        self.assertFalse(rows[1]['buttons']['jump'])

    def test_hash_and_schedule_deterministic_across_dictionary_order(self):
        first = protocol()
        second = dict(reversed(list(first.items())))
        self.assertEqual(runner.compile_protocol(first), runner.compile_protocol(second))
        changed = copy.deepcopy(first)
        changed['events'][1]['tick'] = 1
        self.assertNotEqual(runner.compile_protocol(first)['protocol_sha256'],
                            runner.compile_protocol(changed)['protocol_sha256'])

    def test_same_tick_independent_channels_and_view_changes(self):
        source = protocol()
        source['events'].insert(2, {'tick': 2, 'kind': 'view', 'yaw': -90, 'pitch': 20})
        source['events'].insert(3, {'tick': 2, 'kind': 'button', 'button': 'dash', 'action': 'press'})
        source['events'].insert(5, {'tick': 3, 'kind': 'button', 'button': 'dash', 'action': 'release'})
        rows = runner.compile_protocol(source)['ticks']
        self.assertEqual(rows[2]['event_indices'], [1, 2, 3])
        self.assertTrue(rows[2]['buttons']['dash'])
        self.assertTrue(rows[2]['buttons']['jump'])
        self.assertEqual(rows[2]['view'], {'yaw': -90, 'pitch': 20})
        self.assertEqual(rows[4]['view'], rows[2]['view'])
        self.assertFalse(rows[3]['buttons']['dash'])

    def test_unknown_fields_and_missing_initial_requirements_rejected(self):
        for section, key in ((None, 'position'), ('initial_conditions', 'velocity'),
                             ('abort_policy', 'ignore_human'), ('initial_view', 'roll')):
            with self.subTest(section=section, key=key):
                source = protocol()
                (source if section is None else source[section])[key] = 0
                with self.assertRaisesRegex(runner.ProtocolError, 'unknown fields'):
                    runner.validate_protocol(source)
        for key in protocol()['initial_conditions']:
            source = protocol()
            del source['initial_conditions'][key]
            with self.subTest(missing=key), self.assertRaisesRegex(runner.ProtocolError, 'missing fields'):
                runner.validate_protocol(source)

    def test_numbers_and_types_are_strict(self):
        cases = [('target_slot', True), ('target_slot', -1), ('target_slot', 64),
                 ('max_ticks', 4097), ('max_ticks', 3.0), ('settle_ticks', 0),
                 ('tick_rate_hz', 128), ('trial_id', 'unsafe/name')]
        for key, value in cases:
            source = protocol()
            source[key] = value
            with self.subTest(key=key, value=value), self.assertRaises(runner.ProtocolError):
                runner.validate_protocol(source)
        for value in (True, '1', float('nan'), float('inf'), -float('inf'), 2, 10 ** 1000):
            source = protocol()
            source['events'][0]['forward'] = value
            with self.subTest(value=str(value)[:30]), self.assertRaises(runner.ProtocolError):
                runner.validate_protocol(source)

    def test_initial_resource_and_abort_preconditions_cannot_be_weakened(self):
        for section, key, value in (
                ('initial_conditions', 'rest_speed_max', 2),
                ('initial_conditions', 'min_stamina', -1),
                ('initial_conditions', 'grounded', None),
                ('initial_conditions', 'alive', False),
                ('initial_conditions', 'configuration_id', ''),
                ('initial_conditions', 'require_configuration_confirmation', False),
                ('initial_conditions', 'inactive_states', []),
                ('abort_policy', 'release_all', False),
                *[('abort_policy', reason, False) for reason in runner.ABORT_REASONS]):
            source = protocol()
            source[section][key] = value
            with self.subTest(key=key), self.assertRaises(runner.ProtocolError):
                runner.validate_protocol(source)

    def test_event_order_conflicts_and_unbalanced_edges_are_rejected(self):
        cases = []
        source = protocol()
        source['events'][1:3] = reversed(source['events'][1:3])
        cases.append((source, 'ordered'))
        source = protocol()
        source['events'].insert(2, {'tick': 2, 'kind': 'button', 'button': 'jump', 'action': 'release'})
        cases.append((source, 'conflicting'))
        source = protocol()
        source['events'][2]['action'] = 'press'
        cases.append((source, 'duplicate press'))
        source = protocol()
        source['events'][1]['action'] = 'release'
        cases.append((source, 'release without'))
        source = protocol()
        del source['events'][2]
        cases.append((source, 'unresolved held'))
        source = protocol()
        source['events'].pop()
        cases.append((source, 'neutralize axes'))
        source = protocol()
        source['events'][-1]['tick'] = 9
        cases.append((source, 'integer'))
        for source, expected in cases:
            with self.subTest(expected=expected), self.assertRaisesRegex(runner.ProtocolError, expected):
                runner.validate_protocol(source)

    def test_assertions_have_explicit_bounds_and_known_observations(self):
        for assertion in ({'tick': 12, 'kind': 'grounded', 'equals': True},
                          {'tick': 0, 'kind': 'speed', 'min': 5, 'max': 4},
                          {'tick': 1, 'kind': 'grounded', 'equals': 1},
                          {'tick': 1, 'kind': 'position', 'equals': [0, 0, 0]}):
            source = protocol()
            source['assertions'] = [assertion]
            with self.subTest(assertion=assertion), self.assertRaises(runner.ProtocolError):
                runner.validate_protocol(source)
        source = protocol()
        source['assertions'] *= 2
        with self.assertRaisesRegex(runner.ProtocolError, 'duplicate assertion'):
            runner.validate_protocol(source)

    def test_sweeps_preserve_pulses_and_reject_invalid_variants(self):
        source = protocol()
        result = runner.compile_sweep(source, [1, 2], [-1, 0, 1, 2])
        self.assertEqual(len(result['trials']), 4)
        for trial, press_tick in zip(result['trials'], (1, 2, 3, 4)):
            self.assertEqual([row['tick'] for row in trial['ticks'] if row['buttons']['jump']], [press_tick])
        self.assertEqual(source, protocol())
        for indices, offsets in (([1], [2]), ([1, 2], [-3]), ([1, 2], [7]),
                                 ([1, 1], [0]), ([9], [0]), ([1], [0, 0]),
                                 ([True], [0]), ([1], [True]), ([], [0]), ([1], []),
                                 ([1], list(range(129)))):
            with self.subTest(indices=indices, offsets=offsets[:4]), self.assertRaises(runner.ProtocolError):
                runner.compile_sweep(source, indices, offsets)
        source['max_ticks'] = runner.MAX_TICKS
        with self.assertRaisesRegex(runner.ProtocolError, 'total tick limit'):
            runner.compile_sweep(source, [1, 2], list(range(17)))

    def test_file_cli_duplicate_keys_and_no_overwrite(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            source = directory / 'protocol.json'
            output = directory / 'compiled.json'
            source.write_text(json.dumps(protocol()), encoding='utf-8')
            with contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(runner.main(['validate', str(source)]), 0)
                self.assertEqual(runner.main(['compile', str(source), '--output', str(output)]), 0)
            saved = output.read_bytes()
            with contextlib.redirect_stderr(io.StringIO()):
                self.assertEqual(runner.main(['compile', str(source), '--output', str(output)]), 2)
                self.assertEqual(runner.main(['compile', str(source), '--output', str(source)]), 2)
            self.assertEqual(output.read_bytes(), saved)
            self.assertEqual(json.loads(source.read_text()), protocol())
            for payload in ('{"schema_version":1,"schema_version":1}', '{"x":NaN}', '{'):
                source.write_text(payload, encoding='utf-8')
                with self.assertRaises(runner.ProtocolError):
                    runner.load_protocol(source)
            source.write_text(json.dumps(protocol()), encoding='utf-8')
            with contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(runner.main(['sweep', str(source), '--event-indices', '1,2',
                                              '--offset-ticks', '0,1', '--output', str(directory / 'sweep.json')]), 0)
            self.assertEqual(len(json.loads((directory / 'sweep.json').read_text())['trials']), 2)

    def test_all_examples_compile(self):
        paths = sorted((Path(runner.__file__).parent / 'examples').glob('runner*.json'))
        self.assertGreaterEqual(len(paths), 3)
        for path in paths:
            with self.subTest(path=path.name):
                self.assertEqual(runner.compile_protocol(runner.load_protocol(path))['kind'],
                                 'requested_input_schedule')


if __name__ == '__main__':
    unittest.main()
