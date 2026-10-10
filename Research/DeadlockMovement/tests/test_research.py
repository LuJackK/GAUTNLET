import math
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from research import compare_traces, read_trace


class TraceTests(unittest.TestCase):
    def test_different_sample_rates_interpolate_without_error(self):
        report = compare_traces([(0, 10), (0.25, 9), (0.5, 8), (1, 6)], [(0, 10), (1, 6)])
        self.assertEqual(report['rmse_mps'], 0)

    def test_known_error_metrics(self):
        report = compare_traces([(0, 10), (1, 8)], [(0, 12), (1, 8)])
        self.assertAlmostEqual(report['rmse_mps'], math.sqrt(2))
        self.assertEqual(report['max_abs_error_mps'], 2)
        self.assertEqual(report['mean_error_mps'], 1)

    def test_no_silent_extrapolation(self):
        with self.assertRaises(ValueError):
            compare_traces([(0, 10), (1, 8)], [(0.1, 10), (0.9, 8)])

    def test_invalid_measurements_are_rejected(self):
        for body in ['time_s,speed_mps\n0,10\n0,8\n',
                     'time_s,speed_mps\n0,nan\n1,8\n',
                     'time_s,speed_mps\n0,10\n1,-8\n',
                     'time_s,speed_mps\n0,10\n',
                     'time,speed\n0,10\n1,8\n']:
            with self.subTest(body=body), tempfile.TemporaryDirectory() as temp:
                path = Path(temp) / 'trace.csv'
                path.write_text(body, encoding='utf-8')
                with self.assertRaises(ValueError):
                    read_trace(path)


if __name__ == '__main__':
    unittest.main()
