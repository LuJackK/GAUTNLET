using UnityEngine;
using FishNet.Object;
using FishNet.Object.Prediction;
using FishNet.Managing.Timing;
using FishNet.Connection;
using FishNet.Transporting;
using UnityEngine.EventSystems;

namespace Fragsurf.Movement {

    public class NetworkedCharacter : NetworkBehaviour {
        private const string MAIN_CAMERA_TAG = "MainCamera";
        private const string UNTAGGED = "Untagged";

        private struct PredictionReplicateData : IReplicateData {
            public InputFrame Input;
            private uint _tick;

            public PredictionReplicateData(InputFrame input) {
                Input = input;
                _tick = 0;
            }

            public uint GetTick() => _tick;
            public void SetTick(uint value) => _tick = value;
            public void Dispose() { }
        }

        private struct PredictionReconcileData : IReconcileData {
            public int Frame;
            public Vector3 Position;
            public Vector3 Velocity;
            public float Yaw;
            public float Pitch;
            public float Stamina;
            public float StaminaRegenTimer;
            public float DashTimer;
            public float CurrentDashDuration;
            public float DashCooldownTimer;
            public byte IsDashing;
            public byte CanAirDash;
            public int JumpCount;
            public float JumpTimer;
            public byte Grounded;
            public byte Underwater;
            public byte CameraUnderwater;
            public byte ClimbingLadder;
            public byte MoveType;
            public byte MeleeState;
            public float MeleeTimer;
            public float MeleeCooldownTimer;
            public float CrouchLerp;
            public byte Sliding;
            public float SlideSpeedCurrent;
            private uint _tick;

            public PredictionReconcileData(int frame,
                                           Vector3 position,
                                           Vector3 velocity,
                                           float yaw,
                                           float pitch,
                                           float stamina,
                                           float staminaRegenTimer,
                                           float dashTimer,
                                           float currentDashDuration,
                                           float dashCooldownTimer,
                                           byte isDashing,
                                           byte canAirDash,
                                           int jumpCount,
                                           float jumpTimer,
                                           byte grounded,
                                           byte underwater,
                                           byte cameraUnderwater,
                                           byte climbingLadder,
                                           byte moveType,
                                           byte meleeState,
                                           float meleeTimer,
                                           float meleeCooldownTimer,
                                           float crouchLerp,
                                           byte sliding,
                                           float slideSpeedCurrent) {
                Frame = frame;
                Position = position;
                Velocity = velocity;
                Yaw = yaw;
                Pitch = pitch;
                Stamina = stamina;
                StaminaRegenTimer = staminaRegenTimer;
                DashTimer = dashTimer;
                CurrentDashDuration = currentDashDuration;
                DashCooldownTimer = dashCooldownTimer;
                IsDashing = isDashing;
                CanAirDash = canAirDash;
                JumpCount = jumpCount;
                JumpTimer = jumpTimer;
                Grounded = grounded;
                Underwater = underwater;
                CameraUnderwater = cameraUnderwater;
                ClimbingLadder = climbingLadder;
                MoveType = moveType;
                MeleeState = meleeState;
                MeleeTimer = meleeTimer;
                MeleeCooldownTimer = meleeCooldownTimer;
                CrouchLerp = crouchLerp;
                Sliding = sliding;
                SlideSpeedCurrent = slideSpeedCurrent;
                _tick = 0;
            }

            public uint GetTick() => _tick;
            public void SetTick(uint value) => _tick = value;
            public void Dispose() { }
        }

        // Option A contract (host-authoritative):
        // - NetworkedCharacter is the movement sync entrypoint.
        // - RollbackManager + SurfCharacter drive simulation.
        // - Ownership/input gating remains centralized here.
        
        [SerializeField] private SurfCharacter _character;
        [SerializeField] private RollbackManager _rollback;
        [SerializeField] private LocalInputCollector _inputCollector;
        [SerializeField] private PlayerAiming _playerAiming;

        [Header("Look Sync")]
        [SerializeField, Min(0f)] private float _lookApplySpeed = 20f;

        private float _remoteYaw;
        private float _remotePitch;
        private bool _hasRemoteLook;

        [Header("Migration Flags")]
        [SerializeField] private bool _useFishNetPredictionPipeline = true;
        [SerializeField] private bool _useLegacyAuthoritativeBroadcast = true;
        [SerializeField] private bool _useLegacyInputObserverBroadcast = true;
        [SerializeField] private bool _useLegacyFrameValidation = true;

        [Header("Authoritative Position Sync")]
        [SerializeField] private bool _enforceAuthoritativePosition = true;
        [SerializeField, Min(1)] private int _authoritativeSyncIntervalTicks = 1;
        [SerializeField] private bool _reconcileOwnerContinuously = true;
        [SerializeField, Min(0f)] private float _ownerReconcileNudgeDistance = 0.015f;
        [SerializeField, Min(0f)] private float _ownerReconcileSnapDistance = 0.30f;
        [SerializeField, Range(0f, 1f)] private float _ownerReconcileBlendFactor = 0.35f;
        [SerializeField, Min(0f)] private float _proxyReconcileNudgeDistance = 0.02f;
        [SerializeField, Min(0f)] private float _proxyReconcileSnapDistance = 0.20f;
        [SerializeField, Range(0f, 1f)] private float _reconcileBlendFactor = 0.15f;
        [SerializeField] private bool _ownerSkipReconcileDuringBurstMovement = true;
        [SerializeField, Min(0)] private int _ownerBurstReconcileSkipLimit = 6;

        [Header("Input Transport")]
        [SerializeField, Min(0)] private int _inputRedundancyFrames = 2;

        [Header("Proxy Interpolation")]
        [SerializeField] private bool _enableProxyInterpolationSmoothing = true;
        [SerializeField, Min(0f)] private float _proxyInterpolationSmoothTime = 0.06f;
        [SerializeField, Min(0f)] private float _proxyInterpolationMaxSpeed = 100f;
        [SerializeField, Min(0f)] private float _proxyInterpolationSnapDistance = 1.5f;
        [SerializeField, Min(0f)] private float _proxyVelocityBlendSpeed = 12f;

        [Header("Camera Ownership")]
        [SerializeField] private bool _enforceCameraOwnershipInvariant = true;
        [SerializeField] private bool _enforceVirtualCameraOwnershipInvariant = true;

        private int _lastServerReceivedFrame = -1;
        private int _ownerMismatchRpcCount;
        private int _ownerBurstReconcileSkippedCount;
        private int _lastAuthoritativeStateBroadcastTick = -1;
        private int _lastAppliedAuthoritativeFrame = -1;
        private const int SERVER_INPUT_HISTORY_SLOTS = 512;
        private readonly int[] _serverAcceptedInputFrames = new int[SERVER_INPUT_HISTORY_SLOTS];
        private const int CLIENT_INPUT_HISTORY_SLOTS = 64;
        private readonly InputFrame[] _clientInputHistory = new InputFrame[CLIENT_INPUT_HISTORY_SLOTS];
        private readonly int[] _clientInputHistoryFrames = new int[CLIENT_INPUT_HISTORY_SLOTS];
        private bool _hasProxyInterpolationTarget;
        private Vector3 _proxyInterpolationTargetPosition;
        private Vector3 _proxyInterpolationTargetVelocity;
        private Vector3 _proxyInterpolationVelocityRef;
        private InputFrame _lastTickedPredictionInput;
        private bool _hasLastTickedPredictionInput;

