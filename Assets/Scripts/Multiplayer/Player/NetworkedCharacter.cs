using FishNet.Connection;
using FishNet.Managing.Timing;
using FishNet.Object;
using FishNet.Object.Prediction;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using Fragsurf.Combat;
using GAUNTLET.Networking;
using GAUNTLET.UI;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Fragsurf.Movement {

    public class NetworkedCharacter : NetworkBehaviour {
        [SerializeField] private SurfCharacter _character;
        [SerializeField] private RollbackManager _rollback;
        [SerializeField] private LocalInputCollector _inputCollector;
        [SerializeField] private PlayerAiming _playerAiming;

        [Header("UI/Input Ownership Guard")]
        [SerializeField] private bool _removeEventSystemsFromPlayerHierarchy = true;

        [Header("Camera Ownership")]
        [SerializeField] private bool _enforceCameraOwnershipInvariant = true;
        [SerializeField] private bool _enforceVirtualCameraOwnershipInvariant = true;

        [Header("Combat/Health")]
        [SerializeField] private int _maxHealth = 3;
        [SerializeField] private float _respawnDelaySeconds = 0f;
        [SerializeField] private bool _combatDebugLogging = true;

        private NetworkedCharacterOwnershipGateController _ownershipGate;
        private NetworkedCharacterCameraOwnershipGuard _cameraOwnershipGuard;
        private NetworkedCharacterPredictionReconcileService _predictionReconcileService;
        private readonly SyncVar<int> _currentHealth = new();
        private InputFrame _lastReplicatedInput;
        private bool _hasLastReplicatedInput;
        private bool _hurtboxSubscribed;
        private bool _respawnRoutineRunning;
        private PlayerHealthBillboard _healthBillboard;

        public bool UseFishNetPredictionPipeline => NetworkObject != null && NetworkObject.EnablePrediction;
        public bool UseLegacyAuthoritativeBroadcast => false;
        public bool UseLegacyInputObserverBroadcast => false;
        public bool UseLegacyFrameValidation => false;
        public bool DebugHasLocalAuthority => HasLocalAuthority();
        public int RollbackTick => (_rollback != null) ? _rollback.CurrentTick : InputFrame.InvalidFrame;
        public int RollbackCount => (_rollback != null) ? _rollback.RollbackCount : 0;
        public int LastCorrectedTick => (_rollback != null) ? _rollback.LastCorrectedTick : InputFrame.InvalidFrame;
        internal CorrectionDecision LastCorrectionDecision => (_predictionReconcileService != null)
            ? _predictionReconcileService.Diagnostics.LastDecision
            : CorrectionDecision.Ignore;
        public string LastCorrectionPrimaryReason => (_predictionReconcileService != null)
            ? _predictionReconcileService.Diagnostics.LastPrimaryReason
            : string.Empty;
        public string LastCorrectionSummary => (_predictionReconcileService != null)
            ? _predictionReconcileService.Diagnostics.LastSummary
            : string.Empty;
        public float LastCorrectionWeightedScore => (_predictionReconcileService != null)
            ? _predictionReconcileService.Diagnostics.LastWeightedScore
            : 0f;
        public int CorrectionObserveStreak => (_predictionReconcileService != null)
            ? _predictionReconcileService.Diagnostics.ConsecutiveObserveCount
            : 0;
        public int ReplicateTicksProcessed => (_predictionReconcileService != null)
            ? _predictionReconcileService.Diagnostics.ReplicateTicksProcessed
            : 0;
        public int ReconcilePacketsReceived => (_predictionReconcileService != null)
            ? _predictionReconcileService.Diagnostics.ReconcilePacketsReceived
            : 0;
        public int SpectatorPresentationApplications => (_predictionReconcileService != null)
            ? _predictionReconcileService.Diagnostics.SpectatorPresentationApplications
            : 0;
        public int LastReplicateFrame => (_predictionReconcileService != null)
            ? _predictionReconcileService.Diagnostics.LastReplicateFrame
            : InputFrame.InvalidFrame;
        public int LastSpectatorPresentationFrame => (_predictionReconcileService != null)
            ? _predictionReconcileService.Diagnostics.LastSpectatorPresentationFrame
            : InputFrame.InvalidFrame;
        public int MissingPredictedStateCount => (_predictionReconcileService != null)
            ? _predictionReconcileService.Diagnostics.MissingPredictedStateCount
            : 0;
        public int IgnoredCorrectionCount => (_predictionReconcileService != null)
            ? _predictionReconcileService.Diagnostics.IgnoredCount
            : 0;
        public int ObserveOnlyCorrectionCount => (_predictionReconcileService != null)
            ? _predictionReconcileService.Diagnostics.ObserveOnlyCount
            : 0;
        public int HardCorrectionCount => (_predictionReconcileService != null)
            ? _predictionReconcileService.Diagnostics.HardCorrectCount
            : 0;
        public int ForceCorrectionCount => (_predictionReconcileService != null)
            ? _predictionReconcileService.Diagnostics.ForceCorrectCount
            : 0;

        internal SurfCharacter Character => _character;
        internal RollbackManager RollbackManager => _rollback;
        internal PlayerAiming PlayerAiming => _playerAiming;
        internal bool EnforceCameraOwnershipInvariant => _enforceCameraOwnershipInvariant;
        internal bool EnforceVirtualCameraOwnershipInvariant => _enforceVirtualCameraOwnershipInvariant;
        internal bool ShouldIgnoreCrouchForForeignSimulation => !DebugHasLocalAuthority && (IsServerInitialized || IsClientInitialized);
        internal bool HasEnabledInputCollector => _inputCollector != null && _inputCollector.enabled;
        internal bool HasResolvedLocalConnection => Owner != null && Owner.IsValid && LocalConnection != null && LocalConnection.IsValid;
        internal TimeManager NetworkTimeManager => TimeManager;
        internal MoveData CurrentMoveData => (_character != null) ? _character.moveData : null;
        internal int CurrentMoveFrame => (_character != null && _character.moveData != null) ? _character.moveData.frame : 0;
        internal Vector3 CurrentPosition => (_character != null && _character.moveData != null) ? _character.moveData.origin : transform.position;
        internal Vector3 CurrentVelocity => (_character != null && _character.moveData != null) ? _character.moveData.velocity : Vector3.zero;
        public int CurrentHealth => _currentHealth.Value;
        public int MaxHealth => Mathf.Max(1, _maxHealth);

        public event System.Action<int, int, bool> HealthChanged;

        private void Awake() {
            if (_character == null)
                _character = GetComponent<SurfCharacter>();
            if (_rollback == null)
                _rollback = GetComponent<RollbackManager>();
            if (_inputCollector == null)
                _inputCollector = GetComponent<LocalInputCollector>();
            if (_playerAiming == null)
                _playerAiming = GetComponentInChildren<PlayerAiming>(true);

            _character?.EnsureRuntimeInitialized();
            if (_inputCollector == null)
                _inputCollector = GetComponent<LocalInputCollector>();
            if (_playerAiming == null)
                _playerAiming = GetComponentInChildren<PlayerAiming>(true);
            ValidateAuthorityContract();

            _ownershipGate = new NetworkedCharacterOwnershipGateController(this);
            _cameraOwnershipGuard = new NetworkedCharacterCameraOwnershipGuard(this);
            _predictionReconcileService = new NetworkedCharacterPredictionReconcileService(this);
            _currentHealth.OnChange += CurrentHealth_OnChange;
            _currentHealth.Value = MaxHealth;

            SetLocalControlEnabledInternal(false, preserveHeldButtons: false);
            SetAimingEnabledInternal(false);
            _hasLastReplicatedInput = false;
            _lastReplicatedInput = default;

            if (_rollback != null && _character != null)
                _rollback.Initialize(_character);

            TrySubscribeToHurtbox();
            EnsureHealthBillboard();
        }

        public override void OnStartNetwork() {
            base.OnStartNetwork();

            if (NetworkObject != null && !NetworkObject.EnablePrediction) {
                Debug.LogError("[NetworkedCharacter] FishNet prediction is disabled on this NetworkObject. This controller depends on Replicate/Reconcile for visible multiplayer movement, so remote players will remain frozen unless prediction is enabled or another sync path is added.", this);
            }

            _hasLastReplicatedInput = false;
            _lastReplicatedInput = default;
            _ownershipGate.OnNetworkStart();

            if (TimeManager != null) {
                TimeManager.OnTick += TimeManager_OnTick;
                TimeManager.OnPostTick += TimeManager_OnPostTick;
            }

            EnsureHealthBillboard();
        }

        public override void OnStartClient() {
            base.OnStartClient();
            _ownershipGate.OnClientStart();
            EnsureHealthBillboard();
        }

        public override void OnStartServer() {
            base.OnStartServer();
            _currentHealth.Value = MaxHealth;
        }

        public override void OnOwnershipClient(NetworkConnection prevOwner) {
            base.OnOwnershipClient(prevOwner);
            _ownershipGate.OnOwnershipClient();
        }

        public override void OnStopNetwork() {
            base.OnStopNetwork();

            if (TimeManager != null) {
                TimeManager.OnTick -= TimeManager_OnTick;
                TimeManager.OnPostTick -= TimeManager_OnPostTick;
            }

            _hasLastReplicatedInput = false;
            _lastReplicatedInput = default;
            _ownershipGate.OnNetworkStop();
            SetLocalControlEnabledInternal(false, preserveHeldButtons: false);
            SetAimingEnabledInternal(false);
        }

        private void OnDestroy() {
            UnsubscribeFromHurtbox();
            _currentHealth.OnChange -= CurrentHealth_OnChange;
        }

        private void LateUpdate() {
            _cameraOwnershipGuard.LateUpdate();
        }

        private void TimeManager_OnTick() {
            if (!IsClientInitialized && !IsServerInitialized)
                return;

            if (!CanRunPredictionTick()) {
                return;
            }

            RunInputs(CreateReplicateData());
        }

        private void TimeManager_OnPostTick() {
            CreateReconcile();
        }

        private NetworkedCharacterReplicateData CreateReplicateData() {
            int tick = (TimeManager != null) ? (int)TimeManager.Tick : Mathf.Max(0, CurrentMoveFrame + 1);
            InputFrame input;
            if (DebugHasLocalAuthority && _inputCollector != null) {
                input = _inputCollector.GatherInput(tick);
            } else if (_hasLastReplicatedInput) {
                input = _lastReplicatedInput;
            } else {
                input = InputFrame.Empty(InputFrame.InvalidFrame, ObjectId);
            }

            if (input.characterObjectId <= 0)
                input.characterObjectId = ObjectId;

            if (input.frame < 0 && DebugHasLocalAuthority)
                input.frame = tick;

            if (!DebugHasLocalAuthority && TryGetLookAngles(out float yaw, out float pitch)) {
                input.lookYaw100 = (short)Mathf.RoundToInt(NormalizeSignedAngle(yaw) * 100f);
                input.lookPitch100 = (short)Mathf.RoundToInt(NormalizeSignedAngle(pitch) * 100f);
            }

            return new NetworkedCharacterReplicateData(input);
        }

        [Replicate]
        private void RunInputs(NetworkedCharacterReplicateData data,
                               ReplicateState state = ReplicateState.Invalid,
                               Channel channel = Channel.Unreliable) {
            if (_predictionReconcileService == null)
                return;

            float deltaTime = (TimeManager != null) ? (float)TimeManager.TickDelta : Time.fixedDeltaTime;
            _predictionReconcileService.RunReplicate(data,
                                                     state,
                                                     deltaTime,
                                                     ref _lastReplicatedInput,
                                                     ref _hasLastReplicatedInput);
        }

        public override void CreateReconcile() {
            if (!IsServerInitialized || _predictionReconcileService == null)
                return;

            int frame = (TimeManager != null) ? (int)TimeManager.Tick : CurrentMoveFrame;
            NetworkedCharacterReconcileData data = _predictionReconcileService.BuildReconcileData(frame);
            ReconcileState(data);
        }

        [Reconcile]
        private void ReconcileState(NetworkedCharacterReconcileData data, Channel channel = Channel.Unreliable) {
            if (_predictionReconcileService == null)
                return;

            float deltaTime = (TimeManager != null) ? (float)TimeManager.TickDelta : Time.fixedDeltaTime;
            _predictionReconcileService.ApplyReconcile(data, deltaTime);
        }

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

        internal void RefreshOwnershipState() {
            bool enableLocalControl = _ownershipGate != null && _ownershipGate.IsLocalControlReady;
            SetLocalControlEnabledInternal(enableLocalControl, preserveHeldButtons: enableLocalControl);
            SetAimingEnabledInternal(enableLocalControl);
            _cameraOwnershipGuard?.ApplyOwnershipState();
        }

        internal void RemoveEventSystemsFromPlayerHierarchy(string phase) {
            if (!_removeEventSystemsFromPlayerHierarchy)
                return;

            EventSystem[] eventSystems = GetComponentsInChildren<EventSystem>(true);
            for (int i = 0; i < eventSystems.Length; i++) {
                EventSystem eventSystem = eventSystems[i];
                if (eventSystem != null)
                    Destroy(eventSystem);
            }
        }

        internal bool TryGetLookAngles(out float yaw, out float pitch) {
            yaw = transform.eulerAngles.y;
            pitch = 0f;

            Transform yawTransform = (_playerAiming != null && _playerAiming.bodyTransform != null)
                ? _playerAiming.bodyTransform
                : transform;
            yaw = yawTransform.eulerAngles.y;

            if (_character != null && _character.viewTransform != null)
                pitch = NormalizeSignedAngle(_character.viewTransform.eulerAngles.x);

            return true;
        }

        internal void GetAuthoritativeLookAngles(out float yaw, out float pitch) {
            yaw = transform.eulerAngles.y;
            pitch = 0f;

            if (_character != null && _character.moveData != null) {
                yaw = _character.moveData.viewAngles.y;
                pitch = _character.moveData.viewAngles.x;
                return;
            }

            TryGetLookAngles(out yaw, out pitch);
        }

        internal void SyncCurrentMoveLook(float yaw, float pitch) {
            if (_character == null || _character.moveData == null)
                return;

            MoveData state = _character.moveData;
            state.viewAngles.y = yaw;
            state.viewAngles.x = pitch;
            _character.moveData = state;
        }

        internal void ApplySpectatorPresentationYawFromCurrentState() {
            if (DebugHasLocalAuthority || _character == null || _character.moveData == null)
                return;

            ApplyBodyYaw(_character.moveData.viewAngles.y);
        }

        internal void SetLocalControlEnabledInternal(bool shouldEnable, bool preserveHeldButtons) {
            if (_inputCollector == null)
                return;

            if (!shouldEnable || _inputCollector.enabled != shouldEnable)
                _inputCollector.ResetState(preserveHeldButtons);

            _inputCollector.enabled = shouldEnable;
        }

        internal void SetAimingEnabledInternal(bool shouldEnable) {
            if (_playerAiming != null)
                _playerAiming.enabled = shouldEnable;
        }

        private void ValidateAuthorityContract() {
            if (_character == null)
                Debug.LogWarning("[NetworkedCharacter] Missing SurfCharacter.", this);

            if (_rollback == null)
                Debug.LogWarning("[NetworkedCharacter] Missing RollbackManager.", this);

            if (_inputCollector == null)
                Debug.LogWarning("[NetworkedCharacter] Missing LocalInputCollector.", this);

            if (_playerAiming == null)
                return;

            MonoBehaviour[] behaviours = GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++) {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour != null && behaviour.GetType().Name == "RotateWithCamera") {
                    behaviour.enabled = false;
                    break;
                }
            }
        }

        private void TrySubscribeToHurtbox() {
            if (_hurtboxSubscribed || _character == null || _character.playerHurtboxComponent == null)
                return;

            _character.playerHurtboxComponent.OnTakeHit += PlayerHurtboxComponent_OnTakeHit;
            _hurtboxSubscribed = true;
        }

        private void UnsubscribeFromHurtbox() {
            if (!_hurtboxSubscribed || _character == null || _character.playerHurtboxComponent == null)
                return;

            _character.playerHurtboxComponent.OnTakeHit -= PlayerHurtboxComponent_OnTakeHit;
            _hurtboxSubscribed = false;
        }

        private void EnsureHealthBillboard() {
            if (_healthBillboard == null)
                _healthBillboard = GetComponent<PlayerHealthBillboard>();

            if (_healthBillboard == null)
                _healthBillboard = gameObject.AddComponent<PlayerHealthBillboard>();

            _healthBillboard.Initialize(this, _character);
        }

        private void CurrentHealth_OnChange(int prev, int next, bool asServer) {
            HealthChanged?.Invoke(prev, next, asServer);
        }

        private void PlayerHurtboxComponent_OnTakeHit(Hitbox hitbox) {
            if (!IsServerInitialized || _respawnRoutineRunning)
                return;

            int damage = ResolveDamage(hitbox);
            int nextHealth = Mathf.Max(0, _currentHealth.Value - damage);
            if (_combatDebugLogging) {
                string attackerId = hitbox != null ? hitbox.definition.id : "<null>";
                Debug.Log($"[NetworkedCharacter] Server damage received. objectId={ObjectId}, attackerHitboxId={attackerId}, damage={damage}, prevHealth={_currentHealth.Value}, nextHealth={nextHealth}", this);
            }
            _currentHealth.Value = nextHealth;

            if (nextHealth <= 0)
                StartRespawnRoutineServer();
        }

        private int ResolveDamage(Hitbox hitbox) {
            if (hitbox == null)
                return 1;

            return Mathf.Max(1, Mathf.RoundToInt(hitbox.definition.damage));
        }

        [Server]
        private void StartRespawnRoutineServer() {
            if (_respawnRoutineRunning)
                return;

            _respawnRoutineRunning = true;
            if (_respawnDelaySeconds <= 0f) {
                RespawnNowServer();
                return;
            }

            StartCoroutine(RespawnRoutineServer());
        }

        [Server]
        private IEnumerator RespawnRoutineServer() {
            if (_respawnDelaySeconds > 0f)
                yield return new WaitForSeconds(_respawnDelaySeconds);

            RespawnNowServer();
        }

        [Server]
        private void RespawnNowServer() {
            if (_combatDebugLogging)
                Debug.Log($"[NetworkedCharacter] Respawn triggered. objectId={ObjectId}, resetting health to {MaxHealth}.", this);

            _currentHealth.Value = MaxHealth;

            if (!PlayerSpawnService.TryGetInstance(out PlayerSpawnService spawnService) ||
                !spawnService.TryRespawnPlayer(this)) {
                Debug.LogWarning("[NetworkedCharacter] Respawn requested but PlayerSpawnService was unavailable. Falling back to current position reset.", this);
                ApplyAuthoritativeSpawnPoseServer(transform.position, transform.rotation);
            }

            _respawnRoutineRunning = false;
        }

        private void ApplyAuthoritativeSpawnPoseLocal(Vector3 position, Quaternion rotation) {
            transform.SetPositionAndRotation(position, rotation);

            float yaw = rotation.eulerAngles.y;
            float pitch = 0f;

            if (_character != null) {
                MoveData state = _character.BuildRespawnState(position, yaw, 0f);
                _character.LoadState(state);

                if (_character.viewTransform != null)
                    pitch = NormalizeSignedAngle(_character.viewTransform.eulerAngles.x);
            }

            ApplyLookRotation(yaw, pitch);
            _ownershipGate.OnAuthoritativeSpawnPoseApplied();
        }

        private void ApplyLookRotation(float yaw, float pitch) {
            ApplyBodyYaw(yaw);

            if (_character != null && _character.viewTransform != null) {
                Vector3 viewEuler = _character.viewTransform.eulerAngles;
                viewEuler.x = pitch;
                viewEuler.y = yaw;
                _character.viewTransform.eulerAngles = viewEuler;
            }
        }

        private void ApplyBodyYaw(float yaw) {
            Transform yawTransform = (_playerAiming != null && _playerAiming.bodyTransform != null)
                ? _playerAiming.bodyTransform
                : transform;

            Vector3 bodyEuler = yawTransform.eulerAngles;
            bodyEuler.y = yaw;
            yawTransform.eulerAngles = bodyEuler;
        }

        private bool HasLocalAuthority() {
            if (!IsOwner)
                return false;

            bool ownerValid = Owner != null && Owner.IsValid;
            bool localConnectionValid = LocalConnection != null && LocalConnection.IsValid;
            if (!ownerValid || !localConnectionValid)
                return true;

            return Owner.ClientId == LocalConnection.ClientId;
        }

        private bool CanRunPredictionTick() {
            if (_character == null) {
                return false;
            }

            _character.EnsureRuntimeInitialized();
            if (!_character.IsSimulationReady) {
                return false;
            }

            bool isAuthoritativeServerProxy = IsServerInitialized && !DebugHasLocalAuthority;
            if (_ownershipGate != null && !_ownershipGate.HasAuthoritativeSpawnPose && !isAuthoritativeServerProxy) {
                return false;
            }

            if (DebugHasLocalAuthority && _ownershipGate != null && !_ownershipGate.IsLocalControlReady) {
                return false;
            }

            bool isPureClientProxy = IsClientInitialized && !IsServerInitialized && !DebugHasLocalAuthority;
            if (isPureClientProxy) {
                return false;
            }

            return true;
        }

        private static float NormalizeSignedAngle(float angle) {
            angle %= 360f;
            if (angle > 180f)
                angle -= 360f;
            if (angle < -180f)
                angle += 360f;
            return angle;
        }
    }
}
