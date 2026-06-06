using System;
using System.Text;
using UnityEngine;

namespace Fragsurf.ReplayHarness {

    [Serializable]
    public sealed class ReplayComparisonSettings {
        public float positionTolerance = 0.001f;
        public float velocityTolerance = 0.001f;
        public float scalarTolerance = 0.001f;
        public float angleTolerance = 0.05f;
        public int authoritativeDelayFrames = 6;
    }

    public enum ReplayRunKind {
        LocalReference,
        LocalRepeat,
        AuthoritativeReference,
        PredictedWithCorrections
    }

    [Serializable]
    public sealed class ReplayRunResult {
        public ReplayRunKind runKind;
        public string traceId;
        public readonly System.Collections.Generic.List<ReplayTickRecord> records = new System.Collections.Generic.List<ReplayTickRecord>();
        public int rollbackCount;
        public int correctionCount;
        public int predictedFillCount;
        public int checksumMismatchCount;
        public int firstChecksumMismatchFrame = -1;
        public int lastChecksumMismatchFrame = -1;
        public int predictionCapViolations;
    }

    [Serializable]
    public sealed class ReplayComparisonReport {
        public bool passed;
        public int firstMismatchFrame = -1;
        public string summary = string.Empty;
        public ReplayTickRecord expected;
        public ReplayTickRecord actual;

        public static ReplayComparisonReport Compare(ReplayRunResult expectedRun,
                                                     ReplayRunResult actualRun,
                                                     ReplayComparisonSettings settings,
                                                     string comparisonLabel) {
            ReplayComparisonReport report = new ReplayComparisonReport {
                passed = true
            };

            if (expectedRun == null || actualRun == null) {
                report.passed = false;
                report.summary = $"{comparisonLabel}: missing run data.";
                return report;
            }

            if (expectedRun.records.Count != actualRun.records.Count) {
                report.passed = false;
                report.summary = $"{comparisonLabel}: record count mismatch. expected={expectedRun.records.Count}, actual={actualRun.records.Count}.";
                return report;
            }

            for (int i = 0; i < expectedRun.records.Count; i++) {
                ReplayTickRecord expectedRecord = expectedRun.records[i];
                ReplayTickRecord actualRecord = actualRun.records[i];

                if (!TryCompareSamples(expectedRecord.sample, actualRecord.sample, settings, out string diff)) {
                    report.passed = false;
                    report.firstMismatchFrame = expectedRecord.sample.frame;
                    report.expected = expectedRecord;
                    report.actual = actualRecord;
                    report.summary = BuildSummary(comparisonLabel, expectedRecord, actualRecord, diff);
                    return report;
                }
            }

            report.summary = $"{comparisonLabel}: matched across {expectedRun.records.Count} frames.";
            return report;
        }

