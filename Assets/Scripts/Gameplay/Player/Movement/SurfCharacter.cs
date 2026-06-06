using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine.Rendering;
using Fragsurf.Combat;
using FishNet.Object;

namespace Fragsurf.Movement {

    /// <summary>
    /// Easily add a surfable character to the scene
    /// </summary>
    [AddComponentMenu ("Fragsurf/Surf Character")]
    public class SurfCharacter : MonoBehaviour, ISurfControllable {

        public enum ColliderType {
            Capsule,
            Box
        }

        ///// Fields /////

        [Header("Physics Settings")]
        public Vector3 colliderSize = new Vector3 (1f, 2f, 1f);
        [HideInInspector] public ColliderType collisionType { get { return ColliderType.Box; } } // Capsule doesn't work anymore; I'll have to figure out why some other time, sorry.
        public float weight = 75f;
        public float rigidbodyPushForce = 2f;
        public bool solidCollider = false;

        [Header("View Settings")]
        public Transform viewTransform;
        public Transform playerRotationTransform;

        [Header ("Crouching setup")]
        public float crouchingHeightMultiplier = 0.5f;
        public float crouchingSpeed = 10f;
        float defaultHeight;
        bool allowCrouch = true; // This is separate because you shouldn't be able to toggle crouching on and off during gameplay for various reasons

        [Header ("Features")]
        public bool crouchingEnabled = true;
        public bool slidingEnabled = false;
        public bool laddersEnabled = true;
        public bool supportAngledLadders = true;

        [Header ("Step offset (can be buggy, enable at your own risk)")]
        public bool useStepOffset = false;
        public float stepOffset = 0.35f;


        [Header("VFX Spawn Points")]
        public Transform lowerVfxSpawnPoint;  // For feet/slide effects

        [Header("Sliding Visuals")]
        public Transform renderMesh;
        public Transform playerHitbox;

        [Header("Renderer")]
        public CharacterRenderer characterRenderer;
        
        [Header ("Movement Config")]
        [SerializeField]
        public MovementConfig movementConfig;

        [Header("Combat")]
        public Hitbox meleeHitbox;
        public Hurtbox playerHurtboxComponent;
        public LayerMask enemyLayerMask;
        [SerializeField] private bool _combatDebugLogging = true;
        [SerializeField] private bool _parryDebugLogging = true;
        
        private GameObject _groundObject;
        private Vector3 _baseVelocity;
        private Collider _collider;
        private Vector3 _startPosition;
        private Vector3 _initialRenderMeshLocalPos;
        private Vector3 _initialPlayerHitboxLocalPos;
        private GameObject _colliderObject;
        private GameObject _cameraWaterCheckObject;
        private CameraWaterCheck _cameraWaterCheck;

        private MoveData _moveData = new MoveData ();
        private SurfController _controller = new SurfController ();

        private Rigidbody rb;

        private List<Collider> triggers = new List<Collider> ();
        private int numberOfTriggers = 0;

        private bool underwater = false;
        private const int DIAGNOSTIC_BUFFER_SIZE = 256;
        private readonly string[] _simulationDiagnosticBuffer = new string[DIAGNOSTIC_BUFFER_SIZE];
        private readonly int[] _simulationDiagnosticFrameAtSlot = new int[DIAGNOSTIC_BUFFER_SIZE];
        private string _currentMovementQueryDiagnostics = string.Empty;
        private string _currentMeleeQueryDiagnostics = string.Empty;
        private bool _loggedMissingCharacterRenderer;
        private bool _loggedFoundCharacterRenderer;

        ///// Properties /////

        public MoveType moveType { get { return _moveData.moveType; } }
        public MovementConfig moveConfig { get { return movementConfig; } }
        public MoveData moveData { get { return _moveData; } set { _moveData = value; } }
        public new Collider collider { get { return _collider; } }

        public GameObject groundObject {

            get { return _groundObject; }
            set { _groundObject = value; }

        }

        public Vector3 baseVelocity { get { return _baseVelocity; } }

        public Vector3 forward { get { return viewTransform.forward; } }
        public Vector3 right { get { return viewTransform.right; } }
        public Vector3 up { get { return viewTransform.up; } }

        private Vector3 prevPosition;
        private MoveData _prevState = new MoveData();

        ///// Methods /////

		private void OnDrawGizmos()
		{
			Gizmos.color = Color.red;
			Gizmos.DrawWireCube( transform.position, colliderSize );
		}
		
        private void Awake () {
            EnsureRuntimeInitialized();
        }

        private PlayerAiming _playerAiming;
        private float _defaultTurnSpeed;
        private bool _runtimeInitialized;

        private void Start () {
            EnsureRuntimeInitialized();
        }