        [Header("UI/Input Ownership Guard")]
        [SerializeField] private bool _removeEventSystemsFromPlayerHierarchy = true;

        private void Awake() {
            InitializeInputFrameTracking();

            if (_character == null) _character = GetComponent<SurfCharacter>();
            if (_rollback == null) _rollback = GetComponent<RollbackManager>();
            if (_inputCollector == null) _inputCollector = GetComponent<LocalInputCollector>();
            if (_playerAiming == null) _playerAiming = GetComponentInChildren<PlayerAiming>(true);

            // Conservative default: disable local control until explicit ownership callbacks run.
            if (_inputCollector != null)
                _inputCollector.enabled = false;
            if (_playerAiming != null)
                _playerAiming.enabled = false;

            ValidateAuthorityContract();

            if (_rollback != null && _character != null) {
                _rollback.Initialize(_character);
            }
        }

        private void ValidateAuthorityContract() {
            if (_character == null)
                Debug.LogWarning("[NetworkedCharacter] Missing SurfCharacter. Movement sync contract is incomplete.", this);

            if (_rollback == null)
                Debug.LogWarning("[NetworkedCharacter] Missing RollbackManager. Movement sync contract is incomplete.", this);

            if (_inputCollector == null)
                Debug.LogWarning("[NetworkedCharacter] Missing LocalInputCollector. Owner input capture is unavailable.", this);

            // Guardrail only: do not alter behavior here.
            // RotateWithCamera can compete with PlayerAiming yaw ownership.
            bool hasLegacyRotateWithCamera = false;
            MonoBehaviour[] behaviours = GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++) {
                MonoBehaviour b = behaviours[i];
                if (b != null && b.GetType().Name == "RotateWithCamera") {
                    hasLegacyRotateWithCamera = true;
                    break;
                }
            }

            if (hasLegacyRotateWithCamera && _playerAiming != null) {
                // Step 6: Disable competing yaw authority to keep PlayerAiming as single source.
                // Conservative approach: detect and disable at runtime.
                MonoBehaviour[] allBehaviours = GetComponentsInChildren<MonoBehaviour>(true);
                for (int j = 0; j < allBehaviours.Length; j++) {
                    MonoBehaviour comp = allBehaviours[j];
                    if (comp != null && comp.GetType().Name == "RotateWithCamera") {
                        comp.enabled = false;
                        Debug.Log("[NetworkedCharacter] Disabled RotateWithCamera to maintain PlayerAiming authority.", this);
                        break;
                    }
                }
            }
        }

        public override void OnStartClient() {
            base.OnStartClient();

            RemoveEventSystemsFromPlayerHierarchy("OnStartClient");
            // NOTE: Do NOT apply ownership state here. Let OnOwnershipClient handle it
            // after spawn service confirms proper assignment. This prevents race conditions.
        }

        public override void OnStartServer() {
            base.OnStartServer();
        }

        public override void OnOwnershipClient(FishNet.Connection.NetworkConnection prevOwner) {
            base.OnOwnershipClient(prevOwner);
            // GATE: Defer input enable by one frame to ensure ownership is fully synchronized
            // before collectors respond to input. This prevents race conditions during rapid
            // connection/spawn/ownership assignment sequences.
            StartCoroutine(DelayedApplyOwnershipState());
        }

        private System.Collections.IEnumerator DelayedApplyOwnershipState() {
            yield return null;  // Wait one frame for ownership to settle across all systems
            ApplyOwnershipState();
        }

        private void LateUpdate() {
            if (_enforceCameraOwnershipInvariant)
                EnsureCameraOwnershipInvariant();

            if (_enforceVirtualCameraOwnershipInvariant)
                EnsureVirtualCameraOwnershipInvariant();

            // Apply replicated look only for non-owners to avoid fighting local aiming scripts.
            if (IsClientInitialized && !HasLocalAuthority() && _hasRemoteLook)
                ApplyRemoteLookSmoothed(_remoteYaw, _remotePitch);

            ApplyProxyInterpolationSmoothing();
        }

        public override void OnStartNetwork() {
            base.OnStartNetwork();
            _hasLastTickedPredictionInput = false;
            _lastTickedPredictionInput = default;
            
            // Register tick callback
            if (TimeManager != null) {
                TimeManager.OnTick += TimeManager_OnTick;
            }
        }

        public override void OnStopNetwork() {
            base.OnStopNetwork();
            _hasLastTickedPredictionInput = false;
            _lastTickedPredictionInput = default;
            
            // Unregister tick callback
            if (TimeManager != null) {
                TimeManager.OnTick -= TimeManager_OnTick;
            }

        }

        private void TimeManager_OnTick() {
            bool isClient = IsClientInitialized;
            bool isServer = IsServerInitialized;

            if (!isClient && !isServer)
                return;

            if (_useFishNetPredictionPipeline) {
                if (TryRunFishNetPredictionPipelineOnTick())
                    return;
            }

            SyncMoveDataViewAnglesFromCurrentLook();

            if (isClient && HasLocalAuthority()) {
                // Determine tick frame
                int tick = (int)TimeManager.Tick;
                
                // Get input
                InputFrame input = default;
                if (_inputCollector != null) {
                    input = _inputCollector.GatherInput(tick);
                }

                TrackLocalInputForRedundancy(input);
                SendInputWithRedundancy(tick);

                // Simulate locally
                float dt = (float)TimeManager.TickDelta;
                _rollback.Tick(input, dt);

            } else if (isClient) {
                // Non-owners wait for Observe logic
                // The server receives RPC, simulates, and broadcasts ObserversRpc
            }

            if (isServer)
                TryBroadcastServerState();

        }

        private void TryBroadcastServerState() {
            if (IsFishNetPredictionPipelineActive())
                return;

            // Migration gate: disable legacy snapshot broadcast when transitioning to
            // FishNet prediction/reconcile-driven state convergence.
            if (!_useLegacyAuthoritativeBroadcast)
                return;

            TryBroadcastAuthoritativeStateByServerTick();
        }

        [ServerRpc(RequireOwnership = false)]
        private void SendInputServerRpc(InputFrame input, NetworkConnection sender = null) {
            if (IsFishNetPredictionPipelineActive())
                return;

            // SECURITY: Validate sender identity
            if (sender == null || !sender.IsValid) {
                Debug.LogWarning($"[NetworkedCharacter] Input RPC rejected: Invalid sender. objectId={ObjectId}, frame={input.frame}.", this);
                return;
            }

            // SECURITY: Owner must be assigned before accepting input
            if (!Owner.IsValid) {
                Debug.LogWarning($"[NetworkedCharacter] Input RPC rejected: Owner not yet assigned. objectId={ObjectId}, frame={input.frame}, senderId={sender.ClientId}.", this);
                return;
            }

            // SECURITY: Sender must match owner (client ID check)
            if (sender.ClientId != Owner.ClientId) {
                _ownerMismatchRpcCount++;
                Debug.LogError($"[NetworkedCharacter] INPUT SECURITY VIOLATION: Unauthorized input sender. objectId={ObjectId}, frame={input.frame}, senderId={sender.ClientId}, ownerId={Owner.ClientId}, violationCount={_ownerMismatchRpcCount}", this);
                return;
            }

            // SECURITY: Input must declare the correct character (object ID check from Fix #1)
            if (input.characterObjectId != ObjectId) {
                Debug.LogError($"[NetworkedCharacter] INPUT SECURITY VIOLATION: Input for wrong character. objectId={ObjectId}, input.characterObjectId={input.characterObjectId}, frame={input.frame}.", this);
                return;
            }

            if (_useLegacyFrameValidation) {
                if (!ValidateAndTrackServerInputFrame(input.frame)) {
                    // Conservative hardening: ignore stale/out-of-order input.
                    return;
                }
            }

            // Single input packet timeline: movement + look are consumed on the same authoritative tick.
            _remoteYaw = input.LookYaw;
            _remotePitch = input.LookPitch;
            _hasRemoteLook = true;
            SyncMoveDataViewAngles(_remoteYaw, _remotePitch);
            
            // Track input lag: how far behind server tick is client input?
            // Apply on Server (Skip if we are the owner, as we already simulated in OnTick)
            if (!Owner.IsLocalClient) {
                float dt = (float)TimeManager.TickDelta;
                _rollback.ReceiveRemoteInput(input, dt);
            }

            // Migration gate: legacy proxy movement path uses explicit observer broadcasts.
            if (_useLegacyInputObserverBroadcast)
                BroadcastInputObserversRpc(input);

        }

