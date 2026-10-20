import contextlib
import copy
import io
import json
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from capture_trace import main, validate_capture


def session(synthetic=True):
    return {'type': 'session', 'schema_version': 1, 'module': 'server', 'units': 'source',
            'timing_status': 'unverified', 'synthetic': synthetic,
            'modlock_revision': 'test-fixture', 'observer_revision': 'test-fixture'}


def step(sequence=0, world=1, pawn=42, clock_time=None, clock_tick=None):
    state = {'position': [0.0, 0.0, 0.0], 'velocity': [3.0, 4.0, 0.0],
             'ground_entity': None, 'move_type': None}
    return {'type': 'step', 'sequence': sequence, 'world': world, 'pawn_handle': pawn,
            'command_raw': -1, 'ticks': [0, 1], 'fractions': [0.0, 0.5],
            'buttons': ['0', '18446744073709551615', '1'],
            'angles': [0.0, 90.0, 0.0], 'axes': [1.0, 0.0, 0.0],
            'before': copy.deepcopy(state), 'after': copy.deepcopy(state),
            'wall_ns': 123456, 'clock_tick': clock_tick, 'clock_time_s': clock_time}


def end(attempted=1, written=1, dropped=0, worlds=1):
    return {'type': 'end', 'attempted': attempted, 'written': written,
            'dropped': dropped, 'worlds': worlds}


class CaptureTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.path = Path(self.temp.name) / 'synthetic.jsonl'

    def write(self, records):
        self.path.write_text(''.join(json.dumps(record) + '\n' for record in records), encoding='utf-8')
        return self.path

    def report(self, records):
        return validate_capture(self.write(records))

    def test_synthetic_check_only_format_is_prominent_and_complete(self):
        report = self.report([session(), step(), end()])
        self.assertTrue(report['valid'])
        self.assertTrue(report['complete'])
        self.assertTrue(report['synthetic'])
        self.assertEqual(report['evidence_status'], 'synthetic_only_not_real_game_evidence')
        self.assertIn('SYNTHETIC', report['warnings'][0])
        self.assertEqual(report['native_compatibility'], 'unassessed')
        self.assertIsNone(report['worlds'][0]['callback_clock_span_s'])
        self.assertFalse(report['timing']['exact_step_duration_supported'])

    def test_counts_metrics_and_callback_clock_spans(self):
        first = step(clock_time=2.0, clock_tick=10)
        first['after']['position'] = [3, 4, 0]
        second = step(1, pawn=43, clock_time=2.25, clock_tick=11)
        second['after']['velocity'] = [0, 0, 12]
        third = step(2, world=2, pawn=None, clock_time=0.0, clock_tick=0)
        report = self.report([session(False), first, second, third, end(3, 3, worlds=2)])
        self.assertTrue(report['valid'])
        self.assertEqual(report['world_count'], 2)
        self.assertEqual(report['pawn_count'], 2)
        self.assertEqual(report['unknown_pawn_steps'], 1)
        self.assertEqual(report['metrics']['max_step_displacement_source_units'], 5)
        self.assertEqual(report['metrics']['max_observed_speed_source_units_per_s'], 12)
        self.assertEqual(report['worlds'][0]['callback_clock_span_s'], 0.25)
        self.assertEqual(report['worlds'][0]['duration_source'], 'callback engine clock')
        self.assertIsNone(report['worlds'][1]['callback_clock_span_s'])

    def test_world_indices_can_include_worlds_without_recorded_steps(self):
        self.assertTrue(self.report([session(), step(world=4), end(worlds=6)])['valid'])
        self.assertFalse(self.report([session(), step(world=4), end(worlds=3)])['valid'])

    def test_initial_internal_and_trailing_gaps_are_reconciled(self):
        report = self.report([session(), step(2), step(4), end(7, 2, 5)])
        self.assertTrue(report['format_valid'])
        self.assertFalse(report['valid'])
        self.assertFalse(report['complete'])
        self.assertEqual(report['sequence']['missing_samples'], 5)
        self.assertEqual(report['sequence']['gap_count'], 3)
        self.assertEqual([gap['count'] for gap in report['sequence']['gaps']], [2, 1, 2])

    def test_duplicate_and_reversed_sequences_are_invalid(self):
        for sequences in ([0, 0], [1, 0]):
            with self.subTest(sequences=sequences):
                report = self.report([session(), *(step(index) for index in sequences), end(2, 2)])
                self.assertFalse(report['format_valid'])
                self.assertIn('sequence_order', [error['code'] for error in report['errors']])

    def test_footer_counts_and_ranges_are_checked(self):
        for footer in (end(2, 1, 0), end(1, 2, 0), end(0, 0, 0), end(2, 1, 1)):
            records = [session(), step(), footer]
            if footer == end(2, 1, 1):
                records[1]['sequence'] = 2
            with self.subTest(footer=footer):
                self.assertFalse(self.report(records)['valid'])

    def test_clock_regressions_are_per_world_and_fail_validation(self):
        report = self.report([session(), step(0, clock_time=5, clock_tick=5),
                              step(1, world=2, clock_time=0, clock_tick=0),
                              step(2, clock_time=4, clock_tick=4), end(3, 3, worlds=2)])
        self.assertTrue(report['format_valid'])
        self.assertTrue(report['complete'])
        self.assertFalse(report['valid'])
        self.assertEqual(report['timing']['clock_time_regressions'], 1)
        self.assertEqual(report['timing']['clock_tick_regressions'], 1)
        self.assertIsNone(report['worlds'][0]['callback_clock_span_s'])

    def test_null_clocks_do_not_hide_regressions(self):
        report = self.report([session(), step(0, clock_time=5), step(1),
                              step(2, clock_time=4), end(3, 3)])
        self.assertEqual(report['timing']['clock_time_regressions'], 1)

    def test_duplicate_callback_timestamps_are_retained(self):
        report = self.report([session(), step(0, clock_time=5, clock_tick=4),
                              step(1, clock_time=5, clock_tick=4), end(2, 2)])
        self.assertTrue(report['valid'])
        self.assertEqual(report['timing']['duplicate_clock_times'], 1)
        self.assertEqual(report['worlds'][0]['callback_clock_span_s'], 0)

    def test_raw_fractions_outside_unit_interval_are_reported(self):
        record = step()
        record['fractions'] = [-0.25, 1.25]
        report = self.report([session(), record, end()])
        self.assertTrue(report['valid'])
        self.assertEqual(report['fraction_anomaly_steps'], 1)
        self.assertIn('unverified layout', report['warnings'][-1])

    def test_invalid_step_fields(self):
        mutations = [
            ('sequence', -1), ('sequence', True), ('world', 0), ('world', '1'),
            ('pawn_handle', -1), ('pawn_handle', 1 << 32), ('pawn_handle', False),
            ('command_raw', -(1 << 31) - 1), ('command_raw', 1 << 31),
            ('ticks', [0]), ('ticks', [0, 0.5]), ('ticks', [0, 1 << 31]),
            ('fractions', [0, float('nan')]), ('fractions', [0, True]),
            ('angles', [0, 0]), ('angles', [0, 0, float('inf')]),
            ('axes', '0,0,0'), ('axes', [0, 0, '0']),
            ('buttons', [0, '0', '0']), ('buttons', ['-1', '0', '0']),
            ('buttons', ['18446744073709551616', '0', '0']),
            ('buttons', ['1e2', '0', '0']), ('buttons', [' 1', '0', '0']),
            ('buttons', ['\u0661', '0', '0']), ('buttons', ['0', '0']),
            ('wall_ns', -1), ('wall_ns', 1 << 64), ('wall_ns', True),
            ('clock_tick', -1), ('clock_tick', 1 << 64), ('clock_time_s', '0'),
            ('clock_time_s', float('-inf')), ('clock_time_s', False),
        ]
        for name, value in mutations:
            record = step()
            record[name] = value
            with self.subTest(name=name, value=value):
                self.assertFalse(self.report([session(), record, end()])['format_valid'])

    def test_invalid_state_fields_and_missing_fields(self):
        for state in ('before', 'after'):
            for name, value in (('position', [0, 0]), ('velocity', [0, None, 0]),
                                ('ground_entity', 1 << 32), ('ground_entity', -1),
                                ('move_type', 256), ('move_type', False)):
                record = step()
                record[state][name] = value
                with self.subTest(state=state, name=name):
                    self.assertFalse(self.report([session(), record, end()])['valid'])
        for name in step():
            record = step()
            del record[name]
            with self.subTest(missing=name):
                self.assertFalse(self.report([session(), record, end()])['valid'])

    def test_boundary_values_and_nullable_fields(self):
        record = step(pawn=(1 << 32) - 1, clock_tick=(1 << 64) - 1)
        record['command_raw'] = -(1 << 31)
        record['ticks'] = [-(1 << 31), (1 << 31) - 1]
        record['wall_ns'] = (1 << 64) - 1
        record['before']['ground_entity'] = (1 << 32) - 1
        record['after']['move_type'] = 255
        self.assertTrue(self.report([session(), record, end()])['valid'])

    def test_invalid_headers_and_footers(self):
        for name, value in (('schema_version', True), ('schema_version', 2),
                            ('module', 'client'), ('units', 'meters'),
                            ('timing_status', 'verified'), ('synthetic', 1)):
            header = session()
            header[name] = value
            with self.subTest(name=name):
                self.assertFalse(self.report([header, step(), end()])['valid'])
        for name in ('attempted', 'written', 'dropped', 'worlds'):
            for value in (-1, True, 1.0, '1'):
                footer = end()
                footer[name] = value
                with self.subTest(name=name, value=value):
                    self.assertFalse(self.report([session(), step(), footer])['valid'])

    def test_missing_header_footer_empty_and_records_after_footer(self):
        for records in ([], [session()], [session(), end(0, 0)], [step(), end()],
                        [session(), step()], [session(), session(), step(), end()],
                        [session(), step(), end(), step(1)], [session(), step(), end(), end()],
                        [session(), {'type': 'unknown'}, end(0, 0)]):
            with self.subTest(records=records):
                self.assertFalse(self.report(records)['valid'])

    def test_invalid_json_constants_duplicates_and_blank_lines(self):
        for body in ('{', 'null\n', '[]\n', '\n', '{"type":"session","type":"end"}\n',
                     json.dumps(session()) + '\n{"extra":1e999}\n',
                     json.dumps(session()) + '\n{"extra":NaN}\n'):
            with self.subTest(body=body):
                self.path.write_text(body, encoding='utf-8')
                self.assertFalse(validate_capture(self.path)['valid'])

    def test_nonfinite_metadata_is_rejected(self):
        header = session()
        header['extra_metadata'] = {'nested': [float('inf')]}
        self.assertFalse(self.report([header, step(), end()])['valid'])

    def test_extreme_finite_metrics_do_not_produce_nonfinite_report_json(self):
        record = step()
        record['before']['position'] = [1e308, 0, 0]
        record['after']['position'] = [-1e308, 0, 0]
        report = self.report([session(), record, end()])
        self.assertFalse(report['valid'])
        json.dumps(report, allow_nan=False)

    def test_extreme_finite_clock_span_is_unavailable_without_crashing(self):
        report = self.report([session(), step(0, clock_time=-(10 ** 308)),
                              step(1, clock_time=10 ** 308), end(2, 2)])
        self.assertTrue(report['valid'])
        self.assertIsNone(report['worlds'][0]['callback_clock_span_s'])
        json.dumps(report, allow_nan=False)

    def test_details_are_bounded_but_error_totals_are_retained(self):
        report = self.report([session(), *({'type': 'unknown'} for _ in range(150)), end(0, 0)])
        self.assertEqual(len(report['errors']), 100)
        self.assertGreater(report['error_count'], 100)
        self.assertTrue(report['errors_truncated'])

    def test_missing_or_non_utf8_file_returns_invalid_report(self):
        self.assertFalse(validate_capture(self.path)['valid'])
        self.path.write_bytes(b'\xff')
        self.assertFalse(validate_capture(self.path)['valid'])

    def test_cli_exit_codes_reports_and_no_overwrite(self):
        self.write([session(), step(), end()])
        destination = Path(self.temp.name) / 'report.json'
        with contextlib.redirect_stdout(io.StringIO()) as output:
            self.assertEqual(main(['validate', str(self.path), '--report', str(destination)]), 0)
        self.assertTrue(json.loads(output.getvalue())['valid'])
        original = destination.read_bytes()
        with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
            self.assertEqual(main(['validate', str(self.path), '--report', str(destination)]), 2)
        self.assertEqual(destination.read_bytes(), original)
        self.write([session(), step()])
        with contextlib.redirect_stdout(io.StringIO()) as output:
            self.assertEqual(main(['validate', str(self.path)]), 1)
        self.assertFalse(json.loads(output.getvalue())['valid'])
        self.write([session(), step(1), end(2, 1, 1)])
        with contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(main(['validate', str(self.path)]), 1)


if __name__ == '__main__':
    unittest.main()
