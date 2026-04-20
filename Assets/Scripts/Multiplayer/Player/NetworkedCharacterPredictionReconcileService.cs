using FishNet.Object.Prediction;
using UnityEngine;

namespace Fragsurf.Movement {

    internal sealed class NetworkedCharacterPredictionReconcileService {
        private const int ReplayInputBufferSize = 1024;

        private readonly NetworkedCharacter _owner;
        private readonly InputFrame[] _replayInputs = new InputFrame[ReplayInputBufferSize];
        private readonly int[] _replayInputTicks = new int[ReplayInputBufferSize];
        private readonly byte[] _replayInputQuality = new byte[ReplayInputBufferSize];

        private const byte ReplayInputQualityMissing = 0;
        private const byte ReplayInputQualityRecovered = 1;
        private const byte ReplayInputQualityCanonical = 2;
        private readonly CorrectionGateConfig _correctionGateConfig = CorrectionGateConfig.CreateConservativeDefaults();
        private readonly StateDivergenceEvaluator _divergenceEvaluator;
        private readonly CorrectionGateDiagnosticsSnapshot _diagnostics = new CorrectionGateDiagnosticsSnapshot();
        private int _consecutiveObserveCount;

        internal CorrectionGateDiagnosticsSnapshot Diagnostics => _diagnostics;

        public NetworkedCharacterPredictionReconcileService(NetworkedCharacter owner) {
            _owner = owner;
            _divergenceEvaluator = new StateDivergenceEvaluator(_correctionGateConfig);

            for (int i = 0; i < ReplayInputBufferSize; i++) {
                _replayInputTicks[i] = InputFrame.InvalidFrame;
                _replayInputQuality[i] = ReplayInputQualityMissing;
            }
        }

        public void RunReplicate(NetworkedCharacterReplicateData replicateData,
                                 ReplicateState state,
                                 float deltaTime,
                                 ref InputFrame lastTickedInput,
                                 ref bool hasLastTickedInput) {
            RollbackManager rollback = _owner.RollbackManager;
            if (rollback == null)
                return;

            InputFrame input = replicateData.Input;
            int replicateTick = (int)replicateData.GetTick();
            if (replicateTick > 0) {
                input.frame = replicateTick;
            } else if (input.frame < 0 && state.ContainsCreated()) {
                input.frame = (_owner.NetworkTimeManager != null) ? (int)_owner.NetworkTimeManager.Tick : 0;
            }

            bool isRecoveredInput = false;
            if (!input.IsValid) {
                if (hasLastTickedInput) {
                    input = lastTickedInput;
                    if (replicateTick > 0)
                        input.frame = replicateTick;
                    isRecoveredInput = true;
                    LogReplayIntegrityError($"Missing canonical input for tick {input.frame}; using last known input as emergency recovery.");
                } else {
                    LogReplayIntegrityError("Missing canonical input and no previous authoritative input exists.");
                    return;
                }
            }

            if (input.characterObjectId <= 0)
                input.characterObjectId = _owner.ObjectId;

            if (input.characterObjectId != _owner.ObjectId)
                return;

            _diagnostics.ReplicateTicksProcessed++;
            _diagnostics.LastReplicateFrame = input.frame;

            if (state.ContainsTicked()) {
                lastTickedInput = input;
                hasLastTickedInput = true;
            }

            _owner.SyncCurrentMoveLook(input.LookYaw, input.LookPitch);
            StoreReplayInput(input, isRecoveredInput ? ReplayInputQualityRecovered : ReplayInputQualityCanonical);
            rollback.SimulatePredictedTick(input, deltaTime);
            ApplySpectatorPresentation(input.frame);
        }

