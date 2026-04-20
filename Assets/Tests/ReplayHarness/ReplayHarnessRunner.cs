using System;
using Fragsurf.Movement;
using UnityEngine;

namespace Fragsurf.ReplayHarness {

    public static class ReplayHarnessRunner {

        public static ReplayRunResult RunLocalTrace(ReplayHarnessEnvironment environment,
                                                    ReplayTraceDefinition trace,
                                                    ReplayRunKind runKind) {
            environment.PrepareForRun();
            ReplayRunResult result = CreateRunResult(trace, runKind);

            for (int i = 0; i < trace.frames.Count; i++) {
                InputFrame input = trace.frames[i].ToInputFrame(trace.characterObjectId);
                environment.rollback.SimulatePredictedTick(input, trace.tickDelta);
                AppendRecord(environment, result, input, input.frame);
            }

            CopyMetrics(environment, result);
            return result;
        }

        public static ReplayRunResult RunPredictedWithCorrections(ReplayHarnessEnvironment environment,
                                                                  ReplayTraceDefinition trace,
                                                                  ReplayRunResult authoritativeReference,
                                                                  ReplayComparisonSettings settings) {
            environment.PrepareForRun();
            ReplayRunResult result = CreateRunResult(trace, ReplayRunKind.PredictedWithCorrections);

            int deliveredAuthoritativeIndex = 0;
            for (int i = 0; i < trace.frames.Count; i++) {
                InputFrame input = trace.frames[i].ToInputFrame(trace.characterObjectId);
                environment.rollback.SimulatePredictedTick(input, trace.tickDelta);

                int deliveredThroughFrame = input.frame - Mathf.Max(0, settings.authoritativeDelayFrames);
                while (deliveredAuthoritativeIndex < authoritativeReference.records.Count &&
                       authoritativeReference.records[deliveredAuthoritativeIndex].sample.frame <= deliveredThroughFrame) {
                    ReplayTickRecord authoritativeRecord = authoritativeReference.records[deliveredAuthoritativeIndex];
                    MoveData authoritativeState = CreateAuthoritativeState(environment, authoritativeRecord);
                    AccumulatePredictionMetrics(environment, result, authoritativeRecord);
                    environment.rollback.ApplyAuthoritativeCorrection(authoritativeState,
                                                                     trace.tickDelta,
                                                                     tick => GetReplayInput(trace, tick));
                    result.correctionCount++;
                    deliveredAuthoritativeIndex++;
                }
            }

            while (deliveredAuthoritativeIndex < authoritativeReference.records.Count) {
                ReplayTickRecord authoritativeRecord = authoritativeReference.records[deliveredAuthoritativeIndex];
                MoveData authoritativeState = CreateAuthoritativeState(environment, authoritativeRecord);
                AccumulatePredictionMetrics(environment, result, authoritativeRecord);
                environment.rollback.ApplyAuthoritativeCorrection(authoritativeState,
                                                                 trace.tickDelta,
                                                                 tick => GetReplayInput(trace, tick));
                result.correctionCount++;
                deliveredAuthoritativeIndex++;
            }

            RebuildFromPredictedHistory(environment, trace, result);

            CopyMetrics(environment, result);
            return result;
        }

        private static ReplayRunResult CreateRunResult(ReplayTraceDefinition trace, ReplayRunKind runKind) {
            return new ReplayRunResult {
                traceId = trace.traceId,
                runKind = runKind
            };
        }

        private static void AppendRecord(ReplayHarnessEnvironment environment,
                                         ReplayRunResult result,
                                         InputFrame input,
                                         int frame) {
            result.records.Add(new ReplayTickRecord {
                input = input,
                sample = ReplayStateSample.FromMoveData(environment.character.moveData),
                state = environment.character.moveData != null ? environment.character.moveData.Clone() : null,
                querySnapshot = environment.character.TryGetSimulationDiagnostics(frame, out string diagnostics)
                    ? diagnostics
                    : string.Empty
            });
        }

        private static void CopyMetrics(ReplayHarnessEnvironment environment, ReplayRunResult result) {
            result.rollbackCount = environment.rollback.RollbackCount;
        }

        private static void RebuildFromPredictedHistory(ReplayHarnessEnvironment environment,
                                                        ReplayTraceDefinition trace,
                                                        ReplayRunResult result) {
            result.records.Clear();

            for (int i = 0; i < trace.frames.Count; i++) {
                InputFrame input = trace.frames[i].ToInputFrame(trace.characterObjectId);
                MoveData state = environment.rollback.TryGetPredictedState(input.frame, out MoveData predictedState)
                    ? predictedState
                    : null;

                result.records.Add(new ReplayTickRecord {
                    input = input,
                    state = state,
                    sample = ReplayStateSample.FromMoveData(state),
                    querySnapshot = environment.character.TryGetSimulationDiagnostics(input.frame, out string diagnostics)
                        ? diagnostics
                        : string.Empty
                });
            }
        }

