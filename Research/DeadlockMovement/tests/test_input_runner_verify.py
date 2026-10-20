import copy
import unittest
from pathlib import Path

from input_runner_verify import verify_inputs
from input_runner import compile_protocol, load_protocol


class InputVerificationTests(unittest.TestCase):
    def setUp(self):
        self.schedule = {'kind': 'requested_input_schedule', 'ticks': [
            {'tick': i, 'axes': {'forward': 0, 'strafe': 0},
             'view': {'yaw': 0, 'pitch': 0},
             'buttons': {'jump': i == 1, 'dash': False, 'crouch': False}}
            for i in range(3)]}
        self.rows = [
            {'type': 'step', 'sequence': i, 'clock_tick': 100 + i,
             'world': 1, 'pawn_handle': 7, 'buttons': [str(256 if i == 1 else 0), '0', '0'],
             'axes': [0, 0, 0], 'angles': [0, 0, 0]}
            for i in range(3)]
        self.binding = dict(start_tick=100, world=1, pawn_handle=7,
                            button_masks={'jump': 256, 'dash': 32, 'crouch': 4},
                            strafe_to_left=-1)

    def verify(self, rows=None):
        return verify_inputs(self.schedule, self.rows if rows is None else rows, **self.binding)

    def test_matched_press_and_release(self):
        report = self.verify()
        self.assertTrue(report['input_fidelity_passed'])
        self.assertTrue(report['release_observed'])
        self.assertEqual(report['action_success'], 'not_assessed')

    def test_late_press_fails(self):
        self.rows[1]['buttons'][0] = '0'
        self.rows[2]['buttons'][0] = '256'
        report = self.verify()
        self.assertFalse(report['input_fidelity_passed'])
        self.assertFalse(report['release_observed'])

    def test_missing_target_not_replaced_by_other_pawn(self):
        self.rows[1]['pawn_handle'] = 8
        self.assertIn('missing_tick', [e['code'] for e in self.verify()['issues']])

    def test_all_duplicate_tick_callbacks_checked(self):
        duplicate = copy.deepcopy(self.rows[1])
        duplicate['buttons'][0] = '0'
        self.rows.insert(2, duplicate)
        for i, row in enumerate(self.rows):
            row['sequence'] = i
        self.assertFalse(self.verify()['input_fidelity_passed'])

    def test_sequence_gap_fails_even_with_all_ticks(self):
        self.rows[2]['sequence'] = 4
        self.assertFalse(self.verify()['input_fidelity_passed'])

    def test_wraparound_yaw_matches(self):
        self.rows[1]['angles'][1] = 360
        self.assertTrue(self.verify()['input_fidelity_passed'])

    def test_explicit_axis_mapping(self):
        self.schedule['ticks'][1]['axes']['strafe'] = 1
        self.rows[1]['axes'][1] = -1
        self.assertTrue(self.verify()['input_fidelity_passed'])
        self.binding['strafe_to_left'] = 1
        self.assertFalse(self.verify()['input_fidelity_passed'])

    def test_unknown_or_overlapping_native_masks_rejected(self):
        self.binding['button_masks']['jump'] = 36
        with self.assertRaises(ValueError):
            self.verify()

    def test_nonfinite_input_not_accepted(self):
        self.rows[1]['axes'][0] = float('nan')
        self.assertFalse(self.verify()['input_fidelity_passed'])

    def test_composite_jump_mask_requires_both_bits(self):
        self.binding['button_masks']['jump'] = 256 | 512
        self.assertFalse(self.verify()['input_fidelity_passed'])
        self.rows[1]['buttons'][0] = str(256 | 512)
        self.assertTrue(self.verify()['input_fidelity_passed'])

    def test_clock_regression_detected(self):
        rows = [self.rows[1], self.rows[0], self.rows[2]]
        for i, row in enumerate(rows):
            row['sequence'] = i
        self.assertIn('clock_regression', [e['code'] for e in self.verify(rows)['issues']])

    def test_release_must_be_explicit_in_schedule(self):
        self.schedule['ticks'][-1]['buttons']['jump'] = True
        with self.assertRaises(ValueError):
            self.verify()

    def test_compiler_to_verifier_contract(self):
        source = Path(__file__).resolve().parents[1] / 'examples' / 'runner_jump.json'
        self.schedule = compile_protocol(load_protocol(source))
        self.rows = []
        for state in self.schedule['ticks']:
            held = sum(self.binding['button_masks'][name] for name, down in state['buttons'].items() if down)
            self.rows.append({'type': 'step', 'sequence': state['tick'],
                              'clock_tick': 100 + state['tick'], 'world': 1, 'pawn_handle': 7,
                              'buttons': [str(held), '0', '0'],
                              'axes': [state['axes']['forward'], -state['axes']['strafe'], 0],
                              'angles': [state['view']['pitch'], state['view']['yaw'], 0]})
        self.assertTrue(self.verify()['input_fidelity_passed'])
        press = next(i for i, row in enumerate(self.rows) if row['buttons'][0] != '0')
        self.rows[press]['buttons'][0] = '0'
        self.assertFalse(self.verify()['input_fidelity_passed'])


if __name__ == '__main__':
    unittest.main()