        private void TryBroadcastAuthoritativeStateByServerTick() {
            if (!_enforceAuthoritativePosition || !IsServerInitialized)
                return;

            int serverTick = (TimeManager != null) ? (int)TimeManager.Tick : 0;
            TryBroadcastAuthoritativeState(serverTick);
        }

        private void TryBroadcastAuthoritativeState(int serverTick) {
            if (!_enforceAuthoritativePosition || !IsServerInitialized)
                return;

            if (_authoritativeSyncIntervalTicks < 1)
                _authoritativeSyncIntervalTicks = 1;

            if (_lastAuthoritativeStateBroadcastTick >= 0) {
                int delta = serverTick - _lastAuthoritativeStateBroadcastTick;
                if (delta < _authoritativeSyncIntervalTicks)
                    return;
            }

            _lastAuthoritativeStateBroadcastTick = serverTick;

            Vector3 position = (_character != null && _character.moveData != null)
                ? _character.moveData.origin
                : transform.position;

            Vector3 velocity = (_character != null && _character.moveData != null)
                ? _character.moveData.velocity
                : Vector3.zero;

            float yaw;
            float pitch;
            GetAuthoritativeLookAngles(out yaw, out pitch);

            MoveData md = (_character != null) ? _character.moveData : null;
            float stamina = (md != null) ? md.stamina : 0f;
            float staminaRegenTimer = (md != null) ? md.staminaRegenTimer : 0f;
            float dashTimer = (md != null) ? md.dashTimer : 0f;
            float currentDashDuration = (md != null) ? md.currentDashDuration : 0f;
            float dashCooldownTimer = (md != null) ? md.dashCooldownTimer : 0f;
            byte isDashing = BoolToByte(md != null && md.isDashing);
            byte canAirDash = BoolToByte(md == null || md.canAirDash);
            int jumpCount = (md != null) ? md.jumpCount : 0;
            float jumpTimer = (md != null) ? md.jumpTimer : 0f;
            byte grounded = BoolToByte(md != null && md.grounded);
            byte underwater = BoolToByte(md != null && md.underwater);
            byte cameraUnderwater = BoolToByte(md != null && md.cameraUnderwater);
            byte climbingLadder = BoolToByte(md != null && md.climbingLadder);
            byte moveType = (byte)((md != null) ? (int)md.moveType : (int)MoveType.Walk);
            byte meleeState = (byte)((md != null) ? (int)md.meleeState : (int)MoveData.MeleeState.None);
            float meleeTimer = (md != null) ? md.meleeTimer : 0f;
            float meleeCooldownTimer = (md != null) ? md.meleeCooldownTimer : 0f;
            float crouchLerp = (md != null) ? md.crouchLerp : 0f;
            byte sliding = BoolToByte(md != null && md.sliding);
            float slideSpeedCurrent = (md != null) ? md.slideSpeedCurrent : 0f;

            BroadcastAuthoritativeStateObserversRpc(serverTick,
                                                    position,
                                                    velocity,
                                                    yaw,
                                                    pitch,
                                                    stamina,
                                                    staminaRegenTimer,
                                                    dashTimer,
                                                    currentDashDuration,
                                                    dashCooldownTimer,
                                                    isDashing,
                                                    canAirDash,
                                                    jumpCount,
                                                    jumpTimer,
                                                    grounded,
                                                    underwater,
                                                    cameraUnderwater,
                                                    climbingLadder,
                                                    moveType,
                                                    meleeState,
                                                    meleeTimer,
                                                    meleeCooldownTimer,
                                                    crouchLerp,
                                                    sliding,
                                                    slideSpeedCurrent);
        }