        private static void AccumulatePredictionMetrics(ReplayHarnessEnvironment environment,
                                                        ReplayRunResult result,
                                                        ReplayTickRecord authoritativeRecord) {
            if (environment == null || result == null || authoritativeRecord == null)
                return;

            int frame = authoritativeRecord.sample.frame;
            if (!environment.rollback.TryGetPredictedState(frame, out MoveData predictedState) || predictedState == null) {
                result.predictedFillCount++;
                return;
            }

            ReplayStateSample predictedSample = ReplayStateSample.FromMoveData(predictedState);
            if (predictedSample.debugFingerprint == authoritativeRecord.sample.debugFingerprint)
                return;

            result.checksumMismatchCount++;
            if (result.firstChecksumMismatchFrame < 0)
                result.firstChecksumMismatchFrame = frame;
            result.lastChecksumMismatchFrame = frame;
        }

        private static InputFrame GetReplayInput(ReplayTraceDefinition trace, int tick) {
            if (trace == null || trace.frames == null)
                throw new InvalidOperationException($"Replay trace is missing while resolving replay input for tick {tick}.");

            if (tick >= 0 && tick < trace.frames.Count) {
                ReplayTraceFrame indexedFrame = trace.frames[tick];
                if (indexedFrame.frame == tick)
                    return indexedFrame.ToInputFrame(trace.characterObjectId);
            }

            for (int i = 0; i < trace.frames.Count; i++) {
                ReplayTraceFrame candidate = trace.frames[i];
                if (candidate.frame == tick)
                    return candidate.ToInputFrame(trace.characterObjectId);
            }

            throw new InvalidOperationException($"Missing canonical replay input for tick {tick} in trace '{trace.traceId}'.");
        }

        private static MoveData CreateAuthoritativeState(ReplayHarnessEnvironment environment, ReplayTickRecord authoritativeRecord) {
            if (authoritativeRecord != null && authoritativeRecord.state != null)
                return authoritativeRecord.state.Clone();

            ReplayStateSample sample = authoritativeRecord != null ? authoritativeRecord.sample : default;
            MoveData state = (environment.character != null && environment.character.moveData != null)
                ? environment.character.moveData.Clone()
                : new MoveData();

            state.frame = sample.frame;
            state.origin = sample.position;
            state.velocity = sample.velocity;
            state.viewAngles = new Vector3(sample.pitch, sample.yaw, 0f);
            state.grounded = sample.grounded;
            state.underwater = sample.underwater;
            state.cameraUnderwater = sample.cameraUnderwater;
            state.climbingLadder = sample.climbingLadder;
            state.isDashing = sample.isDashing;
            state.canAirDash = sample.canAirDash;
            state.sliding = sample.sliding;
            state.jumpCount = sample.jumpCount;
            state.jumpTimer = sample.jumpTimer;
            state.stamina = sample.stamina;
            state.staminaRegenTimer = sample.staminaRegenTimer;
            state.dashTimer = sample.dashTimer;
            state.currentDashDuration = sample.currentDashDuration;
            state.dashCooldownTimer = sample.dashCooldownTimer;
            state.moveType = sample.moveType;
            state.forwardMove = sample.forwardMove;
            state.sideMove = sample.sideMove;
            state.verticalAxis = sample.verticalAxis;
            state.horizontalAxis = sample.horizontalAxis;
            state.surfaceFriction = sample.surfaceFriction;
            state.gravityFactor = sample.gravityFactor;
            state.walkFactor = sample.walkFactor;
            state.fallingVelocity = sample.fallingVelocity;
            state.crouching = sample.crouching;
            state.meleeState = sample.meleeState;
            state.meleeTimer = sample.meleeTimer;
            state.meleeCooldownTimer = sample.meleeCooldownTimer;
            state.crouchLerp = sample.crouchLerp;
            state.renderCrouchLerp = sample.renderCrouchLerp;
            state.uncrouchDown = sample.uncrouchDown;
            state.groundedTemp = sample.groundedTemp;
            state.wasSliding = sample.wasSliding;
            state.slideSpeedCurrent = sample.slideSpeedCurrent;
            state.slideDirection = sample.slideDirection;
            state.slideDelay = sample.slideDelay;
            state.hasHitTarget = sample.hasHitTarget;
            state.meleeHitResolved = sample.meleeHitResolved;
            state.meleeHitTargetObjectId = sample.meleeHitTargetObjectId;
            state.meleeHitResolveTick = sample.meleeHitResolveTick;
            state.lastConsumedJumpPressFrame = sample.lastConsumedJumpPressFrame;
            state.lastConsumedDashPressFrame = sample.lastConsumedDashPressFrame;
            return state;
        }
    }
}
