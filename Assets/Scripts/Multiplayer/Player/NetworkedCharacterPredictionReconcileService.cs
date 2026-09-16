using FishNet.Object.Prediction;
using UnityEngine;

namespace Fragsurf.Movement {

    internal sealed class NetworkedCharacterPredictionReconcileService {
        private const int SpectatorSnapshotBufferSize = 64;
        private const int SpectatorInterpolationDelayTicks = 2;

        private readonly NetworkedCharacter _owner;
        private readonly AuthoritativeMoveDataSnapshotBuffer _spectatorSnapshots = new AuthoritativeMoveDataSnapshotBuffer();

        private readonly CorrectionGateConfig _correctionGateConfig = CorrectionGateConfig.CreateConservativeDefaults();
        private readonly StateDivergenceEvaluator _divergenceEvaluator;
        private readonly CorrectionGateDiagnosticsSnapshot _diagnostics = new CorrectionGateDiagnosticsSnapshot();
        private int _consecutiveObserveCount;
        private bool _suppressReplay;

        internal CorrectionGateDiagnosticsSnapshot Diagnostics => _diagnostics;

        public NetworkedCharacterPredictionReconcileService(NetworkedCharacter owner) {
            _owner = owner;
            _divergenceEvaluator = new StateDivergenceEvaluator(_correctionGateConfig);
        }

        public void RunReplicate(NetworkedCharacterReplicateData replicateData,
                                 ReplicateState state,
                                 float deltaTime,
                                 ref InputFrame lastTickedInput,
                                 ref bool hasLastTickedInput) {
            if (_owner.IsPureClientProxy)
                return;
            if (_suppressReplay && state.ContainsReplayed())
                return;

            RollbackManager rollback = _owner.RollbackManager;
            if (rollback == null)
                return;

            if (replicateData.HasInput && replicateData.MovementEpoch != _owner.MovementEpoch) {
                // Old-life inputs must not move a respawned owner during replay.
                if (state.ContainsReplayed() || !_owner.IsServerInitialized)
                    return;
                replicateData.HasInput = false;
            }
            int replicateTick = unchecked((int)replicateData.GetTick());
            if (replicateTick < 0)
                return;
            InputFrame previous = _owner.CurrentMoveData != null
                ? _owner.CurrentMoveData.lastInput : InputFrame.Empty();
            if (!previous.IsValid && _owner.CurrentMoveData != null) {
                Vector3 look = _owner.CurrentMoveData.viewAngles;
                previous = InputFrame.Create(0, _owner.ObjectId, 0, 0, 0, 0, look.y, look.x);
            }
            if (!TryResolveInput(replicateData, state, previous, _owner.ObjectId, out InputFrame input))
                return;

            _diagnostics.ReplicateTicksProcessed++;
            _diagnostics.LastReplicateFrame = input.frame;

            if (state.ContainsTicked()) {
                lastTickedInput = input;
                hasLastTickedInput = true;
            }

            rollback.SimulatePredictedTick(input, deltaTime, !state.ContainsReplayed());
            ApplySpectatorPresentation(input.frame);
        }

        internal static bool TryResolveInput(NetworkedCharacterReplicateData data, ReplicateState state,
                                             InputFrame previous, int objectId, out InputFrame input) {
            int tick = unchecked((int)data.GetTick());
            input = InputFrame.Empty();
            if (tick < 0)
                return false;
            // Availability precedes tick stamping: default structs are not input.
            if (state.ContainsCreated() && data.HasInput && data.Input.IsValid) {
                input = data.Input;
                if (input.characterObjectId != objectId)
                    return false;
                input.frame = tick;
                return true;
            }
            // The seed is part of the restored state, not the latest live input.
            input = previous.IsValid && previous.characterObjectId == objectId
                ? previous.PredictForTick(tick) : InputFrame.Empty(tick, objectId);
            return true;
        }