        [ObserversRpc]
        private void BroadcastAuthoritativeStateObserversRpc(int frame,
                                                             Vector3 position,
                                                             Vector3 velocity,
                                                             float yaw,
                                                             float pitch,
                                                             float stamina,
                                                             float staminaRegenTimer,
                                                             float dashTimer,
                                                             float currentDashDuration,
                                                             float dashCooldownTimer,
                                                             byte isDashing,
                                                             byte canAirDash,
                                                             int jumpCount,
                                                             float jumpTimer,
                                                             byte grounded,
                                                             byte underwater,
                                                             byte cameraUnderwater,
                                                             byte climbingLadder,
                                                             byte moveType,
                                                             byte meleeState,
                                                             float meleeTimer,
                                                             float meleeCooldownTimer,
                                                             float crouchLerp,
                                                             byte sliding,
                                                             float slideSpeedCurrent) {
            if (!IsClientInitialized)
                return;

            if (frame <= _lastAppliedAuthoritativeFrame)
                return;

            // Keep look stream aligned with authoritative snapshots regardless of correction size.
            _remoteYaw = yaw;
            _remotePitch = pitch;
            _hasRemoteLook = true;

            _lastAppliedAuthoritativeFrame = frame;

            bool isLocalOwner = HasLocalAuthority();
            bool shouldApplyGameplayState = !isLocalOwner;
            Vector3 current = (_character != null && _character.moveData != null)
                ? _character.moveData.origin
                : transform.position;

            if (isLocalOwner && !_reconcileOwnerContinuously) {
                return;
            }

            if (isLocalOwner && ShouldDeferOwnerReconcileForBurst()) {
                return;
            }

            // Owner path: reconcile by frame with rollback/replay, not by blending current state
            // toward a delayed snapshot. This preserves burst movement feel under latency.
            if (isLocalOwner) {
                if (_rollback != null && TimeManager != null) {
                    float ownerError = Vector3.Distance(current, position);
                    if (ownerError <= _ownerReconcileNudgeDistance)
                        return;

                    bool ownerDoSnap = ownerError >= _ownerReconcileSnapDistance;
                    Vector3 ownerLocalVelocity = (_character != null && _character.moveData != null) ? _character.moveData.velocity : Vector3.zero;
                    
                    // During burst movement (dash, slide, melee), strongly prefer the authoritative velocity
                    // to avoid position oscillation. The velocity tells us the movement intent better than
                    // a positional snapshot from network latency ago.
                    float velocityBlendFactor = _ownerReconcileBlendFactor;
                    if (IsBurstMovementActiveLocally()) {
                        // For burst movement, trust server velocity more heavily
                        velocityBlendFactor = Mathf.Min(0.6f, _ownerReconcileBlendFactor * 2f);
                    }
                    
                    Vector3 ownerCorrectedPosition = ownerDoSnap
                        ? position
                        : Vector3.Lerp(current, position, _ownerReconcileBlendFactor);
                    Vector3 ownerCorrectedVelocity = ownerDoSnap
                        ? velocity
                        : Vector3.Lerp(ownerLocalVelocity, velocity, velocityBlendFactor);

                    _rollback.ReconcileAuthoritativeFrame(frame, ownerCorrectedPosition, ownerCorrectedVelocity, yaw, pitch, (float)TimeManager.TickDelta);
                }
                return;
            }

            float nudgeDistance = _proxyReconcileNudgeDistance;
            float snapDistance = _proxyReconcileSnapDistance;

            if (!isLocalOwner && _enableProxyInterpolationSmoothing) {
                _hasProxyInterpolationTarget = true;
                _proxyInterpolationTargetPosition = position;
                _proxyInterpolationTargetVelocity = velocity;

                float proxyError = Vector3.Distance(current, position);
                if (proxyError >= _proxyInterpolationSnapDistance) {
                    ApplyAuthoritativeStateLocal(position, velocity, yaw, pitch, frame);
                } else {
                    UpdateProxyMoveDataHints(velocity, yaw, pitch, frame);
                }

                if (shouldApplyGameplayState) {
                    ApplyAuthoritativeGameplayStateLocal(frame,
                                                         stamina,
                                                         staminaRegenTimer,
                                                         dashTimer,
                                                         currentDashDuration,
                                                         dashCooldownTimer,
                                                         isDashing,
                                                         canAirDash,
                                                         jumpCount,
                                                         jumpTimer,
                                                         grounded,
                                                         underwater,
                                                         cameraUnderwater,
                                                         climbingLadder,
                                                         moveType,
                                                         meleeState,
                                                         meleeTimer,
                                                         meleeCooldownTimer,
                                                         crouchLerp,
                                                         sliding,
                                                         slideSpeedCurrent);
                }
                return;
            }

            float error = Vector3.Distance(current, position);
            if (error <= nudgeDistance) {
                if (shouldApplyGameplayState) {
                    ApplyAuthoritativeGameplayStateLocal(frame,
                                                         stamina,
                                                         staminaRegenTimer,
                                                         dashTimer,
                                                         currentDashDuration,
                                                         dashCooldownTimer,
                                                         isDashing,
                                                         canAirDash,
                                                         jumpCount,
                                                         jumpTimer,
                                                         grounded,
                                                         underwater,
                                                         cameraUnderwater,
                                                         climbingLadder,
                                                         moveType,
                                                         meleeState,
                                                         meleeTimer,
                                                         meleeCooldownTimer,
                                                         crouchLerp,
                                                         sliding,
                                                         slideSpeedCurrent);
                }
                return;
            }

            bool doSnap = error >= snapDistance;
            Vector3 correctedPosition = doSnap ? position : Vector3.Lerp(current, position, _reconcileBlendFactor);
            Vector3 localVelocity = (_character != null && _character.moveData != null) ? _character.moveData.velocity : Vector3.zero;
            Vector3 correctedVelocity = doSnap
                ? velocity
                : Vector3.Lerp(localVelocity, velocity, _reconcileBlendFactor);

            ApplyAuthoritativeStateLocal(correctedPosition, correctedVelocity, yaw, pitch, frame);
            if (shouldApplyGameplayState) {
                ApplyAuthoritativeGameplayStateLocal(frame,
                                                     stamina,
                                                     staminaRegenTimer,
                                                     dashTimer,
                                                     currentDashDuration,
                                                     dashCooldownTimer,
                                                     isDashing,
                                                     canAirDash,
                                                     jumpCount,
                                                     jumpTimer,
                                                     grounded,
                                                     underwater,
                                                     cameraUnderwater,
                                                     climbingLadder,
                                                     moveType,
                                                     meleeState,
                                                     meleeTimer,
                                                     meleeCooldownTimer,
                                                     crouchLerp,
                                                     sliding,
                                                     slideSpeedCurrent);
            }
        }

        private static byte BoolToByte(bool value) {
            return (byte)(value ? 1 : 0);
        }

        private static bool ByteToBool(byte value) {
            return value != 0;
        }

        private void ApplyAuthoritativeGameplayStateLocal(int frame,
                                                          float stamina,
                                                          float staminaRegenTimer,
                                                          float dashTimer,
                                                          float currentDashDuration,
                                                          float dashCooldownTimer,
                                                          byte isDashing,
                                                          byte canAirDash,
                                                          int jumpCount,
                                                          float jumpTimer,
                                                          byte grounded,
                                                          byte underwater,
                                                          byte cameraUnderwater,
                                                          byte climbingLadder,
                                                          byte moveType,
                                                          byte meleeState,
                                                          float meleeTimer,
                                                          float meleeCooldownTimer,
                                                          float crouchLerp,
                                                          byte sliding,
                                                          float slideSpeedCurrent) {
            if (_character == null)
                return;

            MoveData state = _character.moveData ?? new MoveData();
            state.frame = Mathf.Max(state.frame, frame);

            state.stamina = stamina;
            state.staminaRegenTimer = staminaRegenTimer;
            state.dashTimer = dashTimer;
            state.currentDashDuration = currentDashDuration;
            state.dashCooldownTimer = dashCooldownTimer;
            state.isDashing = ByteToBool(isDashing);
            state.canAirDash = ByteToBool(canAirDash);
            state.jumpCount = jumpCount;
            state.jumpTimer = jumpTimer;

            state.grounded = ByteToBool(grounded);
            state.underwater = ByteToBool(underwater);
            state.cameraUnderwater = ByteToBool(cameraUnderwater);
            state.climbingLadder = ByteToBool(climbingLadder);

            state.moveType = (MoveType)Mathf.Clamp(moveType, (byte)MoveType.None, (byte)MoveType.HeavyMelee);
            state.meleeState = (MoveData.MeleeState)Mathf.Clamp(meleeState, (byte)MoveData.MeleeState.None, (byte)MoveData.MeleeState.Recovery);
            state.meleeTimer = meleeTimer;
            state.meleeCooldownTimer = meleeCooldownTimer;

            state.crouchLerp = crouchLerp;
            state.sliding = ByteToBool(sliding);
            state.slideSpeedCurrent = slideSpeedCurrent;

            _character.moveData = state;
        }

        private bool IsBurstMovementActiveLocally() {
            if (_character == null || _character.moveData == null)
                return false;

            MoveData state = _character.moveData;
            if (state.isDashing)
                return true;
            if (state.sliding)
                return true;
            if (state.moveType == MoveType.HeavyMelee)
                return true;
            if (state.wishDash)
                return true;
            if (state.wishMelee)
                return true;
            if (state.wishJumpDown)
                return true;
            if (state.crouching)
                return true;

            return false;
        }

        private bool ShouldDeferOwnerReconcileForBurst() {
            if (!_ownerSkipReconcileDuringBurstMovement) {
                _ownerBurstReconcileSkippedCount = 0;
                return false;
            }

            if (!IsBurstMovementActiveLocally()) {
                _ownerBurstReconcileSkippedCount = 0;
                return false;
            }

            int limit = Mathf.Max(0, _ownerBurstReconcileSkipLimit);
            if (limit == 0)
                return false;

            if (_ownerBurstReconcileSkippedCount < limit) {
                _ownerBurstReconcileSkippedCount++;
                return true;
            }

            _ownerBurstReconcileSkippedCount = 0;
            return false;
        }