        public NetworkedCharacterReconcileData BuildReconcileData(int frame) {
            MoveData state = _owner.CurrentMoveData;
            _owner.GetAuthoritativeLookAngles(out float yaw, out float pitch);

            NetworkedCharacterReconcileData data = new NetworkedCharacterReconcileData(frame) {
                Position = _owner.CurrentPosition,
                Velocity = _owner.CurrentVelocity,
                Yaw = yaw,
                Pitch = pitch,
                Stamina = (state != null) ? state.stamina : 0f,
                StaminaRegenTimer = (state != null) ? state.staminaRegenTimer : 0f,
                DashTimer = (state != null) ? state.dashTimer : 0f,
                CurrentDashDuration = (state != null) ? state.currentDashDuration : 0f,
                DashCooldownTimer = (state != null) ? state.dashCooldownTimer : 0f,
                IsDashing = BoolToByte(state != null && state.isDashing),
                CanAirDash = BoolToByte(state == null || state.canAirDash),
                JumpCount = (state != null) ? state.jumpCount : 0,
                JumpTimer = (state != null) ? state.jumpTimer : 0f,
                Grounded = BoolToByte(state != null && state.grounded),
                GroundedTemp = BoolToByte(state != null && state.groundedTemp),
                Jumping = BoolToByte(state != null && state.jumping),
                MoveType = (byte)((state != null) ? (int)state.moveType : (int)MoveType.Walk),
                ForwardMove = (state != null) ? state.forwardMove : 0f,
                SideMove = (state != null) ? state.sideMove : 0f,
                VerticalAxis = (state != null) ? state.verticalAxis : 0f,
                HorizontalAxis = (state != null) ? state.horizontalAxis : 0f,
                SurfaceFriction = (state != null) ? state.surfaceFriction : 0f,
                GravityFactor = (state != null) ? state.gravityFactor : 0f,
                WalkFactor = (state != null) ? state.walkFactor : 0f,
                FallingVelocity = (state != null) ? state.fallingVelocity : 0f,
                Crouching = BoolToByte(state != null && state.crouching),
                CrouchLerp = (state != null) ? state.crouchLerp : 0f,
                RenderCrouchLerp = (state != null) ? state.renderCrouchLerp : 0f,
                UncrouchDown = BoolToByte(state != null && state.uncrouchDown),
                Sliding = BoolToByte(state != null && state.sliding),
                WasSliding = BoolToByte(state != null && state.wasSliding),
                SlideSpeedCurrent = (state != null) ? state.slideSpeedCurrent : 0f,
                SlideDirection = (state != null) ? state.slideDirection : Vector3.zero,
                SlideDelay = (state != null) ? state.slideDelay : 0f,
                MeleeState = (byte)((state != null) ? (int)state.meleeState : (int)MoveData.MeleeState.None),
                MeleeTimer = (state != null) ? state.meleeTimer : 0f,
                MeleeCooldownTimer = (state != null) ? state.meleeCooldownTimer : 0f,
                HasHitTarget = BoolToByte(state != null && state.hasHitTarget),
                MeleeHitResolved = BoolToByte(state != null && state.meleeHitResolved),
                MeleeHitTargetObjectId = (state != null) ? state.meleeHitTargetObjectId : 0,
                MeleeHitResolveTick = (state != null) ? state.meleeHitResolveTick : InputFrame.InvalidFrame,
                LastConsumedJumpPressFrame = (state != null) ? state.lastConsumedJumpPressFrame : -1,
                LastConsumedDashPressFrame = (state != null) ? state.lastConsumedDashPressFrame : -1
            };

            return data;
        }

        public void ApplyReconcile(NetworkedCharacterReconcileData reconcileData, float deltaTime) {
            RollbackManager rollback = _owner.RollbackManager;
            SurfCharacter character = _owner.Character;
            if (rollback == null || character == null)
                return;

            _diagnostics.ReconcilePacketsReceived++;
            MoveData authoritativeState = BuildAuthoritativeMoveData(reconcileData, character);

            StateDivergenceReport report;
            if (!rollback.TryGetPredictedState(reconcileData.Frame, out MoveData predictedState)) {
                report = new StateDivergenceReport {
                    HasPredictedState = false,
                    Decision = CorrectionDecision.HardCorrect,
                    PrimaryReason = "Missing predicted state",
                    Summary = $"HardCorrect | reason=Missing predicted state for authoritative frame {reconcileData.Frame}"
                };
                _diagnostics.MissingPredictedStateCount++;
            } else {
                report = _divergenceEvaluator.Evaluate(predictedState, authoritativeState, _consecutiveObserveCount);
            }

            UpdateDiagnostics(report, reconcileData.Frame);
            LogCorrectionDecision(report, reconcileData.Frame);

            if (report.Decision == CorrectionDecision.Ignore || report.Decision == CorrectionDecision.ObserveOnly) {
                character.RefreshRuntimeStateFromMoveData();
                ApplySpectatorPresentation(reconcileData.Frame);
                return;
            }

            rollback.ApplyAuthoritativeCorrection(authoritativeState, deltaTime, GetReplayInputForTick);
            character.RefreshRuntimeStateFromMoveData();
            ApplySpectatorPresentation(reconcileData.Frame);
        }

