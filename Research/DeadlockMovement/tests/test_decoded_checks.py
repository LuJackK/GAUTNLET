from pathlib import Path
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import analyze_decoded_checks as checks


class FrictionModelTests(unittest.TestCase):
    def test_release_from_walk_speed_takes_34_ticks(self):
        speed, ticks = 318.909, 0
        while speed >= 1:
            speed = checks.friction_step(speed, checks.SV_FRICTION)
            ticks += 1
        self.assertEqual(ticks, 34)

    def test_stop_speed_floor_and_zero_threshold(self):
        # Below the 100 u/s stop speed the loss per tick is constant: 100 x 4 / 64.
        self.assertAlmostEqual(checks.friction_step(50.0, 4.0), 50.0 - 6.25)
        self.assertEqual(checks.friction_step(0.05, 4.0), 0.0)
        self.assertEqual(checks.friction_step(3.0, 4.0), 0.0)

    def test_dash_slide_entry_after_one_tick(self):
        self.assertAlmostEqual(checks.friction_step(650.0, 4 * checks.DASH_SLIDE_FRICTION), 642.890625)
        self.assertAlmostEqual(checks.friction_step(450.0, 4 * checks.DASH_SLIDE_FRICTION), 445.078125)

    def test_summary_ignores_missing_values(self):
        self.assertIsNone(checks.summary([None, float('nan')]))
        self.assertEqual(checks.summary([1, 3, 2])['median'], 2)


if __name__ == '__main__':
    unittest.main()