        private void ApplyAuthoritativeStateLocal(Vector3 position, Vector3 velocity, float yaw, float pitch, int frame) {
            transform.position = position;

            if (_character != null) {
                MoveData state = _character.moveData ?? new MoveData();
                state.origin = position;
                state.velocity = velocity;
                state.frame = Mathf.Max(state.frame, frame);
                state.viewAngles.y = yaw;
                state.viewAngles.x = pitch;

                _character.moveData = state;
                _character.LoadState(state);
            }

            bool shouldApplyImmediateLook = !IsClientInitialized || HasLocalAuthority();
            if (shouldApplyImmediateLook)
                ApplyLookRotation(yaw, pitch);

            _remoteYaw = yaw;
            _remotePitch = pitch;
            _hasRemoteLook = true;

            _hasProxyInterpolationTarget = true;
            _proxyInterpolationTargetPosition = position;
            _proxyInterpolationTargetVelocity = velocity;
            _proxyInterpolationVelocityRef = Vector3.zero;
        }

        private void UpdateProxyMoveDataHints(Vector3 targetVelocity, float yaw, float pitch, int frame) {
            if (_character == null)
                return;

            MoveData state = _character.moveData ?? new MoveData();
            state.frame = Mathf.Max(state.frame, frame);
            
            // Detect if the remote player is doing burst movement
            // If so, apply velocity updates more aggressively for better responsiveness
            bool isRemoteBursting = (state.isDashing || state.sliding || state.moveType == MoveType.HeavyMelee);
            float velocityBlendSpeed = isRemoteBursting 
                ? _proxyVelocityBlendSpeed * 1.5f  // 50% faster during burst movement
                : _proxyVelocityBlendSpeed;
            
            state.velocity = Vector3.Lerp(state.velocity, targetVelocity, Time.deltaTime * velocityBlendSpeed);
            state.viewAngles.y = yaw;
            state.viewAngles.x = pitch;
            _character.moveData = state;
        }

        private void ApplyProxyInterpolationSmoothing() {
            if (!_enableProxyInterpolationSmoothing || !_hasProxyInterpolationTarget)
                return;

            if (!IsClientInitialized || HasLocalAuthority())
                return;

            Vector3 currentPosition = (_character != null && _character.moveData != null)
                ? _character.moveData.origin
                : transform.position;

            Vector3 targetPosition = _proxyInterpolationTargetPosition;
            float distance = Vector3.Distance(currentPosition, targetPosition);

            Vector3 smoothedPosition = distance >= _proxyInterpolationSnapDistance
                ? targetPosition
                : Vector3.SmoothDamp(currentPosition,
                                     targetPosition,
                                     ref _proxyInterpolationVelocityRef,
                                     _proxyInterpolationSmoothTime,
                                     _proxyInterpolationMaxSpeed,
                                     Time.deltaTime);

            transform.position = smoothedPosition;

            if (_character != null) {
                MoveData state = _character.moveData ?? new MoveData();
                state.origin = smoothedPosition;
                state.velocity = Vector3.Lerp(state.velocity, _proxyInterpolationTargetVelocity, Time.deltaTime * _proxyVelocityBlendSpeed);
                _character.moveData = state;
            }
        }

        private bool ValidateAndTrackServerInputFrame(int frame) {
            int frameSlot = PositiveMod(frame, SERVER_INPUT_HISTORY_SLOTS);
            if (_serverAcceptedInputFrames[frameSlot] == frame)
                return false;

            if (_lastServerReceivedFrame >= 0) {
                int delta = frame - _lastServerReceivedFrame;
                if (delta == 0) {
                    return false;
                }

                if (delta < 0) {
                    // Accept late/out-of-order frames while they are still within rollback history.
                    // This is required for edge-trigger actions (dash/jump press) to be recovered.
                    int rollbackTick = (_rollback != null) ? _rollback.CurrentTick : _lastServerReceivedFrame;
                    const int rollbackWindow = 255;
                    bool tooOld = frame < (rollbackTick - rollbackWindow);
                    if (tooOld) {
                        return false;
                    }
                }
            }

            if (frame > _lastServerReceivedFrame)
                _lastServerReceivedFrame = frame;

            _serverAcceptedInputFrames[frameSlot] = frame;

            return true;
        }

        private void InitializeInputFrameTracking() {
            for (int i = 0; i < _serverAcceptedInputFrames.Length; i++)
                _serverAcceptedInputFrames[i] = int.MinValue;

            for (int i = 0; i < _clientInputHistoryFrames.Length; i++)
                _clientInputHistoryFrames[i] = int.MinValue;
        }

        private void TrackLocalInputForRedundancy(InputFrame input) {
            int slot = PositiveMod(input.frame, CLIENT_INPUT_HISTORY_SLOTS);
            _clientInputHistory[slot] = input;
            _clientInputHistoryFrames[slot] = input.frame;
        }

        private void SendInputWithRedundancy(int currentFrame) {
            int resendFrames = Mathf.Max(0, _inputRedundancyFrames);
            int startFrame = Mathf.Max(0, currentFrame - resendFrames);

            for (int frame = startFrame; frame <= currentFrame; frame++) {
                int slot = PositiveMod(frame, CLIENT_INPUT_HISTORY_SLOTS);
                if (_clientInputHistoryFrames[slot] != frame)
                    continue;

                SendInputServerRpc(_clientInputHistory[slot]);
            }
        }

        private static int PositiveMod(int value, int modulus) {
            int result = value % modulus;
            return (result < 0) ? (result + modulus) : result;
        }

        [ObserversRpc(ExcludeOwner = true)]
        private void BroadcastInputObserversRpc(InputFrame input) {
            if (IsFishNetPredictionPipelineActive())
                return;

            // SECURITY: Verify input belongs to this character
            if (input.characterObjectId != ObjectId) {
                Debug.LogError($"[NetworkedCharacter] INPUT SECURITY VIOLATION: Input received for wrong character. Expected objectId={ObjectId}, but input has characterObjectId={input.characterObjectId}, frame={input.frame}", this);
                return;
            }

            // Apply on other clients
            float dt = (float)TimeManager.TickDelta;
            _rollback.ReceiveRemoteInput(input, dt);
        }

        private void SyncMoveDataViewAnglesFromCurrentLook() {
            if (!TryGetLook(out float yaw, out float pitch))
                return;

            SyncMoveDataViewAngles(yaw, pitch);
        }

        private void SyncMoveDataViewAngles(float yaw, float pitch) {
            if (_character == null || _character.moveData == null)
                return;

            MoveData state = _character.moveData;
            state.viewAngles.y = yaw;
            state.viewAngles.x = pitch;
            _character.moveData = state;
        }

        private void GetAuthoritativeLookAngles(out float yaw, out float pitch) {
            yaw = transform.eulerAngles.y;
            pitch = 0f;

            if (_character != null && _character.moveData != null) {
                yaw = _character.moveData.viewAngles.y;
                pitch = _character.moveData.viewAngles.x;
                return;
            }

            TryGetLook(out yaw, out pitch);
        }

        /// <summary>
        /// Server-authoritative spawn pose application.
        /// Applies immediately on server and replicates to all observers (including owner).
        /// </summary>
        public void ApplyAuthoritativeSpawnPoseServer(Vector3 position, Quaternion rotation) {
            if (!IsServerInitialized)
                return;

            ApplyAuthoritativeSpawnPoseLocal(position, rotation);
            ApplyAuthoritativeSpawnPoseObserversRpc(position, rotation);
        }

