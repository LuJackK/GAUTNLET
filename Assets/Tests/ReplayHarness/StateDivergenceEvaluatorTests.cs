using Fragsurf.Movement;
using NUnit.Framework;
using UnityEngine;

namespace Fragsurf.ReplayHarness {

    public class StateDivergenceEvaluatorTests {

        [Test]
        public void Evaluate_ForceCorrects_OnGameplayCriticalMismatch() {
            StateDivergenceEvaluator evaluator = new StateDivergenceEvaluator(CorrectionGateConfig.CreateConservativeDefaults());
            MoveData predicted = CreateBaseline();
            MoveData authoritative = CreateBaseline();
            authoritative.isDashing = !predicted.isDashing;

            StateDivergenceReport report = evaluator.Evaluate(predicted, authoritative, 0);

            Assert.AreEqual(CorrectionDecision.ForceCorrect, report.Decision);
            StringAssert.Contains("isDashing", report.PrimaryReason);
        }

        [Test]
        public void Evaluate_HardCorrects_OnPositionThresholdExceeded() {
            CorrectionGateConfig config = CorrectionGateConfig.CreateConservativeDefaults();
            StateDivergenceEvaluator evaluator = new StateDivergenceEvaluator(config);
            MoveData predicted = CreateBaseline();
            MoveData authoritative = CreateBaseline();
            authoritative.origin += new Vector3(config.PositionHardThreshold + 0.1f, 0f, 0f);

            StateDivergenceReport report = evaluator.Evaluate(predicted, authoritative, 0);

            Assert.AreEqual(CorrectionDecision.HardCorrect, report.Decision);
            StringAssert.Contains("position", report.PrimaryReason);
        }

        [Test]
        public void Evaluate_EscalatesPersistentMediumDrift_ToHardCorrect() {
            StateDivergenceEvaluator evaluator = new StateDivergenceEvaluator(CorrectionGateConfig.CreateConservativeDefaults());
            MoveData predicted = CreateBaseline();
            MoveData authoritative = CreateBaseline();
            authoritative.stamina += 0.06f;
            authoritative.dashTimer += 0.06f;

            StateDivergenceReport first = evaluator.Evaluate(predicted, authoritative, 0);
            StateDivergenceReport third = evaluator.Evaluate(predicted, authoritative, 2);

            Assert.AreEqual(CorrectionDecision.ObserveOnly, first.Decision);
            Assert.AreEqual(1, first.ConsecutiveObserveFrames);
            Assert.AreEqual(CorrectionDecision.HardCorrect, third.Decision);
            StringAssert.Contains("persistent medium drift", third.PrimaryReason);
        }

        [Test]
        public void Evaluate_IgnoresDiagnosticsOnlyInputEchoDrift() {
            StateDivergenceEvaluator evaluator = new StateDivergenceEvaluator(CorrectionGateConfig.CreateConservativeDefaults());
            MoveData predicted = CreateBaseline();
            MoveData authoritative = CreateBaseline();
            authoritative.forwardMove += 1f;
            authoritative.sideMove -= 1f;
            authoritative.verticalAxis += 0.8f;
            authoritative.horizontalAxis -= 0.8f;

            StateDivergenceReport report = evaluator.Evaluate(predicted, authoritative, 0);

            Assert.AreEqual(CorrectionDecision.Ignore, report.Decision);
            Assert.Less(report.WeightedScore, CorrectionGateConfig.CreateConservativeDefaults().ObserveWeightedScoreThreshold);
        }

        [Test]
        public void Evaluate_RelaxesVerticalDrift_ForForeignMeleeCharge() {
            StateDivergenceEvaluator evaluator = new StateDivergenceEvaluator(CorrectionGateConfig.CreateConservativeDefaults());
            MoveData predicted = CreateCharging();
            MoveData authoritative = CreateCharging();
            authoritative.origin += new Vector3(0f, 0.9f, 0f);
            authoritative.velocity += new Vector3(0f, 6f, 0f);

            StateDivergenceReport strictReport = evaluator.Evaluate(predicted, authoritative, 0);
            StateDivergenceReport relaxedReport = evaluator.Evaluate(predicted, authoritative, 0, true);

            Assert.AreEqual(CorrectionDecision.HardCorrect, strictReport.Decision);
            Assert.AreEqual(CorrectionDecision.Ignore, relaxedReport.Decision);
        }

        [Test]
        public void Evaluate_KeepsHorizontalHardCorrect_ForForeignMeleeCharge() {
            StateDivergenceEvaluator evaluator = new StateDivergenceEvaluator(CorrectionGateConfig.CreateConservativeDefaults());
            MoveData predicted = CreateCharging();
            MoveData authoritative = CreateCharging();
            authoritative.origin += new Vector3(0.5f, 0f, 0f);

            StateDivergenceReport report = evaluator.Evaluate(predicted, authoritative, 0, true);

            Assert.AreEqual(CorrectionDecision.HardCorrect, report.Decision);
            StringAssert.Contains("horizontal position", report.PrimaryReason);
        }

        private static MoveData CreateBaseline() {
            return new MoveData {
                frame = 12,
                moveType = MoveType.Walk,
                meleeState = MoveData.MeleeState.None,
                origin = new Vector3(1f, 2f, 3f),
                velocity = new Vector3(4f, 0f, -2f),
                viewAngles = new Vector3(5f, 90f, 0f),
                stamina = 2f,
                staminaRegenTimer = 0.1f,
                dashTimer = 0.1f,
                currentDashDuration = 0.1f,
                dashCooldownTimer = 0.1f,
                isDashing = false,
                canAirDash = true,
                jumpCount = 1,
                jumpTimer = 0.1f,
                grounded = true,
                jumping = false,
                sliding = false,
                wasSliding = false,
                fallingVelocity = 0f,
                slideDirection = Vector3.forward,
                slideSpeedCurrent = 0f,
                surfaceFriction = 1f,
                gravityFactor = 1f,
                walkFactor = 1f,
                crouching = false,
                crouchLerp = 0f,
                renderCrouchLerp = 0f,
                uncrouchDown = false,
                slideDelay = 0f,
                hasHitTarget = false,
                meleeHitResolved = false,
                meleeHitTargetObjectId = 0,
                meleeHitResolveTick = -1,
                lastConsumedJumpPressFrame = 8,
                lastConsumedDashPressFrame = 9,
                forwardMove = 0f,
                sideMove = 0f,
                verticalAxis = 0f,
                horizontalAxis = 0f
            };
        }

        private static MoveData CreateCharging() {
            MoveData state = CreateBaseline();
            state.moveType = MoveType.HeavyMelee;
            state.meleeState = MoveData.MeleeState.Charging;
            state.grounded = false;
            state.velocity = new Vector3(1f, 0f, 1f);
            state.meleeTimer = 0.3f;
            return state;
        }
    }
}
