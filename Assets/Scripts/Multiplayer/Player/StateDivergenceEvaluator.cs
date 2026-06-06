using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Fragsurf.Movement {

    internal sealed class StateDivergenceEvaluator {
        private readonly CorrectionGateConfig _config;

        public StateDivergenceEvaluator(CorrectionGateConfig config) {
            _config = config ?? CorrectionGateConfig.CreateConservativeDefaults();
        }

        public StateDivergenceReport Evaluate(MoveData predicted, MoveData authoritative, int consecutiveObserveCount) {
            return Evaluate(predicted, authoritative, consecutiveObserveCount, false);
        }

        public StateDivergenceReport Evaluate(MoveData predicted,
                                              MoveData authoritative,
                                              int consecutiveObserveCount,
                                              bool relaxForeignMeleeChargeKinematics) {
            StateDivergenceReport report = new StateDivergenceReport {
                HasPredictedState = predicted != null && authoritative != null,
                Decision = CorrectionDecision.Ignore,
                ConsecutiveObserveFrames = 0
            };

            if (predicted == null || authoritative == null) {
                report.Decision = CorrectionDecision.HardCorrect;
                report.PrimaryReason = "Missing predicted state";
                report.Summary = "Predicted state was unavailable for the authoritative frame.";
                return report;
            }

            if (predicted.frame != authoritative.frame) {
                Force(report, $"frame mismatch predicted={predicted.frame} authoritative={authoritative.frame}");
                return FinalizeReport(report);
            }

            CompareForceField(report, predicted.moveType != authoritative.moveType, "moveType", predicted.moveType, authoritative.moveType);
            CompareForceField(report, predicted.meleeState != authoritative.meleeState, "meleeState", predicted.meleeState, authoritative.meleeState);
            CompareForceField(report, predicted.isDashing != authoritative.isDashing, "isDashing", predicted.isDashing, authoritative.isDashing);
            CompareForceField(report, predicted.canAirDash != authoritative.canAirDash, "canAirDash", predicted.canAirDash, authoritative.canAirDash);
            CompareForceField(report, predicted.jumpCount != authoritative.jumpCount, "jumpCount", predicted.jumpCount, authoritative.jumpCount);

            // Skip grounded force-correct during active melee (Charging/Lunging) to prevent aggressive floor-snap
            bool shouldCheckGrounded = !IsActiveMelee(predicted) && !IsActiveMelee(authoritative);
            CompareForceField(report, shouldCheckGrounded && predicted.grounded != authoritative.grounded, "grounded", predicted.grounded, authoritative.grounded);
            CompareForceField(report, predicted.jumping != authoritative.jumping, "jumping", predicted.jumping, authoritative.jumping);
            CompareForceField(report, predicted.sliding != authoritative.sliding, "sliding", predicted.sliding, authoritative.sliding);
            CompareForceField(report, predicted.wasSliding != authoritative.wasSliding, "wasSliding", predicted.wasSliding, authoritative.wasSliding);
            CompareForceField(report, predicted.hasHitTarget != authoritative.hasHitTarget, "hasHitTarget", predicted.hasHitTarget, authoritative.hasHitTarget);
            CompareForceField(report, predicted.meleeHitResolved != authoritative.meleeHitResolved, "meleeHitResolved", predicted.meleeHitResolved, authoritative.meleeHitResolved);
            CompareForceField(report, predicted.meleeHitTargetObjectId != authoritative.meleeHitTargetObjectId, "meleeHitTargetObjectId", predicted.meleeHitTargetObjectId, authoritative.meleeHitTargetObjectId);
            CompareForceField(report, predicted.meleeHitResolveTick != authoritative.meleeHitResolveTick, "meleeHitResolveTick", predicted.meleeHitResolveTick, authoritative.meleeHitResolveTick);
            CompareForceField(report, predicted.isParrying != authoritative.isParrying, "isParrying", predicted.isParrying, authoritative.isParrying);
            CompareForceField(report, predicted.lastConsumedJumpPressFrame != authoritative.lastConsumedJumpPressFrame, "lastConsumedJumpPressFrame", predicted.lastConsumedJumpPressFrame, authoritative.lastConsumedJumpPressFrame);
            CompareForceField(report, predicted.lastConsumedDashPressFrame != authoritative.lastConsumedDashPressFrame, "lastConsumedDashPressFrame", predicted.lastConsumedDashPressFrame, authoritative.lastConsumedDashPressFrame);
            CompareForceField(report, predicted.lastConsumedParryPressFrame != authoritative.lastConsumedParryPressFrame, "lastConsumedParryPressFrame", predicted.lastConsumedParryPressFrame, authoritative.lastConsumedParryPressFrame);

            if (report.HasFatalMismatch)
                return FinalizeReport(report);

            bool relaxMeleeCharge = relaxForeignMeleeChargeKinematics && IsMeleeCharge(predicted) && IsMeleeCharge(authoritative);
            if (relaxMeleeCharge) {
                CompareHardDistance(report,
                                    HorizontalDistance(predicted.origin, authoritative.origin),
                                    _config.ForeignMeleeChargeHorizontalPositionHardThreshold,
                                    "horizontal position",
                                    "m");
                CompareHardDistance(report,
                                    Mathf.Abs(predicted.origin.y - authoritative.origin.y),
                                    _config.ForeignMeleeChargeVerticalPositionHardThreshold,
                                    "vertical position",
                                    "m");
                CompareHardDistance(report,
                                    HorizontalDistance(predicted.velocity, authoritative.velocity),
                                    _config.ForeignMeleeChargeHorizontalVelocityHardThreshold,
                                    "horizontal velocity",
                                    "m/s");
                CompareHardDistance(report,
                                    Mathf.Abs(predicted.velocity.y - authoritative.velocity.y),
                                    _config.ForeignMeleeChargeVerticalVelocityHardThreshold,
                                    "vertical velocity",
                                    "m/s");
            } else {
                CompareHardDistance(report, Vector3.Distance(predicted.origin, authoritative.origin), _config.PositionHardThreshold, "position", "m");

                // Relax velocity hard-threshold during melee charge to allow for velocity retention timing differences.
                float velocityThreshold = IsActiveMelee(predicted) || IsActiveMelee(authoritative)
                    ? _config.VelocityHardThreshold * 2f
                    : _config.VelocityHardThreshold;
                CompareHardDistance(report, Vector3.Distance(predicted.velocity, authoritative.velocity), velocityThreshold, "velocity", "m/s");
            }

            CompareHardDistance(report, Mathf.Abs(Mathf.DeltaAngle(predicted.viewAngles.y, authoritative.viewAngles.y)), _config.YawHardThreshold, "yaw", "deg");
            CompareHardDistance(report, Mathf.Abs(Mathf.DeltaAngle(predicted.viewAngles.x, authoritative.viewAngles.x)), _config.PitchHardThreshold, "pitch", "deg");
            float fallingVelocityThreshold = relaxMeleeCharge
                ? _config.ForeignMeleeChargeFallingVelocityHardThreshold
                : _config.FallingVelocityHardThreshold;
            CompareHardDistance(report, Mathf.Abs(predicted.fallingVelocity - authoritative.fallingVelocity), fallingVelocityThreshold, "fallingVelocity", string.Empty);
            CompareHardDistance(report, Vector3.Angle(NormalizeOrZero(predicted.slideDirection), NormalizeOrZero(authoritative.slideDirection)), _config.SlideDirectionHardThreshold, "slideDirection", "deg");
            CompareHardDistance(report, Mathf.Abs(predicted.slideSpeedCurrent - authoritative.slideSpeedCurrent), _config.SlideSpeedHardThreshold, "slideSpeedCurrent", string.Empty);

            if (report.HasKinematicMismatch)
                return FinalizeReport(report);

            AddWeightedFloat(report, Mathf.Abs(predicted.stamina - authoritative.stamina), _config.ScalarTolerance, _config.StaminaWeight, "stamina");
            AddWeightedFloat(report, Mathf.Abs(predicted.staminaRegenTimer - authoritative.staminaRegenTimer), _config.TimerTolerance, _config.TimerWeight, "staminaRegenTimer");
            AddWeightedFloat(report, Mathf.Abs(predicted.dashTimer - authoritative.dashTimer), _config.TimerTolerance, _config.TimerWeight, "dashTimer");
            AddWeightedFloat(report, Mathf.Abs(predicted.currentDashDuration - authoritative.currentDashDuration), _config.TimerTolerance, _config.TimerWeight, "currentDashDuration");
            AddWeightedFloat(report, Mathf.Abs(predicted.dashCooldownTimer - authoritative.dashCooldownTimer), _config.TimerTolerance, _config.TimerWeight, "dashCooldownTimer");
            AddWeightedFloat(report, Mathf.Abs(predicted.jumpTimer - authoritative.jumpTimer), _config.TimerTolerance, _config.TimerWeight, "jumpTimer");
            AddWeightedFloat(report, Mathf.Abs(predicted.surfaceFriction - authoritative.surfaceFriction), _config.FactorTolerance, _config.FactorWeight, "surfaceFriction");
            AddWeightedFloat(report, Mathf.Abs(predicted.gravityFactor - authoritative.gravityFactor), _config.FactorTolerance, _config.FactorWeight, "gravityFactor");
            AddWeightedFloat(report, Mathf.Abs(predicted.walkFactor - authoritative.walkFactor), _config.FactorTolerance, _config.FactorWeight, "walkFactor");
            AddWeightedBool(report, predicted.crouching != authoritative.crouching, _config.CrouchStateWeight, "crouching", predicted.crouching, authoritative.crouching);
            AddWeightedFloat(report, Mathf.Abs(predicted.crouchLerp - authoritative.crouchLerp), _config.LerpTolerance, _config.CrouchLerpWeight, "crouchLerp");
            AddWeightedFloat(report, Mathf.Abs(predicted.renderCrouchLerp - authoritative.renderCrouchLerp), _config.LerpTolerance, _config.CrouchLerpWeight, "renderCrouchLerp");
            AddWeightedBool(report, predicted.uncrouchDown != authoritative.uncrouchDown, _config.CrouchStateWeight, "uncrouchDown", predicted.uncrouchDown, authoritative.uncrouchDown);
            AddWeightedFloat(report, Mathf.Abs(predicted.slideDelay - authoritative.slideDelay), _config.TimerTolerance, _config.TimerWeight, "slideDelay");
            float meleeTimerTolerance = relaxMeleeCharge
                ? _config.ForeignMeleeChargeTimerTolerance
                : _config.TimerTolerance;
            AddWeightedFloat(report, Mathf.Abs(predicted.meleeTimer - authoritative.meleeTimer), meleeTimerTolerance, _config.TimerWeight, "meleeTimer");
            AddWeightedFloat(report, Mathf.Abs(predicted.meleeCooldownTimer - authoritative.meleeCooldownTimer), _config.TimerTolerance, _config.TimerWeight, "meleeCooldownTimer");
            AddWeightedFloat(report, Mathf.Abs(predicted.parryTimer - authoritative.parryTimer), _config.TimerTolerance, _config.TimerWeight, "parryTimer");

            AddDiagnosticFloat(report, Mathf.Abs(predicted.forwardMove - authoritative.forwardMove), _config.InputEchoTolerance, _config.InputEchoWeight, "forwardMove");
            AddDiagnosticFloat(report, Mathf.Abs(predicted.sideMove - authoritative.sideMove), _config.InputEchoTolerance, _config.InputEchoWeight, "sideMove");
            AddDiagnosticFloat(report, Mathf.Abs(predicted.verticalAxis - authoritative.verticalAxis), _config.InputEchoTolerance, _config.InputEchoWeight, "verticalAxis");
            AddDiagnosticFloat(report, Mathf.Abs(predicted.horizontalAxis - authoritative.horizontalAxis), _config.InputEchoTolerance, _config.InputEchoWeight, "horizontalAxis");

            if (report.WeightedScore >= _config.HardWeightedScoreThreshold) {
                report.Decision = CorrectionDecision.HardCorrect;
                report.PrimaryReason = string.IsNullOrWhiteSpace(report.PrimaryReason)
                    ? $"weighted score {report.WeightedScore:F2} exceeded hard threshold"
                    : report.PrimaryReason;
            } else if (report.WeightedScore >= _config.ObserveWeightedScoreThreshold) {
                report.ConsecutiveObserveFrames = consecutiveObserveCount + 1;
                int observeFramesBeforeHardCorrect = relaxMeleeCharge
                    ? _config.ForeignMeleeChargeConsecutiveObserveFramesBeforeHardCorrect
                    : _config.ConsecutiveObserveFramesBeforeHardCorrect;
                if (report.ConsecutiveObserveFrames >= observeFramesBeforeHardCorrect) {
                    report.Decision = CorrectionDecision.HardCorrect;
                    report.PrimaryReason = $"persistent medium drift for {report.ConsecutiveObserveFrames} frames";
                    report.TopMismatches.Insert(0, report.PrimaryReason);
                } else {
                    report.Decision = CorrectionDecision.ObserveOnly;
                    if (string.IsNullOrWhiteSpace(report.PrimaryReason))
                        report.PrimaryReason = $"weighted score {report.WeightedScore:F2} reached observe threshold";
                }
            }

            return FinalizeReport(report);
        }

        private static Vector3 NormalizeOrZero(Vector3 value) {
            return value.sqrMagnitude > 0.0001f ? value.normalized : Vector3.zero;
        }

        private static float HorizontalDistance(Vector3 predicted, Vector3 authoritative) {
            float deltaX = predicted.x - authoritative.x;
            float deltaZ = predicted.z - authoritative.z;
            return Mathf.Sqrt(deltaX * deltaX + deltaZ * deltaZ);
        }

        private void CompareHardDistance(StateDivergenceReport report, float delta, float threshold, string field, string suffix) {
            if (delta <= threshold)
                return;

            report.Decision = CorrectionDecision.HardCorrect;
            report.HasKinematicMismatch = true;
            string reason = string.IsNullOrEmpty(suffix)
                ? $"{field} drift {delta:F3} exceeded {threshold:F3}"
                : $"{field} drift {delta:F3}{suffix} exceeded {threshold:F3}{suffix}";
            if (string.IsNullOrWhiteSpace(report.PrimaryReason))
                report.PrimaryReason = reason;
            report.TopMismatches.Add(reason);
        }

        private void AddWeightedFloat(StateDivergenceReport report, float delta, float tolerance, float weight, string field) {
            if (delta <= tolerance)
                return;

            float severity = Mathf.Max(1f, delta / Mathf.Max(0.0001f, tolerance));
            float contribution = weight * severity;
            report.WeightedScore += contribution;
            if (string.IsNullOrWhiteSpace(report.PrimaryReason))
                report.PrimaryReason = $"{field} drift {delta:F3} contributed {contribution:F2}";
            report.TopMismatches.Add($"{field} drift {delta:F3} (+{contribution:F2})");
        }

        private void AddDiagnosticFloat(StateDivergenceReport report, float delta, float tolerance, float weight, string field) {
            if (delta <= tolerance)
                return;

            if (weight > 0f)
                report.WeightedScore += weight;
            report.TopMismatches.Add($"{field} echo drift {delta:F3}");
            if (string.IsNullOrWhiteSpace(report.PrimaryReason))
                report.PrimaryReason = $"{field} input echo drift {delta:F3}";
        }

        private void AddWeightedBool(StateDivergenceReport report, bool mismatch, float weight, string field, object predicted, object authoritative) {
            if (!mismatch)
                return;

            report.WeightedScore += weight;
            string reason = $"{field} mismatch predicted={predicted} authoritative={authoritative} (+{weight:F2})";
            if (string.IsNullOrWhiteSpace(report.PrimaryReason))
                report.PrimaryReason = reason;
            report.TopMismatches.Add(reason);
        }

        private void CompareForceField(StateDivergenceReport report, bool mismatch, string field, object predicted, object authoritative) {
            if (!mismatch)
                return;

            Force(report, $"{field} mismatch predicted={predicted} authoritative={authoritative}");
        }

        private void Force(StateDivergenceReport report, string reason) {
            report.Decision = CorrectionDecision.ForceCorrect;
            report.HasFatalMismatch = true;
            if (string.IsNullOrWhiteSpace(report.PrimaryReason))
                report.PrimaryReason = reason;
            report.TopMismatches.Add(reason);
        }

        private static StateDivergenceReport FinalizeReport(StateDivergenceReport report) {
            if (string.IsNullOrWhiteSpace(report.PrimaryReason))
                report.PrimaryReason = "State matched within correction thresholds";

            StringBuilder sb = new StringBuilder(256);
            sb.Append(report.Decision);
            sb.Append(" | reason=").Append(report.PrimaryReason);
            if (report.WeightedScore > 0f)
                sb.Append(" | score=").Append(report.WeightedScore.ToString("F2"));
            if (report.ConsecutiveObserveFrames > 0)
                sb.Append(" | streak=").Append(report.ConsecutiveObserveFrames);

            int mismatchCount = Mathf.Min(report.TopMismatches.Count, 3);
            if (mismatchCount > 0) {
                sb.Append(" | top=");
                for (int i = 0; i < mismatchCount; i++) {
                    if (i > 0)
                        sb.Append("; ");
                    sb.Append(report.TopMismatches[i]);
                }
            }

            report.Summary = sb.ToString();
            return report;
        }

        private static bool IsActiveMelee(MoveData state) {
            if (state == null)
                return false;
            return state.moveType == MoveType.HeavyMelee &&
                   (state.meleeState == MoveData.MeleeState.Charging || state.meleeState == MoveData.MeleeState.Lunging);
        }

        private static bool IsMeleeCharge(MoveData state) {
            return state != null &&
                   state.moveType == MoveType.HeavyMelee &&
                   state.meleeState == MoveData.MeleeState.Charging;
        }
    }
}