        [ObserversRpc]
        private void ApplyAuthoritativeSpawnPoseObserversRpc(Vector3 position, Quaternion rotation) {
            ApplyAuthoritativeSpawnPoseLocal(position, rotation);
        }

        private void ApplyAuthoritativeSpawnPoseLocal(Vector3 position, Quaternion rotation) {
            transform.SetPositionAndRotation(position, rotation);

            float yaw = rotation.eulerAngles.y;
            float pitch = 0f;

            if (_character != null) {
                MoveData state = _character.moveData ?? new MoveData();
                state.origin = position;
                state.velocity = Vector3.zero;
                state.viewAngles.y = yaw;

                _character.moveData = state;
                _character.LoadState(state);

                if (_character.viewTransform != null)
                    pitch = NormalizeSignedAngle(_character.viewTransform.eulerAngles.x);
            }

            ApplyLookRotation(yaw, pitch);

            // Keep remote-look stream aligned with authoritative spawn orientation.
            _remoteYaw = yaw;
            _remotePitch = pitch;
            _hasRemoteLook = true;

            // Force immediate server snapshot so everyone converges right after spawn.
            if (IsServerInitialized) {
                _lastAuthoritativeStateBroadcastTick = -1;
                int serverTick = (TimeManager != null) ? (int)TimeManager.Tick : (_character != null && _character.moveData != null ? _character.moveData.frame : 0);
                TryBroadcastAuthoritativeState(serverTick);
            }
        }

        private bool TryGetLook(out float yaw, out float pitch) {
            yaw = 0f;
            pitch = 0f;

            Transform yawTransform = (_playerAiming != null && _playerAiming.bodyTransform != null)
                ? _playerAiming.bodyTransform
                : transform;
            yaw = yawTransform.eulerAngles.y;

            if (_character != null && _character.viewTransform != null)
                pitch = NormalizeSignedAngle(_character.viewTransform.eulerAngles.x);

            return true;
        }

        private void ApplyLookRotation(float yaw, float pitch) {
            Transform yawTransform = (_playerAiming != null && _playerAiming.bodyTransform != null)
                ? _playerAiming.bodyTransform
                : transform;

            Vector3 bodyEuler = yawTransform.eulerAngles;
            bodyEuler.y = yaw;
            yawTransform.eulerAngles = bodyEuler;

            if (_character != null && _character.viewTransform != null) {
                Vector3 viewEuler = _character.viewTransform.eulerAngles;
                viewEuler.x = pitch;
                viewEuler.y = yaw;
                _character.viewTransform.eulerAngles = viewEuler;
            }
        }

        private void ApplyRemoteLookSmoothed(float yaw, float pitch) {
            Transform yawTransform = (_playerAiming != null && _playerAiming.bodyTransform != null)
                ? _playerAiming.bodyTransform
                : transform;

            Vector3 bodyEuler = yawTransform.eulerAngles;
            float currentYaw = bodyEuler.y;
            bodyEuler.y = Mathf.LerpAngle(currentYaw, yaw, Time.deltaTime * _lookApplySpeed);
            yawTransform.eulerAngles = bodyEuler;

            if (_character != null && _character.viewTransform != null) {
                Vector3 viewEuler = _character.viewTransform.eulerAngles;
                float currentPitch = NormalizeSignedAngle(viewEuler.x);
                viewEuler.x = Mathf.LerpAngle(currentPitch, pitch, Time.deltaTime * _lookApplySpeed);
                viewEuler.y = bodyEuler.y;
                _character.viewTransform.eulerAngles = viewEuler;
            }
        }

        private void OnValidate() {
            _authoritativeSyncIntervalTicks = Mathf.Max(1, _authoritativeSyncIntervalTicks);
            _lookApplySpeed = Mathf.Max(0f, _lookApplySpeed);
            _ownerReconcileNudgeDistance = Mathf.Max(0f, _ownerReconcileNudgeDistance);
            _ownerReconcileSnapDistance = Mathf.Max(_ownerReconcileNudgeDistance, _ownerReconcileSnapDistance);
            _ownerReconcileBlendFactor = Mathf.Clamp01(_ownerReconcileBlendFactor);
            _ownerBurstReconcileSkipLimit = Mathf.Max(0, _ownerBurstReconcileSkipLimit);
            _proxyReconcileNudgeDistance = Mathf.Max(0f, _proxyReconcileNudgeDistance);
            _proxyReconcileSnapDistance = Mathf.Max(_proxyReconcileNudgeDistance, _proxyReconcileSnapDistance);
            _reconcileBlendFactor = Mathf.Clamp01(_reconcileBlendFactor);
            _inputRedundancyFrames = Mathf.Max(0, _inputRedundancyFrames);
            _proxyInterpolationSmoothTime = Mathf.Max(0f, _proxyInterpolationSmoothTime);
            _proxyInterpolationMaxSpeed = Mathf.Max(0f, _proxyInterpolationMaxSpeed);
            _proxyInterpolationSnapDistance = Mathf.Max(0f, _proxyInterpolationSnapDistance);
            _proxyVelocityBlendSpeed = Mathf.Max(0f, _proxyVelocityBlendSpeed);

            if (_useFishNetPredictionPipeline) {
                _useLegacyAuthoritativeBroadcast = false;
                _useLegacyInputObserverBroadcast = false;
                _useLegacyFrameValidation = false;
            }

            // Safety: avoid dead character states by ensuring at least one movement sync path
            // remains active until full prediction/reconcile migration is complete.
            if (!_useFishNetPredictionPipeline && !_useLegacyAuthoritativeBroadcast && !_useLegacyInputObserverBroadcast) {
                _useLegacyAuthoritativeBroadcast = true;
                Debug.LogWarning("[NetworkedCharacter] Both legacy sync gates were disabled. Re-enabling legacy authoritative broadcast to preserve movement sync.", this);
            }
        }

        /// <summary>
        /// FishNet prediction tick route.
        /// Returns true when prediction pipeline handled this tick and legacy flow should be skipped.
        /// </summary>
        private bool TryRunFishNetPredictionPipelineOnTick() {
            if (!IsFishNetPredictionPipelineActive())
                return false;

            int tick = (TimeManager != null) ? (int)TimeManager.Tick : 0;

            PredictionReplicateData rd;
            if (HasLocalAuthority() && _inputCollector != null) {
                InputFrame localInput = _inputCollector.GatherInput(tick);
                rd = new PredictionReplicateData(localInput);
            } else {
                InputFrame fallback = default;
                fallback.frame = tick;
                fallback.characterObjectId = ObjectId;
                if (TryGetLook(out float yaw, out float pitch)) {
                    fallback.lookYaw100 = (short)Mathf.RoundToInt(yaw * 100f);
                    fallback.lookPitch100 = (short)Mathf.RoundToInt(pitch * 100f);
                }
                rd = new PredictionReplicateData(fallback);
            }

            PerformPredictionReplicate(rd);

            return true;
        }

        private bool IsFishNetPredictionPipelineActive() {
            return _useFishNetPredictionPipeline;
        }

