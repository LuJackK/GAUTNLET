import unittest

from analyze_human_pilot import errors, quadratic, regression


class NumericalFitTests(unittest.TestCase):
    def test_known_ballistic_curve(self):
        points = [(i / 64, 4.5 + 281 * (i / 64) - 400 * (i / 64) ** 2) for i in range(46)]
        for actual, expected in zip(quadratic(points), [4.5, 281, -400]):
            self.assertAlmostEqual(actual, expected, places=8)

    def test_known_velocity_slope(self):
        intercept, slope = regression([(i / 64, 287.5 - 800 * i / 64) for i in range(46)])
        self.assertAlmostEqual(intercept, 287.5)
        self.assertAlmostEqual(slope, -800)

    def test_underdetermined_fit_rejected(self):
        with self.assertRaises(ValueError):
            quadratic([(0, 1), (0, 2), (0, 3)])

    def test_error_units(self):
        result = errors([-3, 3])
        self.assertEqual(result, {'rmse': 3, 'max_abs': 3})