        public NetworkedCharacterReconcileData BuildReconcileData(int frame) {
            MoveData state = _owner.CurrentMoveData;
            _owner.GetAuthoritativeLookAngles(out float yaw, out float pitch);

            NetworkedCharacterReconcileData data = new NetworkedCharacterReconcileData(frame) {
                DefaultHeight = state != null ? state.defaultHeight : 2f,
                CrouchingHeight = state != null ? state.crouchingHeight : 1f,
                CrouchingSpeed = state != null ? state.crouchingSpeed : 10f,
                SlidingEnabled = state != null ? state.slidingEnabled : false,
                UseStepOffset = state != null ? state.useStepOffset : false,
                StepOffset = state != null ? state.stepOffset : 0f,
                MovementEpoch = _owner.MovementEpoch,
                LastInput = state != null ? state.lastInput : InputFrame.Empty(),
                MaxStaminaBonus = state != null ? state.maxStaminaBonus : 0f,
                StaminaRegenBonus = state != null ? state.staminaRegenBonus : 0f,
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
                DashStartedThisFrame = BoolToByte(state != null && state.dashStartedThisFrame),
                DoubleJumpedThisFrame = BoolToByte(state != null && state.doubleJumpedThisFrame),
                MeleeHitThisFrame = BoolToByte(state != null && state.meleeHitThisFrame),
                MeleeState = (byte)((state != null) ? (int)state.meleeState : (int)MoveData.MeleeState.None),
                MeleeTimer = (state != null) ? state.meleeTimer : 0f,
                MeleeCooldownTimer = (state != null) ? state.meleeCooldownTimer : 0f,
                MeleeHoldTimer = (state != null) ? state.meleeHoldTimer : 0f,
                HasHitTarget = BoolToByte(state != null && state.hasHitTarget),
                MeleeHitResolved = BoolToByte(state != null && state.meleeHitResolved),
                MeleeHitTargetObjectId = (state != null) ? state.meleeHitTargetObjectId : 0,
                MeleeHitResolveTick = (state != null) ? state.meleeHitResolveTick : InputFrame.InvalidFrame,
                IsParrying = BoolToByte(state != null && state.isParrying),
                ParryTimer = (state != null) ? state.parryTimer : 0f,
                ParryCooldownTimer = (state != null) ? state.parryCooldownTimer : 0f,
                ParryStartedThisFrame = BoolToByte(state != null && state.parryStartedThisFrame),
                ParrySuccessThisFrame = BoolToByte(state != null && state.parrySuccessThisFrame),
                LastConsumedParryPressFrame = (state != null) ? state.lastConsumedParryPressFrame : -1,
                LastConsumedJumpPressFrame = (state != null) ? state.lastConsumedJumpPressFrame : -1,
                LastConsumedDashPressFrame = (state != null) ? state.lastConsumedDashPressFrame : -1
            };

            return data;
        }

        public void ApplyReconcile(NetworkedCharacterReconcileData reconcileData, float deltaTime) {
            SurfCharacter character = _owner.Character;
            if (character == null)
                return;

            bool newEpoch = reconcileData.MovementEpoch > _owner.MovementEpoch;
            _suppressReplay = !_owner.AcceptMovementEpoch(reconcileData.MovementEpoch);
            if (_suppressReplay)
                return;
            // Frame on the wire is server time. FishNet maps GetTick() into the
            // owning client's local input timeline before invoking reconciliation.
            if (reconcileData.GetTick() != uint.MaxValue)
                reconcileData.Frame = unchecked((int)reconcileData.GetTick());
            if (reconcileData.Frame < 0)
                return;

            _diagnostics.ReconcilePacketsReceived++;

            RollbackManager rollback = _owner.RollbackManager;
            MoveData predictedState = null;
            bool hasPredictedState = false;
            if (!_owner.IsPureClientProxy) {
                if (rollback == null)
                    return;

                hasPredictedState = rollback.TryGetPredictedState(reconcileData.Frame, out predictedState);
            }

            MoveData authoritativeState = BuildAuthoritativeMoveData(reconcileData, character, predictedState);
            if (newEpoch)
                _owner.OnReconciledSpawn(authoritativeState);

            if (_owner.IsPureClientProxy) {
                _spectatorSnapshots.Store(reconcileData.Frame, authoritativeState);
                UpdateSpectatorDiagnostics(reconcileData.Frame);
                UpdateSpectatorPresentation();
                return;
            }

            StateDivergenceReport report;
            if (!hasPredictedState) {
                report = new StateDivergenceReport {
                    HasPredictedState = false,
                    Decision = CorrectionDecision.HardCorrect,
                    PrimaryReason = "Missing predicted state",
                    Summary = $"HardCorrect | reason=Missing predicted state for authoritative frame {reconcileData.Frame}"
                };
                _diagnostics.MissingPredictedStateCount++;
            } else {
                report = _divergenceEvaluator.Evaluate(predictedState,
                                                       authoritativeState,
                                                       _consecutiveObserveCount,
                                                       !_owner.DebugHasLocalAuthority);
            }

            UpdateDiagnostics(report, reconcileData.Frame);
            LogCorrectionDecision(report, reconcileData.Frame);

            // The gate remains diagnostic. FishNet will replay even when the
            // difference is small, so every reconcile must restore its baseline.
            rollback.RestoreAuthoritativeState(authoritativeState);
            character.RefreshRuntimeStateFromMoveData();
            ApplySpectatorPresentation(reconcileData.Frame);
        }