        internal void EnsureRuntimeInitialized() {
            if (_runtimeInitialized)
                return;

            if (viewTransform == null) {
                Camera localCamera = GetComponentInChildren<Camera>(true);
                if (localCamera != null) {
                    viewTransform = localCamera.transform;
                    Debug.Log($"[SurfCharacter] Auto-assigned local viewTransform from child camera '{localCamera.name}'.", this);
                } else if (Camera.main != null) {
                    // Final fallback for compatibility; this can be unsafe in multiplayer if multiple cameras are tagged MainCamera.
                    viewTransform = Camera.main.transform;
                    Debug.LogWarning("[SurfCharacter] viewTransform missing; falling back to Camera.main. Assign a per-player viewTransform to avoid multiplayer camera binding issues.", this);
                } else {
                    Debug.LogError("[SurfCharacter] viewTransform is missing and no camera was found in this player hierarchy.", this);
                }
            }

            if (playerRotationTransform == null && transform.childCount > 0)
                playerRotationTransform = transform.GetChild (0);

            _controller.playerTransform = playerRotationTransform;
            if (viewTransform != null) {
                _controller.camera = viewTransform;
                _controller.cameraYPos = viewTransform.localPosition.y;
            }

            if (_colliderObject == null) {
                _colliderObject = new GameObject ("PlayerCollider");
                _colliderObject.layer = gameObject.layer;
                _colliderObject.transform.SetParent (transform);
                _colliderObject.transform.rotation = Quaternion.identity;
                _colliderObject.transform.localPosition = Vector3.zero;
                _colliderObject.transform.SetSiblingIndex (0);
            }

            if (_cameraWaterCheckObject == null && viewTransform != null) {
                _cameraWaterCheckObject = new GameObject ("Camera water check");
                _cameraWaterCheckObject.layer = gameObject.layer;
                _cameraWaterCheckObject.transform.position = viewTransform.position;

                SphereCollider _cameraWaterCheckSphere = _cameraWaterCheckObject.AddComponent<SphereCollider> ();
                _cameraWaterCheckSphere.radius = 0.1f;
                _cameraWaterCheckSphere.isTrigger = true;

                Rigidbody _cameraWaterCheckRb = _cameraWaterCheckObject.AddComponent<Rigidbody> ();
                _cameraWaterCheckRb.useGravity = false;
                _cameraWaterCheckRb.isKinematic = true;

                _cameraWaterCheck = _cameraWaterCheckObject.AddComponent<CameraWaterCheck> ();
            } else if (_cameraWaterCheckObject != null && _cameraWaterCheck == null) {
                _cameraWaterCheck = _cameraWaterCheckObject.GetComponent<CameraWaterCheck>();
            }

            if (characterRenderer == null)
                characterRenderer = GetComponent<CharacterRenderer>();

            prevPosition = transform.position;

            // Cache PlayerAiming and default turn speed
            // First check view transform, then self, then children
            if (viewTransform) _playerAiming = viewTransform.GetComponent<PlayerAiming>();
            if (_playerAiming == null) _playerAiming = GetComponent<PlayerAiming>();
            if (_playerAiming == null) _playerAiming = GetComponentInChildren<PlayerAiming>();
            
            if (_playerAiming != null) {
                _defaultTurnSpeed = _playerAiming.maxTurnSpeed;
            } else {
                Debug.LogError("SurfCharacter: Could not find PlayerAiming script! Melee turn clamping will not work.");
            }

            Collider rootCollider = gameObject.GetComponent<Collider> ();
            if (rootCollider != null)
                GameObject.Destroy (rootCollider);

            if (_colliderObject != null) {
                Collider runtimeCollider = _colliderObject.GetComponent<Collider>();
                if (runtimeCollider != null)
                    _collider = runtimeCollider;
            }

            // rigidbody is required to collide with triggers
            rb = gameObject.GetComponent<Rigidbody> ();
            if (rb == null)
                rb = gameObject.AddComponent<Rigidbody> ();

            allowCrouch = crouchingEnabled;

            rb.isKinematic = true;
            rb.useGravity = false;
            rb.angularDamping = 0f;
            rb.linearDamping = 0f;
            rb.mass = weight;


            if (_collider == null && _colliderObject != null) {
                switch (collisionType) {

                    // Box collider
                    case ColliderType.Box:

                    _collider = _colliderObject.AddComponent<BoxCollider> ();

                    var boxc = (BoxCollider)_collider;
                    boxc.size = colliderSize;

                    defaultHeight = boxc.size.y;

                    break;

                    // Capsule collider
                    case ColliderType.Capsule:

                    _collider = _colliderObject.AddComponent<CapsuleCollider> ();

                    var capc = (CapsuleCollider)_collider;
                    capc.height = colliderSize.y;
                    capc.radius = colliderSize.x / 2f;

                    defaultHeight = capc.height;

                    break;
                }
            }

            _moveData.slopeLimit = movementConfig.slopeLimit;

            _moveData.rigidbodyPushForce = rigidbodyPushForce;

            _moveData.slidingEnabled = slidingEnabled;
            _moveData.laddersEnabled = laddersEnabled;
            _moveData.angledLaddersEnabled = supportAngledLadders;

            _moveData.playerTransform = transform;
            _moveData.viewTransform = viewTransform;
            _moveData.viewTransformDefaultLocalPos = viewTransform.localPosition;

            _moveData.defaultHeight = defaultHeight;
            _moveData.crouchingHeight = crouchingHeightMultiplier;
            _moveData.crouchingSpeed = crouchingSpeed;
            
            _collider.isTrigger = !solidCollider;
            _moveData.origin = transform.position;
            _startPosition = transform.position;

            // Snap to ground on spawn
            float snapDistance = 5f; // Max distance to snap down
            Vector3 rayStart = _moveData.origin;
            if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit snapHit, snapDistance, SurfPhysics.groundLayerMask, QueryTriggerInteraction.Ignore)) {
                // Snap origin so feet touch ground
                float distToFeet = _collider.bounds.extents.y;
                _moveData.origin = snapHit.point + Vector3.up * distToFeet;
                _startPosition = _moveData.origin;
                Debug.Log($"[SurfCharacter] Snapped to ground. New Origin: {_moveData.origin}");
            }

            _moveData.useStepOffset = useStepOffset;
            _moveData.stepOffset = stepOffset;

            if (renderMesh != null)
                _initialRenderMeshLocalPos = renderMesh.localPosition;
            if (playerHitbox != null)
                _initialPlayerHitboxLocalPos = playerHitbox.localPosition;

            // Combat Setup
            if (meleeHitbox == null)
                meleeHitbox = GetComponentInChildren<Hitbox>(true);
            if (playerHurtboxComponent == null)
                playerHurtboxComponent = GetComponentInChildren<Hurtbox>(true);

            if (meleeHitbox != null && movementConfig != null) {
                if (IsValidHitboxDefinition(movementConfig.heavyMeleeHitbox)) {
                    meleeHitbox.ConfigureFromConfig(movementConfig.heavyMeleeHitbox, enemyLayerMask);
                    meleeHitbox.definition = movementConfig.heavyMeleeHitbox;
                } else {
                    Debug.LogWarning($"[SurfCharacter] MovementConfig heavyMeleeHitbox is invalid for shape {movementConfig.heavyMeleeHitbox.shape}. Keeping prefab Hitbox definition instead.", this);
                }

                meleeHitbox.targetLayer = enemyLayerMask;
                meleeHitbox.Deactivate();
            }

            if (playerHurtboxComponent != null && movementConfig != null) {
                playerHurtboxComponent.ConfigureFromConfig(movementConfig.playerHurtbox);
            }