        private static bool TryCompareSamples(ReplayStateSample expected,
                                              ReplayStateSample actual,
                                              ReplayComparisonSettings settings,
                                              out string diff) {
            StringBuilder sb = new StringBuilder(256);

            if (expected.frame != actual.frame)
                AppendDiff(sb, $"frame expected={expected.frame} actual={actual.frame}");

            if (Vector3.Distance(expected.position, actual.position) > settings.positionTolerance)
                AppendDiff(sb, $"position expected={Format(expected.position)} actual={Format(actual.position)}");

            if (Vector3.Distance(expected.velocity, actual.velocity) > settings.velocityTolerance)
                AppendDiff(sb, $"velocity expected={Format(expected.velocity)} actual={Format(actual.velocity)}");

            if (Mathf.Abs(Mathf.DeltaAngle(expected.yaw, actual.yaw)) > settings.angleTolerance)
                AppendDiff(sb, $"yaw expected={expected.yaw:F3} actual={actual.yaw:F3}");

            if (Mathf.Abs(Mathf.DeltaAngle(expected.pitch, actual.pitch)) > settings.angleTolerance)
                AppendDiff(sb, $"pitch expected={expected.pitch:F3} actual={actual.pitch:F3}");

            CompareBool(expected.grounded, actual.grounded, "grounded", sb);
            CompareBool(expected.underwater, actual.underwater, "underwater", sb);
            CompareBool(expected.cameraUnderwater, actual.cameraUnderwater, "cameraUnderwater", sb);
            CompareBool(expected.climbingLadder, actual.climbingLadder, "climbingLadder", sb);
            CompareBool(expected.isDashing, actual.isDashing, "isDashing", sb);
            CompareBool(expected.canAirDash, actual.canAirDash, "canAirDash", sb);
            CompareBool(expected.sliding, actual.sliding, "sliding", sb);
            CompareBool(expected.wasSliding, actual.wasSliding, "wasSliding", sb);
            CompareBool(expected.crouching, actual.crouching, "crouching", sb);
            CompareBool(expected.jumping, actual.jumping, "jumping", sb);
            CompareBool(expected.uncrouchDown, actual.uncrouchDown, "uncrouchDown", sb);
            CompareBool(expected.hasHitTarget, actual.hasHitTarget, "hasHitTarget", sb);
            CompareBool(expected.meleeHitResolved, actual.meleeHitResolved, "meleeHitResolved", sb);
            CompareBool(expected.isParrying, actual.isParrying, "isParrying", sb);
            CompareBool(expected.groundedTemp, actual.groundedTemp, "groundedTemp", sb);

            CompareInt(expected.jumpCount, actual.jumpCount, "jumpCount", sb);
            CompareInt((int)expected.moveType, (int)actual.moveType, "moveType", sb);
            CompareInt((int)expected.meleeState, (int)actual.meleeState, "meleeState", sb);
            CompareInt(expected.lastConsumedJumpPressFrame, actual.lastConsumedJumpPressFrame, "lastConsumedJumpPressFrame", sb);
            CompareInt(expected.lastConsumedDashPressFrame, actual.lastConsumedDashPressFrame, "lastConsumedDashPressFrame", sb);
            CompareInt(expected.lastConsumedParryPressFrame, actual.lastConsumedParryPressFrame, "lastConsumedParryPressFrame", sb);
            CompareInt(expected.meleeHitTargetObjectId, actual.meleeHitTargetObjectId, "meleeHitTargetObjectId", sb);
            CompareInt(expected.meleeHitResolveTick, actual.meleeHitResolveTick, "meleeHitResolveTick", sb);

            CompareFloat(expected.jumpTimer, actual.jumpTimer, settings.scalarTolerance, "jumpTimer", sb);
            CompareFloat(expected.stamina, actual.stamina, settings.scalarTolerance, "stamina", sb);
            CompareFloat(expected.staminaRegenTimer, actual.staminaRegenTimer, settings.scalarTolerance, "staminaRegenTimer", sb);
            CompareFloat(expected.dashTimer, actual.dashTimer, settings.scalarTolerance, "dashTimer", sb);
            CompareFloat(expected.currentDashDuration, actual.currentDashDuration, settings.scalarTolerance, "currentDashDuration", sb);
            CompareFloat(expected.dashCooldownTimer, actual.dashCooldownTimer, settings.scalarTolerance, "dashCooldownTimer", sb);
            CompareFloat(expected.meleeTimer, actual.meleeTimer, settings.scalarTolerance, "meleeTimer", sb);
            CompareFloat(expected.meleeCooldownTimer, actual.meleeCooldownTimer, settings.scalarTolerance, "meleeCooldownTimer", sb);
            CompareFloat(expected.parryTimer, actual.parryTimer, settings.scalarTolerance, "parryTimer", sb);
            CompareFloat(expected.crouchLerp, actual.crouchLerp, settings.scalarTolerance, "crouchLerp", sb);
            CompareFloat(expected.renderCrouchLerp, actual.renderCrouchLerp, settings.scalarTolerance, "renderCrouchLerp", sb);
            CompareFloat(expected.slideSpeedCurrent, actual.slideSpeedCurrent, settings.scalarTolerance, "slideSpeedCurrent", sb);
            CompareFloat(expected.slideDelay, actual.slideDelay, settings.scalarTolerance, "slideDelay", sb);
            CompareFloat(expected.forwardMove, actual.forwardMove, settings.scalarTolerance, "forwardMove", sb);
            CompareFloat(expected.sideMove, actual.sideMove, settings.scalarTolerance, "sideMove", sb);
            CompareFloat(expected.verticalAxis, actual.verticalAxis, settings.scalarTolerance, "verticalAxis", sb);
            CompareFloat(expected.horizontalAxis, actual.horizontalAxis, settings.scalarTolerance, "horizontalAxis", sb);
            CompareFloat(expected.surfaceFriction, actual.surfaceFriction, settings.scalarTolerance, "surfaceFriction", sb);
            CompareFloat(expected.gravityFactor, actual.gravityFactor, settings.scalarTolerance, "gravityFactor", sb);
            CompareFloat(expected.walkFactor, actual.walkFactor, settings.scalarTolerance, "walkFactor", sb);
            CompareFloat(expected.fallingVelocity, actual.fallingVelocity, settings.scalarTolerance, "fallingVelocity", sb);

            if (Vector3.Distance(expected.slideDirection, actual.slideDirection) > settings.positionTolerance)
                AppendDiff(sb, $"slideDirection expected={Format(expected.slideDirection)} actual={Format(actual.slideDirection)}");

            if (sb.Length == 0) {
                diff = string.Empty;
                return true;
            }

            AppendDiff(sb, $"debugFingerprint expected={expected.debugFingerprint} actual={actual.debugFingerprint}");
            diff = sb.ToString().TrimEnd();
            return false;
        }

        private static string BuildSummary(string comparisonLabel,
                                           ReplayTickRecord expectedRecord,
                                           ReplayTickRecord actualRecord,
                                           string diff) {
            StringBuilder sb = new StringBuilder(512);
            sb.AppendLine($"{comparisonLabel}: mismatch at frame {expectedRecord.sample.frame}.");
            sb.AppendLine($"input={expectedRecord.input}");
            sb.AppendLine(diff);

            if (!string.IsNullOrWhiteSpace(expectedRecord.querySnapshot)) {
                sb.AppendLine("expected queries:");
                sb.AppendLine(expectedRecord.querySnapshot);
            }

            if (!string.IsNullOrWhiteSpace(actualRecord.querySnapshot)) {
                sb.AppendLine("actual queries:");
                sb.AppendLine(actualRecord.querySnapshot);
            }

            return sb.ToString().TrimEnd();
        }

        private static void CompareFloat(float expected, float actual, float tolerance, string label, StringBuilder sb) {
            if (Mathf.Abs(expected - actual) > tolerance)
                AppendDiff(sb, $"{label} expected={expected:F4} actual={actual:F4}");
        }

        private static void CompareInt(int expected, int actual, string label, StringBuilder sb) {
            if (expected != actual)
                AppendDiff(sb, $"{label} expected={expected} actual={actual}");
        }

        private static void CompareBool(bool expected, bool actual, string label, StringBuilder sb) {
            if (expected != actual)
                AppendDiff(sb, $"{label} expected={expected} actual={actual}");
        }

        private static void AppendDiff(StringBuilder sb, string line) {
            sb.AppendLine(line);
        }

        private static string Format(Vector3 value) {
            return $"({value.x:F4}, {value.y:F4}, {value.z:F4})";
        }
    }
}