        public void UpdateSpectatorPresentation() {
            if (!_owner.IsPureClientProxy)
                return;

            if (!_spectatorSnapshots.TryGetInterpolatedState(GetSpectatorPresentationFrame(), out MoveData presentationState))
                return;

            _owner.ApplySpectatorPresentationState(presentationState);
            _diagnostics.SpectatorPresentationApplications++;
            _diagnostics.LastSpectatorPresentationFrame = presentationState.frame;
        }

        public void ResetSpectatorPresentationBuffer() {
            _spectatorSnapshots.Clear();
        }

        public void ResetSession() {
            _suppressReplay = false;
            _consecutiveObserveCount = 0;
            _spectatorSnapshots.Clear();
        }

        private MoveData BuildAuthoritativeMoveData(NetworkedCharacterReconcileData reconcileData,
                                                    SurfCharacter character,
                                                    MoveData predictedState) {
            MoveData state = predictedState != null
                ? predictedState.Clone()
                : CreateAuthoritativeMoveDataShell(character);
            state.frame = reconcileData.Frame;
            state.lastInput = reconcileData.LastInput;
            if (state.lastInput.IsValid)
                state.lastInput.frame = reconcileData.Frame;
            state.defaultHeight = reconcileData.DefaultHeight;
            state.crouchingHeight = reconcileData.CrouchingHeight;
            state.crouchingSpeed = reconcileData.CrouchingSpeed;
            state.slidingEnabled = reconcileData.SlidingEnabled;
            state.useStepOffset = reconcileData.UseStepOffset;
            state.stepOffset = reconcileData.StepOffset;
            state.maxStaminaBonus = reconcileData.MaxStaminaBonus;
            state.staminaRegenBonus = reconcileData.StaminaRegenBonus;
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
            state.dashStartedThisFrame = ByteToBool(reconcileData.DashStartedThisFrame);
            state.doubleJumpedThisFrame = ByteToBool(reconcileData.DoubleJumpedThisFrame);
            state.meleeHitThisFrame = ByteToBool(reconcileData.MeleeHitThisFrame);
            state.meleeState = (MoveData.MeleeState)Mathf.Clamp(reconcileData.MeleeState,
                                                                (byte)MoveData.MeleeState.None,
                                                                (byte)MoveData.MeleeState.Recovery);
            state.meleeTimer = reconcileData.MeleeTimer;
            state.meleeCooldownTimer = reconcileData.MeleeCooldownTimer;
            state.meleeHoldTimer = reconcileData.MeleeHoldTimer;
            state.hasHitTarget = ByteToBool(reconcileData.HasHitTarget);
            state.meleeHitResolved = ByteToBool(reconcileData.MeleeHitResolved);
            state.meleeHitTargetObjectId = reconcileData.MeleeHitTargetObjectId;
            state.meleeHitResolveTick = reconcileData.MeleeHitResolveTick;
            state.isParrying = ByteToBool(reconcileData.IsParrying);
            state.wishParry = false;
            state.parryTimer = reconcileData.ParryTimer;
            state.parryCooldownTimer = reconcileData.ParryCooldownTimer;
            state.parryStartedThisFrame = ByteToBool(reconcileData.ParryStartedThisFrame);
            state.parrySuccessThisFrame = ByteToBool(reconcileData.ParrySuccessThisFrame);
            state.lastConsumedParryPressFrame = reconcileData.LastConsumedParryPressFrame;
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

        private static MoveData CreateAuthoritativeMoveDataShell(SurfCharacter character) {
            MoveData template = character != null ? character.moveData : null;
            return new MoveData {
                playerTransform = template != null ? template.playerTransform : (character != null ? character.transform : null),
                viewTransform = template != null ? template.viewTransform : (character != null ? character.viewTransform : null),
                viewTransformDefaultLocalPos = template != null
                    ? template.viewTransformDefaultLocalPos
                    : (character != null && character.viewTransform != null ? character.viewTransform.localPosition : Vector3.zero),
                slopeLimit = template != null ? template.slopeLimit : (character != null && character.movementConfig != null ? character.movementConfig.slopeLimit : 45f),
                rigidbodyPushForce = template != null ? template.rigidbodyPushForce : (character != null ? character.rigidbodyPushForce : 1f),
                defaultHeight = template != null ? template.defaultHeight : 2f,
                crouchingHeight = template != null ? template.crouchingHeight : (character != null ? character.crouchingHeightMultiplier : 1f),
                crouchingSpeed = template != null ? template.crouchingSpeed : (character != null ? character.crouchingSpeed : 10f),
                slidingEnabled = template != null ? template.slidingEnabled : character != null && character.slidingEnabled,
                laddersEnabled = template != null ? template.laddersEnabled : character != null && character.laddersEnabled,
                angledLaddersEnabled = template != null ? template.angledLaddersEnabled : character != null && character.supportAngledLadders,
                useStepOffset = template != null ? template.useStepOffset : character != null && character.useStepOffset,
                stepOffset = template != null ? template.stepOffset : (character != null ? character.stepOffset : 0f)
            };
        }

        private static byte BoolToByte(bool value) {
            return (byte)(value ? 1 : 0);
        }

        private static bool ByteToBool(byte value) {
            return value != 0;
        }

        private void ApplySpectatorPresentation(int frame) {
            _owner.ApplySpectatorPresentationYawFromCurrentState();

            if (_owner.DebugHasLocalAuthority)
                return;

            _diagnostics.SpectatorPresentationApplications++;
            _diagnostics.LastSpectatorPresentationFrame = frame;
        }

        private float GetSpectatorPresentationFrame() {
            if (_owner.NetworkTimeManager == null)
                return _spectatorSnapshots.LatestFrame;

            float tickPercent = Mathf.Clamp01((float)_owner.NetworkTimeManager.GetTickPercentAsDouble());
            return (float)_owner.NetworkTimeManager.Tick - SpectatorInterpolationDelayTicks + tickPercent;
        }

        private void UpdateSpectatorDiagnostics(int frame) {
            _diagnostics.LastFrame = frame;
            _diagnostics.LastDecision = CorrectionDecision.Ignore;
            _diagnostics.LastWeightedScore = 0f;
            _diagnostics.LastPrimaryReason = "Pure client proxy spectator presentation";
            _diagnostics.LastSummary = $"SpectatorPresentation | authoritative frame {frame} buffered";
            _consecutiveObserveCount = 0;
            _diagnostics.ConsecutiveObserveCount = 0;
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

        private sealed class AuthoritativeMoveDataSnapshotBuffer {
            private readonly MoveData[] _states = new MoveData[SpectatorSnapshotBufferSize];
            private readonly int[] _frames = new int[SpectatorSnapshotBufferSize];

            public int LatestFrame { get; private set; } = InputFrame.InvalidFrame;

            public AuthoritativeMoveDataSnapshotBuffer() {
                Clear();
            }

            public void Clear() {
                for (int i = 0; i < SpectatorSnapshotBufferSize; i++) {
                    _states[i] = null;
                    _frames[i] = InputFrame.InvalidFrame;
                }

                LatestFrame = InputFrame.InvalidFrame;
            }

            public void Store(int frame, MoveData state) {
                if (state == null || frame == InputFrame.InvalidFrame)
                    return;

                int slot = FrameToSlot(frame);
                _states[slot] = state.Clone();
                _frames[slot] = frame;

                if (LatestFrame == InputFrame.InvalidFrame || frame > LatestFrame)
                    LatestFrame = frame;
            }

            public bool TryGetInterpolatedState(float targetFrame, out MoveData state) {
                state = null;

                int beforeFrame = InputFrame.InvalidFrame;
                int afterFrame = InputFrame.InvalidFrame;
                MoveData beforeState = null;
                MoveData afterState = null;

                for (int i = 0; i < SpectatorSnapshotBufferSize; i++) {
                    MoveData candidate = _states[i];
                    int frame = _frames[i];
                    if (candidate == null || frame == InputFrame.InvalidFrame)
                        continue;

                    if (frame <= targetFrame && (beforeFrame == InputFrame.InvalidFrame || frame > beforeFrame)) {
                        beforeFrame = frame;
                        beforeState = candidate;
                    }

                    if (frame >= targetFrame && (afterFrame == InputFrame.InvalidFrame || frame < afterFrame)) {
                        afterFrame = frame;
                        afterState = candidate;
                    }
                }

                if (beforeState != null && afterState != null) {
                    if (beforeFrame == afterFrame) {
                        state = beforeState.Clone();
                        return true;
                    }

                    float span = Mathf.Max(1f, afterFrame - beforeFrame);
                    float t = Mathf.Clamp01((targetFrame - beforeFrame) / span);
                    state = Interpolate(beforeState, afterState, t);
                    return true;
                }

                if (beforeState != null) {
                    state = beforeState.Clone();
                    return true;
                }

                if (afterState != null) {
                    state = afterState.Clone();
                    return true;
                }

                return false;
            }

            private static MoveData Interpolate(MoveData from, MoveData to, float t) {
                MoveData state = from.Clone();
                state.origin = Vector3.Lerp(from.origin, to.origin, t);
                state.velocity = Vector3.Lerp(from.velocity, to.velocity, t);
                state.viewAngles = new Vector3(Mathf.LerpAngle(from.viewAngles.x, to.viewAngles.x, t),
                                               Mathf.LerpAngle(from.viewAngles.y, to.viewAngles.y, t),
                                               Mathf.LerpAngle(from.viewAngles.z, to.viewAngles.z, t));
                state.forwardMove = Mathf.Lerp(from.forwardMove, to.forwardMove, t);
                state.sideMove = Mathf.Lerp(from.sideMove, to.sideMove, t);
                state.verticalAxis = Mathf.Lerp(from.verticalAxis, to.verticalAxis, t);
                state.horizontalAxis = Mathf.Lerp(from.horizontalAxis, to.horizontalAxis, t);
                state.stamina = Mathf.Lerp(from.stamina, to.stamina, t);
                state.staminaRegenTimer = Mathf.Lerp(from.staminaRegenTimer, to.staminaRegenTimer, t);
                state.dashTimer = Mathf.Lerp(from.dashTimer, to.dashTimer, t);
                state.currentDashDuration = Mathf.Lerp(from.currentDashDuration, to.currentDashDuration, t);
                state.dashCooldownTimer = Mathf.Lerp(from.dashCooldownTimer, to.dashCooldownTimer, t);
                state.jumpTimer = Mathf.Lerp(from.jumpTimer, to.jumpTimer, t);
                state.surfaceFriction = Mathf.Lerp(from.surfaceFriction, to.surfaceFriction, t);
                state.gravityFactor = Mathf.Lerp(from.gravityFactor, to.gravityFactor, t);
                state.walkFactor = Mathf.Lerp(from.walkFactor, to.walkFactor, t);
                state.fallingVelocity = Mathf.Lerp(from.fallingVelocity, to.fallingVelocity, t);
                state.crouchLerp = Mathf.Lerp(from.crouchLerp, to.crouchLerp, t);
                state.renderCrouchLerp = Mathf.Lerp(from.renderCrouchLerp, to.renderCrouchLerp, t);
                state.slideSpeedCurrent = Mathf.Lerp(from.slideSpeedCurrent, to.slideSpeedCurrent, t);
                state.slideDirection = Vector3.Lerp(from.slideDirection, to.slideDirection, t);
                state.slideDelay = Mathf.Lerp(from.slideDelay, to.slideDelay, t);
                state.meleeTimer = Mathf.Lerp(from.meleeTimer, to.meleeTimer, t);
                state.meleeCooldownTimer = Mathf.Lerp(from.meleeCooldownTimer, to.meleeCooldownTimer, t);
                state.meleeHoldTimer = Mathf.Lerp(from.meleeHoldTimer, to.meleeHoldTimer, t);
                state.parryTimer = Mathf.Lerp(from.parryTimer, to.parryTimer, t);
                state.parryCooldownTimer = Mathf.Lerp(from.parryCooldownTimer, to.parryCooldownTimer, t);
                return state;
            }

            private static int FrameToSlot(int frame) {
                int slot = frame % SpectatorSnapshotBufferSize;
                return (slot < 0) ? slot + SpectatorSnapshotBufferSize : slot;
            }
        }
    }
}
