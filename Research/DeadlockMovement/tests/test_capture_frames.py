import contextlib
import copy
import io
import json
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from capture_frames import main, validate_frames


def frame_header(synthetic=True):
    return {'type': 'frame_session', 'schema_version': 1, 'synthetic': synthetic,
            'phase': 'after_game_frame'}


def frame(sequence=0, world=1, clock_tick=None, clock_time_s=None, movement_sequence=None):
    return {'type': 'frame', 'sequence': sequence, 'world': world, 'clock_tick': clock_tick,
            'clock_time_s': clock_time_s, 'movement_sequence': movement_sequence,
            'slot': 0, 'pawn_handle': 1, 'hero_id': 2, 'session_generation': 3,
            'position': [0, 0, 0], 'velocity': None, 'ground_entity': None,
            'grounded': None, 'sliding': None, 'dashing': None, 'climbing': None,
            'mantling': None, 'stamina': None, 'abilities': None}


def marker(sequence=1):
    return {'type': 'marker', 'sequence': sequence, 'world': 1, 'clock_tick': None,
            'clock_time_s': None, 'movement_sequence': None, 'request_id': 'request_1',
            'trial_id': 'fall-01', 'event': 'start'}


def frame_end(attempted=1, written=1, dropped=0):
    return {'type': 'frame_end', 'attempted': attempted, 'written': written, 'dropped': dropped}


def ability():
    return {'handle': 1, 'slot': 65535, 'charges': -1, 'cooldown_start': 0,
            'cooldown_end': 1.5, 'recharge_start': -0.5, 'recharge_end': 2}


class FrameTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.path = Path(self.temp.name) / 'frames.jsonl'

    def write(self, rows, path=None):
        path = path or self.path
        path.write_text(''.join(json.dumps(row) + '\n' for row in rows), encoding='utf-8')
        return path

    def report(self, rows, **kwargs):
        return validate_frames(self.write(rows), **kwargs)

    def test_nullable_synthetic_frame_is_explicitly_unavailable_and_not_real_evidence(self):
        report = self.report([frame_header(), frame(), frame_end()])
        self.assertTrue(report['valid'])
        self.assertTrue(report['complete'])
        self.assertTrue(report['synthetic'])
        self.assertIn('SYNTHETIC', report['warnings'][0])
        self.assertEqual(report['evidence_status'], 'synthetic_only_not_real_game_evidence')
        self.assertIn('no state or readiness is inferred', report['warnings'][-1])
        self.assertEqual(report['native_compatibility'], 'unassessed')

    def test_observed_states_abilities_and_markers_count_together(self):
        row = frame(clock_tick=5, clock_time_s=1)
        row.update(velocity=[1, 2, 3], ground_entity=0, grounded=True, sliding=False,
                   dashing=False, climbing=True, mantling=False,
                   stamina={'current': 0.0, 'max': 3.0}, abilities=[ability()])
        second = frame(2, clock_tick=5, clock_time_s=1)
        second['abilities'] = []
        report = self.report([frame_header(False), row, marker(), second, frame_end(3, 3)])
        self.assertTrue(report['valid'])
        self.assertEqual(report['frame_count'], 2)
        self.assertEqual(report['marker_count'], 1)
        self.assertEqual(report['data_records'], 3)
        self.assertEqual(report['markers'][0]['trial_id'], 'fall-01')
        self.assertEqual(report['markers'][0]['event'], 'start')
        self.assertEqual(report['worlds'][0]['callback_clock_span_s'], 0)

    def test_optional_friction_and_ability_class_fields(self):
        slide = {**ability(), 'class': 'CCitadel_Ability_Slide',
                 'fields': {'m_bIsSliding': 1, 'm_flSpeedAdjust': 207.1}}
        row = {**frame(), 'friction': 0.8, 'surface_friction': None, 'abilities': [slide]}
        self.assertTrue(self.report([frame_header(), row, frame_end()])['valid'])
        for name, value in (('class', 7), ('class', ''), ('fields', []),
                            ('fields', {'bad': 1}), ('fields', {'m_flX': 'x'})):
            bad = {**frame(), 'abilities': [{**ability(), name: value}]}
            with self.subTest(name=name, value=value):
                self.assertFalse(self.report([frame_header(), bad, frame_end()])['valid'])
        bad = {**frame(), 'friction': 'x'}
        self.assertFalse(self.report([frame_header(), bad, frame_end()])['valid'])

    def test_no_physical_availability_or_readiness_inference(self):
        row = frame()
        row.update(grounded=True, ground_entity=None, velocity=None,
                   stamina={'current': -1, 'max': 0}, abilities=[ability()])
        self.assertTrue(self.report([frame_header(), row, frame_end()])['valid'])

    def test_all_marker_events_and_identifier_boundaries(self):
        rows = [frame_header(), frame()]
        for sequence, event in enumerate(('arm', 'start', 'end', 'abort'), 1):
            row = marker(sequence)
            row.update(event=event, request_id='A_-' + 'x' * 61, trial_id='A_-012')
            rows.append(row)
        rows.append(frame_end(5, 5))
        self.assertTrue(self.report(rows)['valid'])

    def test_invalid_frame_fields_and_uint_boundaries(self):
        cases = [('sequence', -1), ('sequence', 1 << 64), ('sequence', True),
                 ('world', 0), ('world', True), ('clock_tick', -1), ('clock_tick', 1 << 64),
                 ('clock_time_s', False), ('clock_time_s', float('inf')),
                 ('movement_sequence', -1), ('movement_sequence', 1 << 64),
                 ('slot', -1), ('slot', 64), ('slot', False),
                 ('pawn_handle', None), ('pawn_handle', 1 << 32),
                 ('hero_id', -1), ('session_generation', True),
                 ('position', [0, 0]), ('position', [0, False, 0]),
                 ('velocity', [0, 0]), ('velocity', [0, 0, float('nan')]),
                 ('ground_entity', -1), ('ground_entity', 1 << 32),
                 ('grounded', 0), ('sliding', 'false'), ('dashing', []),
                 ('climbing', {}), ('mantling', 1), ('stamina', []),
                 ('stamina', {'current': 0}), ('stamina', {'current': False, 'max': 3}),
                 ('stamina', {'current': 0, 'max': float('inf')}), ('abilities', {})]
        for name, value in cases:
            row = frame()
            row[name] = value
            with self.subTest(name=name, value=value):
                self.assertFalse(self.report([frame_header(), row, frame_end()])['valid'])
        row = frame(clock_tick=(1 << 64) - 1, movement_sequence=(1 << 64) - 1)
        row.update(slot=63, pawn_handle=(1 << 32) - 1, hero_id=(1 << 32) - 1,
                   session_generation=(1 << 32) - 1, ground_entity=(1 << 32) - 1)
        self.assertTrue(self.report([frame_header(), row, frame_end()])['valid'])

    def test_invalid_ability_fields_and_missing_fields(self):
        cases = [('handle', 1 << 32), ('handle', None), ('slot', 65536), ('slot', -1),
                 ('charges', 1 << 31), ('charges', -(1 << 31) - 1), ('charges', True),
                 ('cooldown_start', None), ('cooldown_end', float('nan')),
                 ('recharge_start', '0'), ('recharge_end', float('inf'))]
        for name, value in cases:
            row = frame()
            row['abilities'] = [{**ability(), name: value}]
            with self.subTest(name=name):
                self.assertFalse(self.report([frame_header(), row, frame_end()])['valid'])
        for name in ability():
            entry = ability()
            del entry[name]
            row = frame()
            row['abilities'] = [entry]
            with self.subTest(missing=name):
                self.assertFalse(self.report([frame_header(), row, frame_end()])['valid'])

    def test_all_frame_and_marker_fields_are_required_even_nullable_ones(self):
        for original in (frame(), marker(0)):
            for name in original:
                row = copy.deepcopy(original)
                del row[name]
                with self.subTest(kind=original['type'], missing=name):
                    report = self.report([frame_header(), row, frame_end()])
                    self.assertFalse(report['valid'])
                    self.assertFalse(report['format_valid'])

    def test_unsafe_marker_names_and_events_are_rejected(self):
        for field in ('request_id', 'trial_id'):
            for value in ('', '../path', 'a b', 'x' * 65, 'caf\u00e9', 'trial\nstart', 12, None):
                row = marker()
                row[field] = value
                with self.subTest(field=field, value=value):
                    self.assertFalse(self.report([frame_header(), frame(), row, frame_end(2, 2)])['valid'])
        row = marker()
        row['event'] = 'finish'
        self.assertFalse(self.report([frame_header(), frame(), row, frame_end(2, 2)])['valid'])

    def test_initial_internal_trailing_loss_and_footer_counts(self):
        report = self.report([frame_header(), frame(2), marker(4), frame_end(7, 2, 5)])
        self.assertTrue(report['format_valid'])
        self.assertFalse(report['valid'])
        self.assertFalse(report['complete'])
        self.assertEqual(report['sequence']['missing_samples'], 5)
        self.assertEqual([gap['count'] for gap in report['sequence']['gaps']], [2, 1, 2])
        for footer in (frame_end(2, 1), frame_end(1, 2), frame_end(0, 0), frame_end(2, 1, 0)):
            with self.subTest(footer=footer):
                self.assertFalse(self.report([frame_header(), frame(), footer])['valid'])

    def test_duplicate_and_reversed_sequences_and_global_movement_refs_fail(self):
        for sequences in ((0, 0), (1, 0)):
            with self.subTest(sequences=sequences):
                self.assertFalse(self.report([frame_header(), *(frame(value) for value in sequences), frame_end(2, 2)])['valid'])
        report = self.report([frame_header(), frame(movement_sequence=5),
                              frame(1, world=2, movement_sequence=4), frame_end(2, 2)])
        self.assertFalse(report['valid'])
        self.assertIn('movement_sequence_regression', [error['code'] for error in report['errors']])

    def test_clock_checks_are_per_world_and_keep_duplicate_timestamps(self):
        rows = [frame_header(), frame(clock_tick=5, clock_time_s=1),
                frame(1, world=2, clock_tick=0, clock_time_s=0),
                frame(2, clock_tick=5, clock_time_s=1), frame_end(3, 3)]
        self.assertTrue(self.report(rows)['valid'])
        rows[3]['clock_tick'] = 4
        rows[3]['clock_time_s'] = 0.5
        report = self.report(rows)
        self.assertTrue(report['format_valid'])
        self.assertFalse(report['valid'])
        self.assertEqual(report['clock_tick_regressions'], 1)
        self.assertEqual(report['clock_time_regressions'], 1)
        self.assertIsNone(report['worlds'][0]['callback_clock_span_s'])

    def test_null_clock_observations_do_not_hide_regressions(self):
        report = self.report([frame_header(), frame(clock_tick=5), frame(1),
                              frame(2, clock_tick=4), frame_end(3, 3)])
        self.assertEqual(report['clock_tick_regressions'], 1)

    def test_empty_marker_only_missing_headers_and_footers_are_invalid(self):
        for rows in ([], [frame_header()], [frame_header(), frame_end(0, 0)],
                     [frame_header(), marker(0), frame_end()], [frame(), frame_end()],
                     [frame_header(), frame()], [frame_header(), frame_header(), frame(), frame_end()],
                     [frame_header(), frame(), frame_end(), marker()],
                     [frame_header(), {'type': 'unknown'}, frame_end(0, 0)]):
            with self.subTest(rows=rows):
                self.assertFalse(self.report(rows)['valid'])

    def test_strict_headers_and_uint64_footer_values(self):
        for field, value in (('schema_version', True), ('schema_version', 2),
                             ('synthetic', 1), ('phase', 'before_game_frame')):
            with self.subTest(field=field):
                self.assertFalse(self.report([{**frame_header(), field: value}, frame(), frame_end()])['valid'])
        for field in ('attempted', 'written', 'dropped'):
            for value in (-1, 1 << 64, True, 1.0, '1'):
                with self.subTest(field=field, value=value):
                    self.assertFalse(self.report([frame_header(), frame(), {**frame_end(), field: value}])['valid'])

    def test_malformed_nonfinite_duplicate_keys_and_utf8_fail(self):
        for content in ('\n', '{', 'null\n', '[]\n',
                        '{"type":"frame_session","type":"frame_end"}\n',
                        '{"type":"frame_session","extra":NaN}\n',
                        '{"type":"frame_session","extra":1e999}\n'):
            self.path.write_text(content, encoding='utf-8')
            with self.subTest(content=content):
                self.assertFalse(validate_frames(self.path)['valid'])
        self.path.write_bytes(b'\xff')
        self.assertFalse(validate_frames(self.path)['valid'])
        self.path.unlink()
        self.assertFalse(validate_frames(self.path)['valid'])

    def test_error_and_marker_details_are_bounded(self):
        report = self.report([frame_header(), *({'type': 'unknown'} for _ in range(150)), frame_end(0, 0)])
        self.assertEqual(len(report['errors']), 100)
        self.assertGreater(report['error_count'], 100)
        self.assertTrue(report['errors_truncated'])
        report = self.report([frame_header(), frame(), *(marker(value) for value in range(1, 151)), frame_end(151, 151)])
        self.assertTrue(report['valid'])
        self.assertEqual(len(report['markers']), 100)
        self.assertTrue(report['markers_truncated'])

    def test_movement_correlation_bounds_use_attempted_callbacks(self):
        movement_report = {'valid': False, 'synthetic': True, 'footer': {'attempted': 3}}
        report = self.report([frame_header(), frame(movement_sequence=2), frame_end()],
                             movement_report=movement_report)
        self.assertEqual(report['movement_correlation']['out_of_bounds'], 0)
        self.assertIn('movement_trace_invalid', [error['code'] for error in report['errors']])
        movement_report['valid'] = True
        report = self.report([frame_header(), frame(movement_sequence=3), frame_end()],
                             movement_report=movement_report)
        self.assertFalse(report['valid'])
        self.assertEqual(report['movement_correlation']['out_of_bounds'], 1)
        report = self.report([frame_header(), frame(movement_sequence=2), frame_end()],
                             movement_report=movement_report)
        self.assertTrue(report['valid'])
        movement_report['synthetic'] = False
        self.assertFalse(self.report([frame_header(), frame(), frame_end()], movement_report=movement_report)['valid'])

    def test_real_movement_file_is_optional_and_missing_file_fails_correlation(self):
        missing = Path(self.temp.name) / 'missing.jsonl'
        report = self.report([frame_header(), frame(), frame_end()], movement_trace=missing)
        self.assertFalse(report['valid'])
        self.assertTrue(report['movement_correlation']['checked'])

    def test_large_finite_clock_spans_do_not_break_json(self):
        report = self.report([frame_header(), frame(clock_time_s=-(10 ** 308)),
                              frame(1, clock_time_s=10 ** 308), frame_end(2, 2)])
        self.assertTrue(report['valid'])
        self.assertIsNone(report['worlds'][0]['callback_clock_span_s'])
        json.dumps(report, allow_nan=False)

    def test_cli_reports_return_codes_and_refuses_report_overwrite(self):
        self.write([frame_header(), frame(), frame_end()])
        destination = Path(self.temp.name) / 'report.json'
        with contextlib.redirect_stdout(io.StringIO()) as output:
            self.assertEqual(main(['validate', str(self.path), '--report', str(destination)]), 0)
        self.assertTrue(json.loads(output.getvalue())['valid'])
        original = destination.read_bytes()
        with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
            self.assertEqual(main(['validate', str(self.path), '--report', str(destination)]), 2)
        self.assertEqual(destination.read_bytes(), original)
        self.write([frame_header(), frame()])
        with contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(main(['validate', str(self.path)]), 1)


if __name__ == '__main__':
    unittest.main()
