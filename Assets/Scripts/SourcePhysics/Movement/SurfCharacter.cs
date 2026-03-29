using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Rendering;
using Fragsurf.Combat;

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
            
            _controller.playerTransform = playerRotationTransform;
            
            if (viewTransform != null) {

                _controller.camera = viewTransform;
                _controller.cameraYPos = viewTransform.localPosition.y;

            }

        }

        private PlayerAiming _playerAiming;
        private float _defaultTurnSpeed;

        private void Start () {
            
            _colliderObject = new GameObject ("PlayerCollider");
            _colliderObject.layer = gameObject.layer;
            _colliderObject.transform.SetParent (transform);
            _colliderObject.transform.rotation = Quaternion.identity;
            _colliderObject.transform.localPosition = Vector3.zero;
            _colliderObject.transform.SetSiblingIndex (0);

            // Water check
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

            if (characterRenderer == null) {
                characterRenderer = GetComponent<CharacterRenderer>();
            }

            prevPosition = transform.position;

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

            _collider = gameObject.GetComponent<Collider> ();

            if (_collider != null)
                GameObject.Destroy (_collider);

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
                meleeHitbox.ConfigureFromConfig(movementConfig.heavyMeleeHitbox, enemyLayerMask);
                meleeHitbox.Deactivate();
            }

            if (playerHurtboxComponent != null && movementConfig != null) {
                playerHurtboxComponent.ConfigureFromConfig(movementConfig.playerHurtbox);
            }

            _hasNetworkedCharacter = GetComponent<NetworkedCharacter>() != null;

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

        }

        private LocalInputCollector _inputCollector;
        private bool _hasNetworkedCharacter;
        private int _frameCounter;
        private int _lastRenderedTick = -1;
        private bool _loggedSimulationNotReady;

        public bool IsSimulationReady {
            get {
                return _colliderObject != null &&
                       _cameraWaterCheckObject != null &&
                       _cameraWaterCheck != null &&
                       _collider != null &&
                       movementConfig != null &&
                       viewTransform != null;
            }
        }

        public MoveData SimulationTick(MoveData state, InputFrame input, float deltaTime, bool allowGameplaySideEffects = true) {
            if (state == null)
                state = _moveData != null ? _moveData : new MoveData();

            if (!IsSimulationReady) {
                state.frame = input.frame;
                if (!_loggedSimulationNotReady) {
                    _loggedSimulationNotReady = true;
                    Debug.LogWarning("[SurfCharacter] SimulationTick skipped: character runtime is not initialized yet.", this);
                }
                return state;
            }

            _loggedSimulationNotReady = false;
            
            state.frame = input.frame;
            _prevState = state.Clone();
            MoveData prevStateCloned = state.Clone();

            // Clear pulse flags
            state.dashStartedThisFrame = false;
            state.doubleJumpedThisFrame = false;
            state.meleeHitThisFrame = false;

            _colliderObject.transform.rotation = Quaternion.identity;

            ApplyInputToState(ref state, input);
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

            state.cameraUnderwater = _cameraWaterCheck.IsUnderwater ();
            _cameraWaterCheckObject.transform.position = viewTransform.position;
            state.underwater = underwater;
            
            if (allowCrouch)
                _controller.Crouch (this, movementConfig, deltaTime);

            _controller.ProcessMovement (this, movementConfig, deltaTime);

            ProcessHitboxes(ref state, allowGameplaySideEffects);

            // characterRenderer.ApplyState moved to Update() to avoid redundant calls during rollback resimulations

            return state;
        }

        private void ProcessHitboxes(ref MoveData state, bool allowGameplaySideEffects) {
            if (meleeHitbox != null && meleeHitbox.isActive) {
                Hurtbox hit = meleeHitbox.CheckHit(state.origin, state);
                if (hit != null) {
                    state.hasHitTarget = true;
                    state.meleeHitThisFrame = true;
                    state.velocity.x = 0f;
                    state.velocity.z = 0f; // Halt attacker momentum
                    
                    ISurfControllable targetSurfer = hit.GetComponentInParent<ISurfControllable>();
                    if (targetSurfer != null) {
                        Vector3 attackDirection = Quaternion.Euler(state.viewAngles.x, state.viewAngles.y, 0f) * Vector3.forward;
                        attackDirection.y = 0f;
                        attackDirection.Normalize();
                        
                        targetSurfer.moveData.velocity += attackDirection * meleeHitbox.definition.hitForce;
                    }
                    if (allowGameplaySideEffects) {
                        hit.TakeHit(meleeHitbox); // Trigger VFX/audio
                    }
                }
            }
        }

        public void LoadState(MoveData savedState) {
            _moveData = savedState.Clone();
            _prevState = savedState.Clone(); // Synchronize previous state to prevent jumps after load
            // Snap transform immediately — renderer will interpolate next Update()
            if (rb != null) {
                rb.position = _moveData.origin;
            } else {
                transform.position = _moveData.origin;
            }
        }

        private void Update () {
            // Simulation is now driven by FishNet TimeManager.OnTick via NetworkedCharacter/RollbackManager.
            // Visual logic remains here:

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
                renderMesh.localPosition = _initialRenderMeshLocalPos + dashPivotOffset - dashRotation * dashPivotOffset;
            }
                
            if (playerHitbox != null) {
                playerHitbox.localRotation = dashRotation * _smoothedSlideRotation;
                playerHitbox.localPosition = _initialPlayerHitboxLocalPos + dashPivotOffset - dashRotation * dashPivotOffset;
            }
            
            // transform.position = moveData.origin;
            // prevPosition = transform.position;

            // Apply interpolation
            if (FishNet.InstanceFinder.TimeManager != null) {
                float interpolationFrac = (float)FishNet.InstanceFinder.TimeManager.GetTickPercentAsDouble();
                transform.position = Vector3.Lerp(_prevState.origin, _moveData.origin, interpolationFrac);
                
                bool isNewTick = _moveData.frame > _lastRenderedTick;
                if (characterRenderer != null) {
                    characterRenderer.ApplyState(_moveData, _prevState, isNewTick);
                }
                if (isNewTick) _lastRenderedTick = _moveData.frame;
            } else {
                transform.position = _moveData.origin;
                if (characterRenderer != null) {
                    characterRenderer.ApplyState(_moveData, _prevState, true);
                }
            }
            
            _colliderObject.transform.rotation = Quaternion.identity;
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
            state.crouching      = input.HasButton(InputFrame.BTN_CROUCH);
            
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
                state.meleeCooldownTimer -= deltaTime;
            }

            if (state.moveType == MoveType.Walk) {
                // Input Listener for Melee (Q key)
                if (state.wishMelee && state.meleeCooldownTimer <= 0f) {
                     state.moveType = MoveType.HeavyMelee;
                     state.meleeState = MoveData.MeleeState.Charging;
                     state.meleeTimer = 0f;
                     // Retain fractional momentum
                     state.velocity *= movementConfig.heavyMeleeChargeVelocityRetention;
                     state.velocity.y = 0f; // Pause vertical momentum
                     SetTurnClamp(movementConfig.heavyMeleeTurnClamp);
                }
            } else if (state.moveType == MoveType.HeavyMelee) {
                state.meleeTimer += deltaTime;
                
                if (state.meleeState == MoveData.MeleeState.Charging) {
                    bool release = !state.wishMelee || state.meleeTimer >= movementConfig.heavyMeleeChargeTime;
                    // Lunge if released generally. 
                    if (release) {
                        state.meleeState = MoveData.MeleeState.Lunging;
                        state.meleeTimer = 0f;
                        state.hasHitTarget = false;
                        
                        // Calculate Lunge Direction
                        Vector3 lungeDir = Quaternion.Euler(state.viewAngles.x, state.viewAngles.y, 0f) * Vector3.forward;
                        if (state.grounded && lungeDir.y < 0) {
                            lungeDir.y = 0;
                            lungeDir.Normalize();
                        }
                        
                        // Apply Velocity
                        state.velocity = lungeDir * movementConfig.heavyMeleeBaseSpeed * movementConfig.heavyMeleeSpeedMultiplier;
                        SetTurnClamp(movementConfig.heavyMeleeTurnClamp);

                        if (meleeHitbox != null) meleeHitbox.Activate();
                    }
                }
                else if (state.meleeState == MoveData.MeleeState.Lunging) {
                    if (state.meleeTimer > movementConfig.heavyMeleeLungeDuration || state.hasHitTarget) {
                        Debug.Log($"[SurfCharacter] Lunge Ending. Timer: {state.meleeTimer}, Hit: {state.hasHitTarget}");
                        // Natural End or Hit -> Recovery
                        state.meleeState = MoveData.MeleeState.Recovery;
                        state.meleeTimer = 0f;
                        SetTurnClamp(_defaultTurnSpeed);
                        if (meleeHitbox != null) meleeHitbox.Deactivate();
                    }
                    
                    // Cancel Check (e.g. Crouch to Slide)
                    // "HMC" - If we crouch, we break out into slide (Walk state handles this if velocity high + crouch)
                    if (state.crouching) {
                         state.moveType = MoveType.Walk;
                         state.meleeState = MoveData.MeleeState.None;
                         SetTurnClamp(_defaultTurnSpeed);
                    }
                }
                else if (state.meleeState == MoveData.MeleeState.Recovery) {
                    if (state.meleeTimer > movementConfig.heavyMeleeRecoveryDuration) {
                        state.moveType = MoveType.Walk;
                        state.meleeState = MoveData.MeleeState.None;
                        SetTurnClamp(_defaultTurnSpeed);
                        state.meleeCooldownTimer = movementConfig.heavyMeleeCooldown;

                        if (meleeHitbox != null) meleeHitbox.Deactivate();
                    }
                }
            }
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



    }

}