            _networkedCharacter = GetComponent<NetworkedCharacter>();
            _hasNetworkedCharacter = _networkedCharacter != null;

            // Step 7: Input ownership is managed by NetworkedCharacter in multiplayer.
            // Legacy fallback: only auto-create if no NetworkedCharacter exists (singleplayer/legacy).
            _inputCollector = GetComponent<LocalInputCollector>();
            if (_inputCollector == null && !_hasNetworkedCharacter) {
                // Only add in non-networked sessions to avoid conflicts with NetworkedCharacter.
                _inputCollector = gameObject.AddComponent<LocalInputCollector>();
                Debug.Log("[SurfCharacter] Auto-created LocalInputCollector (legacy singleplayer path).", this);
            } else if (_inputCollector == null && _hasNetworkedCharacter) {
                // Multiplayer: NetworkedCharacter owns input collector setup.
                Debug.Log("[SurfCharacter] Input authority managed by NetworkedCharacter (multiplayer path).", this);
            }
            _prevState = _moveData.Clone();
            _prevState.renderCrouchLerp = _prevState.crouchLerp;
            SyncRuntimeStateFromMoveData();

            for (int i = 0; i < DIAGNOSTIC_BUFFER_SIZE; i++)
                _simulationDiagnosticFrameAtSlot[i] = -1;
            