        private MoveData BuildAuthoritativeMoveData(NetworkedCharacterReconcileData reconcileData, SurfCharacter character) {
            MoveData state = character.moveData != null ? character.moveData.Clone() : new MoveData();
            state.frame = reconcileData.Frame;
            state.origin = reconcileData.Position;
            state.velocity = reconcileData.Velocity;
            state.viewAngles.y = reconcileData.Yaw;
            state.viewAngles.x = reconcileData.Pitch;
            state.stamina = reconcileData.Stamina;
            state.staminaRegenTimer = reconcileData.StaminaRegenTimer;
            state.dashTimer = reconcileData.DashTimer;
            state.currentDashDuration = reconcileData.CurrentDashDuration;
            state.dashCooldownTimer = reconcileData.DashCooldownTimer;
            state.isDashing = ByteToBool(reconcileData.IsDashing);
            state.canAirDash = ByteToBool(reconcileData.CanAirDash);
            state.jumpCount = reconcileData.JumpCount;
            state.jumpTimer = reconcileData.JumpTimer;
            state.grounded = ByteToBool(reconcileData.Grounded);
            state.groundedTemp = ByteToBool(reconcileData.GroundedTemp);
            state.jumping = ByteToBool(reconcileData.Jumping);
            state.moveType = (MoveType)Mathf.Clamp(reconcileData.MoveType, (byte)MoveType.None, (byte)MoveType.HeavyMelee);
            state.forwardMove = reconcileData.ForwardMove;
            state.sideMove = reconcileData.SideMove;
            state.verticalAxis = reconcileData.VerticalAxis;
            state.horizontalAxis = reconcileData.HorizontalAxis;
            state.surfaceFriction = reconcileData.SurfaceFriction;
            state.gravityFactor = reconcileData.GravityFactor;
            state.walkFactor = reconcileData.WalkFactor;
            state.fallingVelocity = reconcileData.FallingVelocity;
            state.crouching = ByteToBool(reconcileData.Crouching);
            state.crouchLerp = reconcileData.CrouchLerp;
            state.renderCrouchLerp = reconcileData.RenderCrouchLerp;
            state.uncrouchDown = ByteToBool(reconcileData.UncrouchDown);
            state.sliding = ByteToBool(reconcileData.Sliding);
            state.wasSliding = ByteToBool(reconcileData.WasSliding);
            state.slideSpeedCurrent = reconcileData.SlideSpeedCurrent;
            state.slideDirection = reconcileData.SlideDirection;
            state.slideDelay = reconcileData.SlideDelay;
            state.meleeState = (MoveData.MeleeState)Mathf.Clamp(reconcileData.MeleeState,
                                                                (byte)MoveData.MeleeState.None,
                                                                (byte)MoveData.MeleeState.Recovery);
            state.meleeTimer = reconcileData.MeleeTimer;
            state.meleeCooldownTimer = reconcileData.MeleeCooldownTimer;
            state.hasHitTarget = ByteToBool(reconcileData.HasHitTarget);
            state.meleeHitResolved = ByteToBool(reconcileData.MeleeHitResolved);
            state.meleeHitTargetObjectId = reconcileData.MeleeHitTargetObjectId;
            state.meleeHitResolveTick = reconcileData.MeleeHitResolveTick;
            state.lastConsumedJumpPressFrame = reconcileData.LastConsumedJumpPressFrame;
            state.lastConsumedDashPressFrame = reconcileData.LastConsumedDashPressFrame;

            if (_owner.ShouldIgnoreCrouchForForeignSimulation) {
                state.crouching = false;
                state.crouchLerp = 0f;
                state.renderCrouchLerp = 0f;
                state.uncrouchDown = false;
            }

            return state;
        }

        private static byte BoolToByte(bool value) {
            return (byte)(value ? 1 : 0);
        }

        private static bool ByteToBool(byte value) {
            return value != 0;
        }

        private void StoreReplayInput(InputFrame input, byte quality) {
            if (!input.IsValid)
                return;

            int slot = FrameToSlot(input.frame);
            int existingTick = _replayInputTicks[slot];
            byte existingQuality = _replayInputQuality[slot];
            if (existingTick == input.frame) {
                InputFrame existingInput = _replayInputs[slot];
                if (existingInput.HasSameControls(input)) {
                    _replayInputs[slot] = input;
                    _replayInputQuality[slot] = (byte)Mathf.Max(existingQuality, quality);
                    return;
                }

                if (existingQuality == ReplayInputQualityRecovered && quality == ReplayInputQualityCanonical) {
                    _replayInputs[slot] = input;
                    _replayInputQuality[slot] = quality;
                    return;
                }

                if (existingQuality == ReplayInputQualityCanonical && quality == ReplayInputQualityRecovered) {
                    return;
                }

                if (existingQuality == ReplayInputQualityCanonical && quality == ReplayInputQualityCanonical) {
                    LogReplayIntegrityError($"Conflicting canonical replay input detected for tick {input.frame}.");
                }
            } else if (existingTick != InputFrame.InvalidFrame && !IsExpectedReplayEviction(existingTick, input.frame)) {
                LogReplayIntegrityError($"Replay input slot overwrite detected. slot={slot}, oldTick={existingTick}, newTick={input.frame}.");
            }

            _replayInputs[slot] = input;
            _replayInputTicks[slot] = input.frame;
            _replayInputQuality[slot] = quality;
        }