        [Replicate]
        private void PerformPredictionReplicate(PredictionReplicateData rd,
                                                ReplicateState state = ReplicateState.Invalid,
                                                Channel channel = Channel.Unreliable) {
            if (!IsFishNetPredictionPipelineActive())
                return;

            if (_rollback == null || TimeManager == null)
                return;

            InputFrame input = rd.Input;
            int replicateTick = (int)rd.GetTick();
            if (replicateTick > 0) {
                input.frame = replicateTick;
            } else if (input.frame < 0) {
                input.frame = (TimeManager != null) ? (int)TimeManager.Tick : 0;
            }

            bool hasCreatedData = state.ContainsCreated();
            if (!hasCreatedData && _hasLastTickedPredictionInput) {
                // Future prediction path: preserve held input state so actions which rely on hold
                // (slide, melee charge) continue correctly when an input packet is missing.
                // Do not preserve edge flags to avoid repeated one-frame actions.
                InputFrame predicted = _lastTickedPredictionInput;
                predicted.frame = input.frame;
                predicted.justPressed = 0;
                input = predicted;
            }

            if (state.ContainsTicked()) {
                _lastTickedPredictionInput = input;
                _hasLastTickedPredictionInput = true;
            }

            if (input.characterObjectId <= 0)
                input.characterObjectId = ObjectId;

            if (input.characterObjectId != ObjectId)
                return;

            _remoteYaw = input.LookYaw;
            _remotePitch = input.LookPitch;
            _hasRemoteLook = true;

            SyncMoveDataViewAngles(_remoteYaw, _remotePitch);

            float dt = (float)TimeManager.TickDelta;
            _rollback.Tick(input, dt);
        }

        private PredictionReconcileData BuildPredictionReconcileData() {
            int frame = (TimeManager != null) ? (int)TimeManager.Tick : 0;

            Vector3 position = (_character != null && _character.moveData != null)
                ? _character.moveData.origin
                : transform.position;

            Vector3 velocity = (_character != null && _character.moveData != null)
                ? _character.moveData.velocity
                : Vector3.zero;

            float yaw;
            float pitch;
            GetAuthoritativeLookAngles(out yaw, out pitch);

            MoveData md = (_character != null) ? _character.moveData : null;
            float stamina = (md != null) ? md.stamina : 0f;
            float staminaRegenTimer = (md != null) ? md.staminaRegenTimer : 0f;
            float dashTimer = (md != null) ? md.dashTimer : 0f;
            float currentDashDuration = (md != null) ? md.currentDashDuration : 0f;
            float dashCooldownTimer = (md != null) ? md.dashCooldownTimer : 0f;
            byte isDashing = BoolToByte(md != null && md.isDashing);
            byte canAirDash = BoolToByte(md == null || md.canAirDash);
            int jumpCount = (md != null) ? md.jumpCount : 0;
            float jumpTimer = (md != null) ? md.jumpTimer : 0f;
            byte grounded = BoolToByte(md != null && md.grounded);
            byte underwater = BoolToByte(md != null && md.underwater);
            byte cameraUnderwater = BoolToByte(md != null && md.cameraUnderwater);
            byte climbingLadder = BoolToByte(md != null && md.climbingLadder);
            byte moveType = (byte)((md != null) ? (int)md.moveType : (int)MoveType.Walk);
            byte meleeState = (byte)((md != null) ? (int)md.meleeState : (int)MoveData.MeleeState.None);
            float meleeTimer = (md != null) ? md.meleeTimer : 0f;
            float meleeCooldownTimer = (md != null) ? md.meleeCooldownTimer : 0f;
            float crouchLerp = (md != null) ? md.crouchLerp : 0f;
            byte sliding = BoolToByte(md != null && md.sliding);
            float slideSpeedCurrent = (md != null) ? md.slideSpeedCurrent : 0f;

            PredictionReconcileData rd = new PredictionReconcileData(frame,
                                                                     position,
                                                                     velocity,
                                                                     yaw,
                                                                     pitch,
                                                                     stamina,
                                                                     staminaRegenTimer,
                                                                     dashTimer,
                                                                     currentDashDuration,
                                                                     dashCooldownTimer,
                                                                     isDashing,
                                                                     canAirDash,
                                                                     jumpCount,
                                                                     jumpTimer,
                                                                     grounded,
                                                                     underwater,
                                                                     cameraUnderwater,
                                                                     climbingLadder,
                                                                     moveType,
                                                                     meleeState,
                                                                     meleeTimer,
                                                                     meleeCooldownTimer,
                                                                     crouchLerp,
                                                                     sliding,
                                                                     slideSpeedCurrent);

            return rd;
        }

        [Reconcile]
        private void PerformPredictionReconcile(PredictionReconcileData rd, Channel channel = Channel.Unreliable) {
            if (!IsFishNetPredictionPipelineActive())
                return;

            if (!IsClientInitialized)
                return;

            int frame = rd.Frame;
            if (frame <= _lastAppliedAuthoritativeFrame)
                return;

            _remoteYaw = rd.Yaw;
            _remotePitch = rd.Pitch;
            _hasRemoteLook = true;
            _lastAppliedAuthoritativeFrame = frame;

            bool isLocalOwner = HasLocalAuthority();
            bool shouldApplyGameplayState = !isLocalOwner;
            Vector3 current = (_character != null && _character.moveData != null)
                ? _character.moveData.origin
                : transform.position;

            if (isLocalOwner && !_reconcileOwnerContinuously)
                return;

            if (isLocalOwner && ShouldDeferOwnerReconcileForBurst())
                return;

            if (isLocalOwner) {
                if (_rollback != null && TimeManager != null) {
                    float ownerError = Vector3.Distance(current, rd.Position);
                    if (ownerError <= _ownerReconcileNudgeDistance)
                        return;

                    bool ownerDoSnap = ownerError >= _ownerReconcileSnapDistance;
                    Vector3 ownerLocalVelocity = (_character != null && _character.moveData != null) ? _character.moveData.velocity : Vector3.zero;
                    Vector3 ownerCorrectedPosition = ownerDoSnap
                        ? rd.Position
                        : Vector3.Lerp(current, rd.Position, _ownerReconcileBlendFactor);
                    Vector3 ownerCorrectedVelocity = ownerDoSnap
                        ? rd.Velocity
                        : Vector3.Lerp(ownerLocalVelocity, rd.Velocity, _ownerReconcileBlendFactor);

                    _rollback.ReconcileAuthoritativeFrame(frame, ownerCorrectedPosition, ownerCorrectedVelocity, rd.Yaw, rd.Pitch, (float)TimeManager.TickDelta);
                }
                return;
            }

            if (_enableProxyInterpolationSmoothing) {
                _hasProxyInterpolationTarget = true;
                _proxyInterpolationTargetPosition = rd.Position;
                _proxyInterpolationTargetVelocity = rd.Velocity;

                float proxyError = Vector3.Distance(current, rd.Position);
                if (proxyError >= _proxyInterpolationSnapDistance) {
                    ApplyAuthoritativeStateLocal(rd.Position, rd.Velocity, rd.Yaw, rd.Pitch, frame);
                } else {
                    UpdateProxyMoveDataHints(rd.Velocity, rd.Yaw, rd.Pitch, frame);
                }

                if (shouldApplyGameplayState) {
                    ApplyAuthoritativeGameplayStateLocal(frame,
                                                         rd.Stamina,
                                                         rd.StaminaRegenTimer,
                                                         rd.DashTimer,
                                                         rd.CurrentDashDuration,
                                                         rd.DashCooldownTimer,
                                                         rd.IsDashing,
                                                         rd.CanAirDash,
                                                         rd.JumpCount,
                                                         rd.JumpTimer,
                                                         rd.Grounded,
                                                         rd.Underwater,
                                                         rd.CameraUnderwater,
                                                         rd.ClimbingLadder,
                                                         rd.MoveType,
                                                         rd.MeleeState,
                                                         rd.MeleeTimer,
                                                         rd.MeleeCooldownTimer,
                                                         rd.CrouchLerp,
                                                         rd.Sliding,
                                                         rd.SlideSpeedCurrent);
                }
                return;
            }

            float error = Vector3.Distance(current, rd.Position);
            if (error <= _proxyReconcileNudgeDistance) {
                if (shouldApplyGameplayState) {
                    ApplyAuthoritativeGameplayStateLocal(frame,
                                                         rd.Stamina,
                                                         rd.StaminaRegenTimer,
                                                         rd.DashTimer,
                                                         rd.CurrentDashDuration,
                                                         rd.DashCooldownTimer,
                                                         rd.IsDashing,
                                                         rd.CanAirDash,
                                                         rd.JumpCount,
                                                         rd.JumpTimer,
                                                         rd.Grounded,
                                                         rd.Underwater,
                                                         rd.CameraUnderwater,
                                                         rd.ClimbingLadder,
                                                         rd.MoveType,
                                                         rd.MeleeState,
                                                         rd.MeleeTimer,
                                                         rd.MeleeCooldownTimer,
                                                         rd.CrouchLerp,
                                                         rd.Sliding,
                                                         rd.SlideSpeedCurrent);
                }
                return;
            }

            bool doSnap = error >= _proxyReconcileSnapDistance;
            Vector3 correctedPosition = doSnap ? rd.Position : Vector3.Lerp(current, rd.Position, _reconcileBlendFactor);
            Vector3 localVelocity = (_character != null && _character.moveData != null) ? _character.moveData.velocity : Vector3.zero;
            Vector3 correctedVelocity = doSnap
                ? rd.Velocity
                : Vector3.Lerp(localVelocity, rd.Velocity, _reconcileBlendFactor);

            ApplyAuthoritativeStateLocal(correctedPosition, correctedVelocity, rd.Yaw, rd.Pitch, frame);
            if (shouldApplyGameplayState) {
                ApplyAuthoritativeGameplayStateLocal(frame,
                                                     rd.Stamina,
                                                     rd.StaminaRegenTimer,
                                                     rd.DashTimer,
                                                     rd.CurrentDashDuration,
                                                     rd.DashCooldownTimer,
                                                     rd.IsDashing,
                                                     rd.CanAirDash,
                                                     rd.JumpCount,
                                                     rd.JumpTimer,
                                                     rd.Grounded,
                                                     rd.Underwater,
                                                     rd.CameraUnderwater,
                                                     rd.ClimbingLadder,
                                                     rd.MoveType,
                                                     rd.MeleeState,
                                                     rd.MeleeTimer,
                                                     rd.MeleeCooldownTimer,
                                                     rd.CrouchLerp,
                                                     rd.Sliding,
                                                     rd.SlideSpeedCurrent);
            }
        }