            _runtimeInitialized = true;
        }

        private LocalInputCollector _inputCollector;
        private bool _hasNetworkedCharacter;
        private NetworkedCharacter _networkedCharacter;
        private int _frameCounter;
        private float _standaloneSimulationAccumulator;
        private int _lastRenderedTick = -1;
        private MoveData _lastPresentedState;
        private bool _loggedSimulationNotReady;

        private const int MaxStandaloneSimulationStepsPerFrame = 4;

        public bool IsSimulationReady {
            get {
                RefreshRuntimeComponentReferences();
                return _colliderObject != null &&
                       _cameraWaterCheckObject != null &&
                       _cameraWaterCheck != null &&
                       _collider != null &&
                       movementConfig != null &&
                       viewTransform != null;
            }
        }

        public MoveData SimulationTick(MoveData state, InputFrame input, float deltaTime, bool allowGameplaySideEffects = true) {
            EnsureRuntimeInitialized();

            if (state == null)
                state = _moveData != null ? _moveData : new MoveData();

            if (!IsSimulationReady) {
                state.frame = input.frame;
                if (!_loggedSimulationNotReady) {
                    _loggedSimulationNotReady = true;
                    Debug.LogWarning("[SurfCharacter] SimulationTick skipped: character runtime is not initialized yet.", this);
                    NetcodeDebugEngine.Log(NetcodeDebugCategory.Simulation,
                                           NetcodeDebugSeverity.Warning,
                                           BuildDebugContext(input.frame),
                                           "Simulation tick skipped because runtime state is not fully initialized.",
                                           NetcodeDebugSuspectFlags.SpawnOwnershipRace | NetcodeDebugSuspectFlags.PureSimulationLeak,
                                           "surf-sim-not-ready",
                                           1f);
                }
                return state;
            }

            _loggedSimulationNotReady = false;
            
            state.frame = input.frame;
            _prevState = state.Clone();
            _prevState.renderCrouchLerp = _prevState.crouchLerp;
            _currentMovementQueryDiagnostics = string.Empty;
            _currentMeleeQueryDiagnostics = string.Empty;

            // Clear pulse flags
            state.dashStartedThisFrame = false;
            state.doubleJumpedThisFrame = false;
            state.meleeHitThisFrame = false;
            state.parryStartedThisFrame = false;
            state.parrySuccessThisFrame = false;

            _colliderObject.transform.rotation = Quaternion.identity;

            ApplyInputToState(ref state, input);
            UpdateParryState(ref state, deltaTime);
            UpdateMeleeState(ref state, deltaTime);
            
            // Previous movement code (Removed to fix double-movement bug in networked mode)
            // Vector3 positionalMovement = transform.position - prevPosition;
            // transform.position = prevPosition;
            // state.origin += positionalMovement;

            // Triggers
            if (numberOfTriggers != triggers.Count) {
                numberOfTriggers = triggers.Count;

                underwater = false;
                triggers.RemoveAll (item => item == null);
                foreach (Collider trigger in triggers) {

                    if (trigger == null)
                        continue;

                    if (trigger.GetComponentInParent<Water> ())
                        underwater = true;

                }

            }

            state.cameraUnderwater = _moveData.cameraUnderwater;
            state.underwater = underwater;

            if (ShouldIgnoreCrouchForForeignSimulation()) {
                StripCrouchState(ref state);
            } else if (allowCrouch) {
                _controller.Crouch (this, movementConfig, deltaTime);
            }

            _controller.ProcessMovement (this, movementConfig, deltaTime);
            state.renderCrouchLerp = state.crouchLerp;

            ProcessHitboxes(ref state, allowGameplaySideEffects);
            SyncMeleeRuntimeState(state);
            StoreSimulationDiagnostics(input.frame, state);

            // characterRenderer.ApplyState moved to Update() to avoid redundant calls during rollback resimulations

            return state;
        }

        private void ProcessHitboxes(ref MoveData state, bool allowGameplaySideEffects) {
            if (meleeHitbox == null) {
                _currentMeleeQueryDiagnostics = "melee query: unavailable (no hitbox)";
                return;
            }

            if (!IsMeleeHitQueryActive(state)) {
                _currentMeleeQueryDiagnostics = $"melee query: inactive, moveType={state.moveType}, meleeState={state.meleeState}, hasHitTarget={state.hasHitTarget}";
                return;
            }

            Hurtbox hit = meleeHitbox.CheckHit(state.origin, state, playerHurtboxComponent);
            if (hit == null) {
                _currentMeleeQueryDiagnostics = "melee query: active, result=no-hit";
                if (_combatDebugLogging) {
                    Debug.Log($"[SurfCharacter] Melee query active but found no target. frame={state.frame}, origin={state.origin}, velocity={state.velocity}, meleeTimer={state.meleeTimer:F3}, hitbox='{meleeHitbox.name}', targetMask={enemyLayerMask.value}", this);
                }
                return;
            }

            state.hasHitTarget = true;
            state.meleeHitResolved = true;
            state.meleeHitResolveTick = state.frame;
            state.meleeHitTargetObjectId = ResolveHitTargetObjectId(hit);
            state.meleeHitThisFrame = true;
            state.velocity.x = 0f;
            state.velocity.z = 0f;
            bool applyGameplaySideEffects = allowGameplaySideEffects && ShouldApplyAuthoritativeMeleeSideEffects();
            _currentMeleeQueryDiagnostics = $"melee query: active, result=hit, target={hit.name}, sideEffects={(applyGameplaySideEffects ? "applied" : "deferred")}";
            if (_combatDebugLogging) {
                Debug.Log($"[SurfCharacter] Melee hit resolved. frame={state.frame}, target='{hit.name}', targetObjectId={state.meleeHitTargetObjectId}, applyGameplaySideEffects={applyGameplaySideEffects}, isServerPath={ShouldApplyAuthoritativeMeleeSideEffects()}", this);
            }

            // Keep rollback simulation self-contained: the attacker records the hit and
            // optional presentation hooks fire outside replay-only passes.
            if (applyGameplaySideEffects) {
                hit.TakeHit(meleeHitbox);
            }
        }

        private static bool IsMeleeHitQueryActive(MoveData state) {
            return ShouldEnableMeleeHitbox(state);
        }

        private bool ShouldApplyAuthoritativeMeleeSideEffects() {
            if (!_hasNetworkedCharacter || _networkedCharacter == null)
                return true;

            return _networkedCharacter.IsServerInitialized;
        }

        private static int ResolveHitTargetObjectId(Hurtbox hit) {
            if (hit == null)
                return 0;

            NetworkObject networkObject = hit.GetComponentInParent<NetworkObject>();
            return networkObject != null ? networkObject.ObjectId : 0;
        }

        private static bool IsValidHitboxDefinition(HitboxDefinition definition) {
            if (definition.shape == ShapeType.Box)
                return definition.size.sqrMagnitude > 0f;

            if (definition.shape == ShapeType.Sphere || definition.shape == ShapeType.Cone)
                return definition.radius > 0f;

            return false;
        }

        public MoveData BuildRespawnState(Vector3 position, float yaw, float pitch) {
            EnsureRuntimeInitialized();

            MoveData state = new MoveData {
                moveType = MoveType.Walk,
                playerTransform = transform,
                viewTransform = viewTransform,
                viewTransformDefaultLocalPos = (_moveData != null)
                    ? _moveData.viewTransformDefaultLocalPos
                    : (viewTransform != null ? viewTransform.localPosition : Vector3.zero),
                origin = position,
                viewAngles = new Vector3(pitch, yaw, 0f),
                velocity = Vector3.zero,
                surfaceFriction = 1f,
                gravityFactor = 1f,
                walkFactor = 1f,
                stamina = (movementConfig != null) ? movementConfig.maxStamina : 3f,
                canAirDash = true,
                slopeLimit = (movementConfig != null) ? movementConfig.slopeLimit : 45f,
                rigidbodyPushForce = rigidbodyPushForce,
                defaultHeight = defaultHeight,
                crouchingHeight = crouchingHeightMultiplier,
                crouchingSpeed = crouchingSpeed,
                slidingEnabled = slidingEnabled,
                laddersEnabled = laddersEnabled,
                angledLaddersEnabled = supportAngledLadders,
                useStepOffset = useStepOffset,
                stepOffset = stepOffset
            };

            return state;
        }

        public void LoadState(MoveData savedState) {
            _moveData = savedState.Clone();
            _prevState = savedState.Clone(); // Synchronize previous state to prevent jumps after load
            _moveData.renderCrouchLerp = _moveData.crouchLerp;
            _prevState.renderCrouchLerp = _prevState.crouchLerp;
            // Snap transform immediately — renderer will interpolate next Update()
            SyncRuntimeStateFromMoveData();
            if (rb != null) {
                rb.position = _moveData.origin;
            } else {
                transform.position = _moveData.origin;
            }
        }

        public void RefreshRuntimeStateFromMoveData() {
            if (_moveData == null)
                return;

            if (ShouldIgnoreCrouchForForeignSimulation())
                StripCrouchState(ref _moveData);

            SyncRuntimeStateFromMoveData();
        }

        private void SyncRuntimeStateFromMoveData() {
            SyncColliderShapeFromMoveData();
            SyncMeleeRuntimeStateFromMoveData();
        }

        private void RefreshRuntimeComponentReferences() {
            if (_colliderObject != null && _collider == null)
                _collider = _colliderObject.GetComponent<Collider>();

            if (_cameraWaterCheckObject != null && _cameraWaterCheck == null)
                _cameraWaterCheck = _cameraWaterCheckObject.GetComponent<CameraWaterCheck>();
        }

        private void SyncColliderShapeFromMoveData() {
            if (_collider == null || _moveData == null)
                return;

            float crouchingHeight = Mathf.Clamp(_moveData.crouchingHeight, 0.01f, 1f);
            float targetHeight = _moveData.crouching
                ? _moveData.defaultHeight * crouchingHeight
                : _moveData.defaultHeight;

            if (_collider is BoxCollider boxCollider) {
                Vector3 size = boxCollider.size;
                size.y = targetHeight;
                boxCollider.size = size;
                return;
            }

            if (_collider is CapsuleCollider capsuleCollider)
                capsuleCollider.height = targetHeight;
        }

        private void SyncMeleeRuntimeStateFromMoveData() {
            SyncMeleeRuntimeState(_moveData);
        }

        private void SyncMeleeRuntimeState(MoveData state) {
            bool shouldClampTurn = movementConfig != null && state != null && state.moveType == MoveType.HeavyMelee;
            SetTurnClamp(shouldClampTurn ? movementConfig.heavyMeleeTurnClamp : _defaultTurnSpeed);

            if (meleeHitbox == null) {
                return;
            }

            if (ShouldEnableMeleeHitbox(state)) {
                meleeHitbox.Activate();
            } else {
                meleeHitbox.Deactivate();
            }
        }

        private static bool ShouldEnableMeleeHitbox(MoveData state) {
            return state != null &&
                   state.moveType == MoveType.HeavyMelee &&
                   state.meleeState == MoveData.MeleeState.Lunging &&
                   !state.hasHitTarget;
        }

        private void Update () {
            RunStandaloneSimulationIfNeeded();

            // Simulation is normally driven by FishNet TimeManager.OnTick via
            // NetworkedCharacter/RollbackManager. When no network session is active,
            // keep the legacy local character path alive here.
            ApplyPresentationCrouchState();

            // Visual Rotations (Sliding Tilt & Dash Roll)
            Quaternion targetSlideRotation = Quaternion.identity;
            if (slidingEnabled && movementConfig != null) {
                if (_moveData.sliding) {
                    Vector3 slideDir = _moveData.slideDirection;
                    if (slideDir.sqrMagnitude > 0.001f && renderMesh != null) {
                        // Calculate tilt relative to the mesh's parent orientation
                        Vector3 localSlideDir = renderMesh.parent.InverseTransformDirection(slideDir);
                        Vector3 localRightDir = Vector3.Cross(Vector3.up, localSlideDir).normalized;
                        
                        // Use negative angle to tilt back (pitch up)
                        targetSlideRotation = Quaternion.AngleAxis(-movementConfig.slideTiltAngle, localRightDir);
                    }
                }
                
                _smoothedSlideRotation = Quaternion.Slerp(_smoothedSlideRotation, targetSlideRotation, Time.deltaTime * movementConfig.slideTiltSpeed);
            }
            
            Quaternion dashRotation = Quaternion.identity;
            Vector3 dashPivotOffset = Vector3.zero;
            
            // Detect dash start to initialize visual parameters
            if (_moveData.isDashing && !_wasVisualDashing) {
                _isVisualDashing = true;
                _visualDashProgress = 0f;
                _visualDashDuration = _moveData.currentDashDuration;
                
                Vector3 dashDir = new Vector3(_moveData.velocity.x, 0, _moveData.velocity.z).normalized;
                if (dashDir.sqrMagnitude == 0 && renderMesh != null) dashDir = renderMesh.parent.forward;
                _visualDashDir = dashDir;
            }
            _wasVisualDashing = _moveData.isDashing;
            
            if (_isVisualDashing && _visualDashDuration > 0f && renderMesh != null) {
                // Increment our own independent timer
                _visualDashProgress += Time.deltaTime;
                
                // Determine speed multiplier based on dash type
                float speedMultiplier = (_visualDashDuration == movementConfig.airDashDuration) ? movementConfig.airDashAnimationSpeedMultiplier : movementConfig.dashAnimationSpeedMultiplier;
                if (speedMultiplier <= 0f) speedMultiplier = 1f; // Prevent div by zero
                
                float totalVisualDuration = _visualDashDuration / speedMultiplier;
                float dashProgress = Mathf.Clamp01(_visualDashProgress / totalVisualDuration);
                
                float dashRollAngle = Mathf.Lerp(0f, 360f, dashProgress);
                
                Vector3 localDashDir = renderMesh.parent.InverseTransformDirection(_visualDashDir);
                Vector3 localRightDir = Vector3.Cross(Vector3.up, localDashDir).normalized;
                
                dashRotation = Quaternion.AngleAxis(dashRollAngle, localRightDir);
                
                // Calculate position offset for pivot
                dashPivotOffset = Vector3.up * movementConfig.dashRollPivotOffset;
                
                // End visual dash when progress completes
                if (_visualDashProgress >= totalVisualDuration) {
                     _isVisualDashing = false;
                }
            }
            
            if (renderMesh != null) {
                renderMesh.localRotation = dashRotation * _smoothedSlideRotation;
                // Move position around the pivot relative to its original position
                float crouchLerp = Mathf.Clamp01(Mathf.Max(_moveData.renderCrouchLerp, _moveData.crouchLerp));
                Vector3 crouchedRenderPosition = _initialRenderMeshLocalPos;
                crouchedRenderPosition.y = Mathf.Lerp(
                    _initialRenderMeshLocalPos.y,
                    _initialRenderMeshLocalPos.y * Mathf.Clamp(_moveData.crouchingHeight, 0.01f, 1f),
                    crouchLerp);
                renderMesh.localPosition = crouchedRenderPosition + dashPivotOffset - dashRotation * dashPivotOffset;
            }
                
            if (playerHitbox != null) {
                playerHitbox.localRotation = Quaternion.identity;
                float crouchLerp = Mathf.Clamp01(Mathf.Max(_moveData.renderCrouchLerp, _moveData.crouchLerp));
                Vector3 crouchedHitboxPosition = _initialPlayerHitboxLocalPos;
                crouchedHitboxPosition.y = Mathf.Lerp(
                    _initialPlayerHitboxLocalPos.y,
                    _initialPlayerHitboxLocalPos.y * Mathf.Clamp(_moveData.crouchingHeight, 0.01f, 1f),
                    crouchLerp);
                playerHitbox.localPosition = crouchedHitboxPosition;
            }
            
            // transform.position = moveData.origin;
            // prevPosition = transform.position;

            // Apply interpolation
            if (FishNet.InstanceFinder.TimeManager != null) {
                float interpolationFrac = (float)FishNet.InstanceFinder.TimeManager.GetTickPercentAsDouble();
                if (ShouldApplyCharacterTransformInterpolation()) {
                    transform.position = Vector3.Lerp(_prevState.origin, _moveData.origin, interpolationFrac);
                }
                
                bool isNewTick = _moveData.frame > _lastRenderedTick;
                MoveData presentationPrevState = _lastPresentedState ?? _prevState;
                if (characterRenderer != null) {
                    characterRenderer.ApplyState(_moveData, presentationPrevState, isNewTick);
                }
                if (isNewTick) {
                    _lastRenderedTick = _moveData.frame;
                    _lastPresentedState = _moveData.Clone();
                }
            } else {
                if (ShouldApplyCharacterTransformInterpolation()) {
                    transform.position = _moveData.origin;
                }
                bool isNewTick = _moveData.frame > _lastRenderedTick;
                MoveData presentationPrevState = _lastPresentedState ?? _prevState;
                if (characterRenderer != null) {
                    characterRenderer.ApplyState(_moveData, presentationPrevState, isNewTick);
                }
                if (isNewTick) {
                    _lastRenderedTick = _moveData.frame;
                    _lastPresentedState = _moveData.Clone();
                }
            }

            RefreshCameraWaterCheck();
            
            _colliderObject.transform.rotation = Quaternion.identity;
        }

        private void RunStandaloneSimulationIfNeeded() {
            if (!ShouldRunStandaloneSimulation())
                return;

            if (_inputCollector == null)
                return;

            if (!_inputCollector.enabled)
                _inputCollector.enabled = true;

            if (_playerAiming != null && !_playerAiming.enabled)
                _playerAiming.enabled = true;

            float simulationDelta = GetStandaloneSimulationDelta();
            _standaloneSimulationAccumulator = Mathf.Min(_standaloneSimulationAccumulator + Time.deltaTime,
                                                        simulationDelta * MaxStandaloneSimulationStepsPerFrame);

            int stepCount = 0;
            while (_standaloneSimulationAccumulator >= simulationDelta && stepCount < MaxStandaloneSimulationStepsPerFrame) {
                int nextFrame = Mathf.Max(_frameCounter, _moveData != null ? _moveData.frame + 1 : 0);
                _frameCounter = nextFrame + 1;

                InputFrame input = _inputCollector.GatherInput(nextFrame);
                _moveData = SimulationTick(_moveData, input, simulationDelta);
                _standaloneSimulationAccumulator -= simulationDelta;
                stepCount++;
            }
        }

        private bool ShouldRunStandaloneSimulation() {
            if (_networkedCharacter == null)
                return true;

            return !_networkedCharacter.IsClientInitialized && !_networkedCharacter.IsServerInitialized;
        }

        private bool ShouldApplyCharacterTransformInterpolation() {
            if (_networkedCharacter == null)
                return true;

            // Only suppress transform writes for pure client-side proxies when
            // state forwarding is disabled and another sync path is expected to
            // drive visuals (eg. NetworkTransform).
            if (_networkedCharacter.IsClientInitialized &&
                !_networkedCharacter.IsOwner &&
                !_networkedCharacter.IsServerInitialized) {
                FishNet.Object.NetworkObject networkObject = _networkedCharacter.NetworkObject;
                if (networkObject != null && !networkObject.EnableStateForwarding)
                    return false;
            }

            return true;
        }

        private static float GetStandaloneSimulationDelta() {
            if (Time.fixedDeltaTime > 0f)
                return Time.fixedDeltaTime;

            return 1f / 60f;
        }

        private void RefreshCameraWaterCheck() {
            if (_cameraWaterCheckObject == null || _cameraWaterCheck == null || viewTransform == null)
                return;

            _cameraWaterCheckObject.transform.position = viewTransform.position;
            _moveData.cameraUnderwater = _cameraWaterCheck.IsUnderwater();
        }

        internal void SetMovementQueryDiagnostics(string summary) {
            _currentMovementQueryDiagnostics = summary ?? string.Empty;
        }

        public bool TryGetSimulationDiagnostics(int frame, out string diagnostics) {
            int slot = PositiveMod(frame, DIAGNOSTIC_BUFFER_SIZE);
            if (_simulationDiagnosticFrameAtSlot[slot] == frame) {
                diagnostics = _simulationDiagnosticBuffer[slot];
                return true;
            }

            diagnostics = string.Empty;
            return false;
        }

        private void StoreSimulationDiagnostics(int frame, MoveData state) {
            StringBuilder sb = new StringBuilder(256);
            sb.AppendLine(!string.IsNullOrWhiteSpace(_currentMovementQueryDiagnostics)
                ? _currentMovementQueryDiagnostics
                : "movement queries: unavailable");
            sb.AppendLine(!string.IsNullOrWhiteSpace(_currentMeleeQueryDiagnostics)
                ? _currentMeleeQueryDiagnostics
                : "melee query: unavailable");
            sb.AppendLine($"water state: underwater={state.underwater}, cameraUnderwater={state.cameraUnderwater}");

            int slot = PositiveMod(frame, DIAGNOSTIC_BUFFER_SIZE);
            _simulationDiagnosticBuffer[slot] = sb.ToString().TrimEnd();
            _simulationDiagnosticFrameAtSlot[slot] = frame;
        }

        private void ApplyPresentationCrouchState() {
            if (viewTransform == null)
                return;

            float crouchLerp = Mathf.Clamp01(Mathf.Max(_moveData.renderCrouchLerp, _moveData.crouchLerp));
            float crouchingHeight = Mathf.Clamp(_moveData.crouchingHeight, 0.01f, 1f);
            float heightDifference = _moveData.defaultHeight - _moveData.defaultHeight * crouchingHeight;

            if (!_moveData.crouching) {
                viewTransform.localPosition = Vector3.Lerp(
                    _moveData.viewTransformDefaultLocalPos,
                    _moveData.viewTransformDefaultLocalPos * crouchingHeight + Vector3.down * heightDifference * 0.5f,
                    crouchLerp);
            } else {
                viewTransform.localPosition = Vector3.Lerp(
                    _moveData.viewTransformDefaultLocalPos - Vector3.down * heightDifference * 0.5f,
                    _moveData.viewTransformDefaultLocalPos * crouchingHeight,
                    crouchLerp);
            }
        }

        private void ApplyInputToState (ref MoveData state, InputFrame input) {
            
            state.verticalAxis   = input.stickY / 127f;
            state.horizontalAxis = input.stickX / 127f;
            state.wishJump       = input.HasButton(InputFrame.BTN_JUMP);
            bool jumpPressedEdge = input.IsJustPressed(InputFrame.BTN_JUMP);
            bool dashPressedEdge = input.IsJustPressed(InputFrame.BTN_DASH);

            state.wishJumpDown   = jumpPressedEdge && state.lastConsumedJumpPressFrame != input.frame;
            if (state.wishJumpDown)
                state.lastConsumedJumpPressFrame = input.frame;

            state.wishDash       = dashPressedEdge && state.lastConsumedDashPressFrame != input.frame;
            if (state.wishDash)
                state.lastConsumedDashPressFrame = input.frame;

            state.wishMelee      = input.HasButton(InputFrame.BTN_MELEE);
            bool parryPressedEdge = input.IsJustPressed(InputFrame.BTN_BLOCK);
            state.wishParry      = parryPressedEdge && state.lastConsumedParryPressFrame != input.frame;
            state.parryStartedThisFrame = state.wishParry;
            if (state.parryStartedThisFrame)
                state.lastConsumedParryPressFrame = input.frame;
            bool crouchHeld = input.HasButton(InputFrame.BTN_CROUCH);
            state.slideRequested = crouchHeld;
            state.crouching      = ShouldIgnoreCrouchForForeignSimulation() ? false : crouchHeld;
            
            bool moveLeft = state.horizontalAxis < 0f;
            bool moveRight = state.horizontalAxis > 0f;
            bool moveFwd = state.verticalAxis > 0f;
            bool moveBack = state.verticalAxis < 0f;

            if (!moveLeft && !moveRight)
                state.sideMove = 0f;
            else if (moveLeft)
                state.sideMove = -moveConfig.acceleration;
            else if (moveRight)
                state.sideMove = moveConfig.acceleration;

            if (!moveFwd && !moveBack)
                state.forwardMove = 0f;
            else if (moveFwd)
                state.forwardMove = moveConfig.acceleration;
            else if (moveBack)
                state.forwardMove = -moveConfig.acceleration;
            
            state.viewAngles = new Vector3(input.LookPitch, input.LookYaw, 0f);

        }

        private void UpdateParryState(ref MoveData state, float deltaTime) {
            if (state.parryTimer > 0f)
                state.parryTimer = Mathf.Max(0f, state.parryTimer - deltaTime);

            if (state.parryStartedThisFrame) {
                float duration = movementConfig != null ? movementConfig.parryDuration : 0.35f;
                state.parryTimer = Mathf.Max(0f, duration);
            }

            state.isParrying = state.parryTimer > 0f;
        }

        private bool ShouldIgnoreCrouchForForeignSimulation() {
            return _networkedCharacter != null && _networkedCharacter.ShouldIgnoreCrouchForForeignSimulation;
        }

        private void StripCrouchState(ref MoveData state) {
            if (state == null)
                return;

            state.crouching = false;
            state.crouchLerp = 0f;
            state.renderCrouchLerp = 0f;
            state.uncrouchDown = false;
        }

        private void DisableInput () {

            _moveData.verticalAxis = 0f;
            _moveData.horizontalAxis = 0f;
            _moveData.sideMove = 0f;
            _moveData.forwardMove = 0f;
            _moveData.wishJump = false;

        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="angle"></param>
        /// <param name="from"></param>
        /// <param name="to"></param>
        /// <returns></returns>
        public static float ClampAngle (float angle, float from, float to) {

            if (angle < 0f)
                angle = 360 + angle;

            if (angle > 180f)
                return Mathf.Max (angle, 360 + from);

            return Mathf.Min (angle, to);

        }

        private static float NormalizeSignedAngle(float angle) {
            angle %= 360f;
            if (angle > 180f)
                angle -= 360f;
            return angle;
        }

        private void OnTriggerEnter (Collider other) {
            
            if (!triggers.Contains (other))
                triggers.Add (other);

        }

        private void OnTriggerExit (Collider other) {
            
            if (triggers.Contains (other))
                triggers.Remove (other);

        }

        private void OnCollisionStay (Collision collision) {
            // Keep network simulation authoritative and deterministic.
            // Ignore out-of-band rigidbody impulses in networked mode.
            if (_hasNetworkedCharacter)
                return;

            if (collision.rigidbody == null)
                return;

            Vector3 relativeVelocity = collision.relativeVelocity * collision.rigidbody.mass / 50f;
            Vector3 impactVelocity = new Vector3 (relativeVelocity.x * 0.0025f, relativeVelocity.y * 0.00025f, relativeVelocity.z * 0.0025f);

            float maxYVel = Mathf.Max (moveData.velocity.y, 10f);
            Vector3 newVelocity = new Vector3 (moveData.velocity.x + impactVelocity.x, Mathf.Clamp (moveData.velocity.y + Mathf.Clamp (impactVelocity.y, -0.5f, 0.5f), -maxYVel, maxYVel), moveData.velocity.z + impactVelocity.z);

            newVelocity = Vector3.ClampMagnitude (newVelocity, Mathf.Max (moveData.velocity.magnitude, 30f));
            moveData.velocity = newVelocity;

        }

        private void UpdateMeleeState(ref MoveData state, float deltaTime) {
            if (state.meleeCooldownTimer > 0f) {
                state.meleeCooldownTimer = Mathf.Max(0f, state.meleeCooldownTimer - deltaTime);
            }

            if (state.moveType == MoveType.Walk) {
                if (state.wishMelee && state.meleeCooldownTimer <= 0f) {
                    // Track how long the melee button has been held
                    state.meleeHoldTimer += deltaTime;
                    
                    // Only enter charging state after minimum duration is held
                    if (state.meleeHoldTimer >= movementConfig.heavyMeleeMinCharge) {
                        BeginMeleeCharge(ref state);
                    }
                } else {
                    // Reset hold timer when button is not pressed or cooldown is active
                    state.meleeHoldTimer = 0f;
                }
            } else if (state.moveType == MoveType.HeavyMelee) {
                state.meleeTimer += deltaTime;
                
                if (state.meleeState == MoveData.MeleeState.Charging) {
                    bool releasedEarly = !state.wishMelee;
                    bool reachedFullCharge = state.meleeTimer >= movementConfig.heavyMeleeChargeTime;
                    bool reachedMinCharge = state.meleeTimer >= movementConfig.heavyMeleeMinCharge;

                    if (reachedFullCharge || (releasedEarly && reachedMinCharge)) {
                        BeginMeleeLunge(ref state);
                    } else if (releasedEarly) {
                        CancelMelee(ref state, false);
                    }
                }
                else if (state.meleeState == MoveData.MeleeState.Lunging) {
                    if (state.meleeTimer >= movementConfig.heavyMeleeLungeDuration || state.hasHitTarget) {
                        Debug.Log($"[SurfCharacter] Lunge Ending. Timer: {state.meleeTimer}, Hit: {state.hasHitTarget}");
                        EnterMeleeRecovery(ref state);
                    }
                    else if (state.crouching) {
                        CancelMelee(ref state, true);
                    }
                }
                else if (state.meleeState == MoveData.MeleeState.Recovery) {
                    if (state.meleeTimer >= movementConfig.heavyMeleeRecoveryDuration) {
                        FinishMeleeRecovery(ref state);
                    }
                }
            }
        }

        private void BeginMeleeCharge(ref MoveData state) {
            state.moveType = MoveType.HeavyMelee;
            state.meleeState = MoveData.MeleeState.Charging;
            state.meleeTimer = 0f;
            state.meleeHoldTimer = 0f;
            state.velocity *= movementConfig.heavyMeleeChargeVelocityRetention;
            state.velocity.y = 0f;
        }

        private void BeginMeleeLunge(ref MoveData state) {
            state.moveType = MoveType.HeavyMelee;
            state.meleeState = MoveData.MeleeState.Lunging;
            state.meleeTimer = 0f;
            state.hasHitTarget = false;
            state.meleeHitResolved = false;
            state.meleeHitTargetObjectId = 0;
            state.meleeHitResolveTick = InputFrame.InvalidFrame;

            Vector3 lungeDir = Quaternion.Euler(state.viewAngles.x, state.viewAngles.y, 0f) * Vector3.forward;
            if (state.grounded && lungeDir.y < 0f) {
                lungeDir.y = 0f;
                lungeDir.Normalize();
            }

            state.velocity = lungeDir * movementConfig.heavyMeleeBaseSpeed * movementConfig.heavyMeleeSpeedMultiplier;

            if (_combatDebugLogging) {
                Debug.Log($"[SurfCharacter] Melee lunge started. frame={state.frame}, origin={state.origin}, lungeDir={lungeDir}, velocity={state.velocity}, hitboxRadius={movementConfig.heavyMeleeHitbox.radius}, hitboxOffset={movementConfig.heavyMeleeHitbox.offset}, targetMask={enemyLayerMask.value}", this);
            }
        }

        private static void EnterMeleeRecovery(ref MoveData state) {
            state.moveType = MoveType.HeavyMelee;
            state.meleeState = MoveData.MeleeState.Recovery;
            state.meleeTimer = 0f;
        }

        private void CancelMelee(ref MoveData state, bool applyCooldown) {
            state.moveType = MoveType.Walk;
            state.meleeState = MoveData.MeleeState.None;
            state.meleeTimer = 0f;
            state.meleeHoldTimer = 0f;

            if (applyCooldown) {
                state.meleeCooldownTimer = movementConfig.heavyMeleeCooldown;
            }
        }

        private void FinishMeleeRecovery(ref MoveData state) {
            state.moveType = MoveType.Walk;
            state.meleeState = MoveData.MeleeState.None;
            state.meleeTimer = 0f;
            state.meleeHoldTimer = 0f;
            state.meleeCooldownTimer = movementConfig.heavyMeleeCooldown;
        }

        public bool IsParrying {
            get {
                return _moveData != null && _moveData.isParrying;
            }
        }

        public void MarkParrySuccess() {
            if (_moveData == null)
                return;

            _moveData.parrySuccessThisFrame = true;
        }

        [System.Obsolete("Legacy method. Melee hit detection now handled by Hitbox component. Use ProcessHitboxes() instead.", false)]
        private void OnMeleeHit(Hurtbox target) {
            Debug.Log($"[SurfCharacter] OnMeleeHit Triggered! Target: {target.name}");
            _moveData.hasHitTarget = true;
            
            // Stop horizontal momentum on hit
            _moveData.velocity.x = 0f;
            _moveData.velocity.z = 0f;
        }

        [System.Obsolete("Legacy method. Melee hit detection now handled by Hitbox component. Use ProcessHitboxes() instead.", false)]
        private void CheckMeleeHit()
        {
            // Removed legacy hit detection logic
        }

        private void SetTurnClamp(float val) {
            if (_playerAiming != null) {
                _playerAiming.maxTurnSpeed = val;
            }
        }

        private Quaternion _smoothedSlideRotation = Quaternion.identity;
        
        // Decoupled Visual Dash state
        private bool _wasVisualDashing = false;
        private bool _isVisualDashing = false;
        private float _visualDashProgress = 0f;
        private float _visualDashDuration = 0f;
        private Vector3 _visualDashDir = Vector3.forward;

        private static int PositiveMod(int value, int modulus) {
            int result = value % modulus;
            return result < 0 ? result + modulus : result;
        }

        private NetcodeDebugContext BuildDebugContext(int frame) {
            FishNet.Object.NetworkObject networkObject = GetComponent<FishNet.Object.NetworkObject>();

            return new NetcodeDebugContext {
                ObjectId = (networkObject != null) ? networkObject.ObjectId : -1,
                OwnerId = (networkObject != null && networkObject.Owner != null && networkObject.Owner.IsValid) ? networkObject.Owner.ClientId : -1,
                LocalClientId = (_networkedCharacter != null && _networkedCharacter.LocalConnection != null && _networkedCharacter.LocalConnection.IsValid)
                    ? _networkedCharacter.LocalConnection.ClientId
                    : -1,
                Tick = frame,
                Frame = frame,
                IsServerInitialized = _networkedCharacter != null && _networkedCharacter.IsServerInitialized,
                IsClientInitialized = _networkedCharacter != null && _networkedCharacter.IsClientInitialized,
                IsOwner = _networkedCharacter != null && _networkedCharacter.IsOwner,
                HasLocalAuthority = _networkedCharacter != null && _networkedCharacter.DebugHasLocalAuthority,
                MoveType = (_moveData != null) ? _moveData.moveType.ToString() : "Unknown"
            };
        }



    }

}