        private InputFrame GetReplayInputForTick(int tick) {
            int slot = FrameToSlot(tick);
            if (_replayInputTicks[slot] == tick) {
                InputFrame input = _replayInputs[slot];
                input.frame = tick;
                return input;
            }

            LogReplayIntegrityError($"Missing replay input for tick {tick}.");
            return InputFrame.Empty(InputFrame.InvalidFrame, _owner.ObjectId);
        }

        private static int FrameToSlot(int frame) {
            int slot = frame % ReplayInputBufferSize;
            return (slot < 0) ? slot + ReplayInputBufferSize : slot;
        }

        private static bool IsExpectedReplayEviction(int existingTick, int newTick) {
            if (existingTick == InputFrame.InvalidFrame)
                return false;

            return newTick - existingTick >= ReplayInputBufferSize;
        }

        private void ApplySpectatorPresentation(int frame) {
            _owner.ApplySpectatorPresentationYawFromCurrentState();

            if (_owner.DebugHasLocalAuthority)
                return;

            _diagnostics.SpectatorPresentationApplications++;
            _diagnostics.LastSpectatorPresentationFrame = frame;
        }

        private void LogReplayIntegrityError(string message) {
            Debug.LogError($"[NetworkedCharacterPredictionReconcileService] {message}", _owner);
        }

        private void UpdateDiagnostics(StateDivergenceReport report, int frame) {
            _diagnostics.LastFrame = frame;
            _diagnostics.LastDecision = report.Decision;
            _diagnostics.LastWeightedScore = report.WeightedScore;
            _diagnostics.LastPrimaryReason = report.PrimaryReason ?? string.Empty;
            _diagnostics.LastSummary = report.Summary ?? string.Empty;

            switch (report.Decision) {
                case CorrectionDecision.Ignore:
                    _diagnostics.IgnoredCount++;
                    _consecutiveObserveCount = 0;
                    break;
                case CorrectionDecision.ObserveOnly:
                    _diagnostics.ObserveOnlyCount++;
                    _consecutiveObserveCount = report.ConsecutiveObserveFrames;
                    break;
                case CorrectionDecision.HardCorrect:
                    _diagnostics.HardCorrectCount++;
                    _consecutiveObserveCount = 0;
                    break;
                case CorrectionDecision.ForceCorrect:
                    _diagnostics.ForceCorrectCount++;
                    _consecutiveObserveCount = 0;
                    break;
            }

            _diagnostics.ConsecutiveObserveCount = _consecutiveObserveCount;
        }

        private void LogCorrectionDecision(StateDivergenceReport report, int frame) {
            if (!NetcodeDebugEngine.IsAvailable)
                return;

            NetcodeDebugSeverity severity = report.Decision == CorrectionDecision.ForceCorrect
                ? NetcodeDebugSeverity.Warning
                : NetcodeDebugSeverity.Info;
            NetcodeDebugSuspectFlags suspects =
                (report.Decision == CorrectionDecision.HardCorrect || report.Decision == CorrectionDecision.ForceCorrect)
                    ? NetcodeDebugSuspectFlags.CorrectionPolicyPressure
                    : NetcodeDebugSuspectFlags.None;

            NetcodeDebugContext context = new NetcodeDebugContext {
                ObjectId = _owner.ObjectId,
                OwnerId = (_owner.Owner != null && _owner.Owner.IsValid) ? _owner.Owner.ClientId : -1,
                LocalClientId = (_owner.LocalConnection != null && _owner.LocalConnection.IsValid) ? _owner.LocalConnection.ClientId : -1,
                Tick = (_owner.NetworkTimeManager != null) ? (int)_owner.NetworkTimeManager.Tick : frame,
                Frame = frame,
                IsServerInitialized = _owner.IsServerInitialized,
                IsClientInitialized = _owner.IsClientInitialized,
                IsOwner = _owner.IsOwner,
                HasLocalAuthority = _owner.DebugHasLocalAuthority,
                MoveType = _owner.CurrentMoveData != null ? _owner.CurrentMoveData.moveType.ToString() : string.Empty
            };

            NetcodeDebugEngine.Log(NetcodeDebugCategory.Divergence,
                                   severity,
                                   context,
                                   report.Summary,
                                   suspects,
                                   $"correction-gate-{_owner.ObjectId}-{report.Decision}",
                                   report.Decision == CorrectionDecision.ObserveOnly ? 0.25f : 0f);
        }
    }
}