        public override void CreateReconcile() {
            if (!IsFishNetPredictionPipelineActive())
                return;

            if (!IsServerInitialized)
                return;

            PredictionReconcileData rd = BuildPredictionReconcileData();
            PerformPredictionReconcile(rd);
        }

        private void ApplyOwnershipState() {
            bool isLocalOwner = HasLocalAuthority();

            if (_inputCollector != null)
                _inputCollector.enabled = isLocalOwner;

            if (_playerAiming != null)
                _playerAiming.enabled = isLocalOwner;

            if (isLocalOwner) {
                _hasProxyInterpolationTarget = false;
                _proxyInterpolationVelocityRef = Vector3.zero;
                _ownerBurstReconcileSkippedCount = 0;
            }

            ApplyVirtualCameraOwnershipState(isLocalOwner);

            if (_character != null && _character.viewTransform != null) {
                Camera cam = _character.viewTransform.GetComponentInChildren<Camera>(true);
                if (cam != null) {
                    cam.enabled = isLocalOwner;
                    cam.tag = isLocalOwner ? MAIN_CAMERA_TAG : UNTAGGED;
                }

                AudioListener listener = _character.viewTransform.GetComponentInChildren<AudioListener>(true);
                if (listener != null) listener.enabled = isLocalOwner;
            }

            if (_enforceCameraOwnershipInvariant)
                EnsureCameraOwnershipInvariant();

            if (_enforceVirtualCameraOwnershipInvariant)
                EnsureVirtualCameraOwnershipInvariant();
        }

        private void EnsureCameraOwnershipInvariant() {
            if (_character == null || _character.viewTransform == null)
                return;

            if (!Owner.IsValid || LocalConnection == null || !LocalConnection.IsValid)
                return;

            bool shouldEnable = HasLocalAuthority();

            Camera cam = _character.viewTransform.GetComponentInChildren<Camera>(true);
            AudioListener listener = _character.viewTransform.GetComponentInChildren<AudioListener>(true);

            if (cam != null && cam.enabled != shouldEnable) {
                cam.enabled = shouldEnable;
            }

            if (cam != null) {
                string expectedTag = shouldEnable ? MAIN_CAMERA_TAG : UNTAGGED;
                if (cam.tag != expectedTag)
                    cam.tag = expectedTag;
            }

            if (listener != null && listener.enabled != shouldEnable) {
                listener.enabled = shouldEnable;
            }
        }

        private void EnsureVirtualCameraOwnershipInvariant() {
            if (_playerAiming == null || _playerAiming.freeLookCamera == null)
                return;

            if (!Owner.IsValid || LocalConnection == null || !LocalConnection.IsValid)
                return;

            bool shouldEnable = HasLocalAuthority();
            ApplyVirtualCameraOwnershipState(shouldEnable);
        }

        private void ApplyVirtualCameraOwnershipState(bool shouldEnable) {
            if (_playerAiming == null || _playerAiming.freeLookCamera == null)
                return;

            var vcam = _playerAiming.freeLookCamera;
            bool isChildCamera = vcam.transform.IsChildOf(transform);

            // Do not force shared/global virtual cameras.
            if (!isChildCamera)
                return;

            if (vcam.gameObject.activeSelf != shouldEnable)
                vcam.gameObject.SetActive(shouldEnable);

            if (vcam.enabled != shouldEnable)
                vcam.enabled = shouldEnable;
        }



        private bool HasLocalAuthority() {
            if (!IsOwner)
                return false;

            if (!Owner.IsValid || LocalConnection == null || !LocalConnection.IsValid)
                return false;

            return Owner.ClientId == LocalConnection.ClientId;
        }

        private void RemoveEventSystemsFromPlayerHierarchy(string phase) {
            if (!_removeEventSystemsFromPlayerHierarchy)
                return;

            EventSystem[] eventSystems = GetComponentsInChildren<EventSystem>(true);
            if (eventSystems == null || eventSystems.Length == 0)
                return;

            int removed = 0;
            for (int i = 0; i < eventSystems.Length; i++) {
                EventSystem es = eventSystems[i];
                if (es == null)
                    continue;

                BaseInputModule[] modules = es.GetComponents<BaseInputModule>();
                for (int j = 0; j < modules.Length; j++) {
                    if (modules[j] != null)
                        Destroy(modules[j]);
                }

                Destroy(es);
                removed++;
            }

            if (removed > 0) {
                Debug.LogWarning($"[NetworkedCharacter] Removed {removed} EventSystem component(s) from networked player hierarchy during {phase}. Scene-level EventSystem should be unique.", this);
            }
        }

        private static float NormalizeSignedAngle(float angle) {
            angle %= 360f;
            if (angle > 180f)
                angle -= 360f;
            return angle;
        }
    }
}
