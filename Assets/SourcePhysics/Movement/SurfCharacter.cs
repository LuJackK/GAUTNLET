using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Rendering;

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

        [Header("VFX")]
        public ParticleSystem slideTrailParticles;
        public ParticleSystem slideSparksParticles;
        public GameObject dashPoofPrefab;
        public GameObject jumpPoofPrefab;
        public ParticleSystem gauntletFlameParticles;
        
        [Header("VFX Spawn Points")]
        public Transform gloveVfxSpawnPoint;  // For gauntlet flames
        public Transform lowerVfxSpawnPoint;  // For feet/slide effects
        public Transform middleVfxSpawnPoint; // For body/center effects (dash, jump)

        [Header("Sliding Visuals")]
        public Transform renderMesh;
        public Transform playerHitbox;

        [Header("Animation")]
        public Animator animator;
        public string speedParameterName = "Speed";
        public string groundedParameterName = "Grounded";
        public string jumpingParameterName = "Jumping";
        public string crouchingParameterName = "Crouching";
        public string slidingParameterName = "Sliding";
        public string punchingParameterName = "Punching";

        [Header ("Movement Config")]
        [SerializeField]
        public MovementConfig movementConfig;
        
        private GameObject _groundObject;
        private Vector3 _baseVelocity;
        private Collider _collider;
        private Vector3 _angles;
        private Vector3 _startPosition;
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

        private MoveType _moveType = MoveType.Walk;
        public MoveType moveType { get { return _moveType; } }
        public MovementConfig moveConfig { get { return movementConfig; } }
        public MoveData moveData { get { return _moveData; } }
        public new Collider collider { get { return _collider; } }

        public GameObject groundObject {

            get { return _groundObject; }
            set { _groundObject = value; }

        }

        public Vector3 baseVelocity { get { return _baseVelocity; } }

        public Vector3 forward { get { return viewTransform.forward; } }
        public Vector3 right { get { return viewTransform.right; } }
        public Vector3 up { get { return viewTransform.up; } }

        Vector3 prevPosition;

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
            
            _cameraWaterCheck = _cameraWaterCheckObject.AddComponent<CameraWaterCheck> ();

            prevPosition = transform.position;

            if (viewTransform == null)
                viewTransform = Camera.main.transform;

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

        }

        private void Update () {

            _colliderObject.transform.rotation = Quaternion.identity;


            //UpdateTestBinds ();
            UpdateMoveData ();
            UpdateMeleeLogic(); // Handle state transitions
            
            
            // Previous movement code
            Vector3 positionalMovement = transform.position - prevPosition;
            transform.position = prevPosition;
            moveData.origin += positionalMovement;

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

            _moveData.cameraUnderwater = _cameraWaterCheck.IsUnderwater ();
            _cameraWaterCheckObject.transform.position = viewTransform.position;
            moveData.underwater = underwater;
            
            if (allowCrouch)
                _controller.Crouch (this, movementConfig, Time.deltaTime);

            _controller.ProcessMovement (this, movementConfig, Time.deltaTime);

            // Sliding Tilt
            if (slidingEnabled && movementConfig != null) {
                Quaternion targetRotation = Quaternion.identity;
                if (_controller.IsSliding) {
                    Vector3 slideDir = _controller.SlideDirection;
                    if (slideDir.sqrMagnitude > 0.001f && renderMesh != null) {
                        // Calculate tilt relative to the mesh's parent orientation
                        Vector3 localSlideDir = renderMesh.parent.InverseTransformDirection(slideDir);
                        Vector3 localRightDir = Vector3.Cross(Vector3.up, localSlideDir).normalized;
                        
                        // Use negative angle to tilt back (pitch up)
                        targetRotation = Quaternion.AngleAxis(-movementConfig.slideTiltAngle, localRightDir);
                    }
                }

                if (renderMesh != null)
                    renderMesh.localRotation = Quaternion.Slerp(renderMesh.localRotation, targetRotation, Time.deltaTime * movementConfig.slideTiltSpeed);
                
                if (playerHitbox != null)
                    playerHitbox.localRotation = Quaternion.Slerp(playerHitbox.localRotation, targetRotation, Time.deltaTime * movementConfig.slideTiltSpeed);
            }

            // Debug Logging (Every Frame)
            bool isPunching = _moveType == MoveType.HeavyMelee;
            // Debug.Log($"Grounded: {_moveData.grounded}, Jumping: {_controller.jumping}, Crouching: {_moveData.crouching}, Sliding: {_controller.IsSliding}, Punching: {isPunching}");

            if (animator != null) {
                float horizontalSpeed = new Vector3(_moveData.velocity.x, 0f, _moveData.velocity.z).magnitude;
                animator.SetFloat(speedParameterName, horizontalSpeed);

                animator.SetBool(groundedParameterName, _moveData.grounded);
                animator.SetBool(jumpingParameterName, _controller.jumping);
                animator.SetBool(crouchingParameterName, _moveData.crouching);
                animator.SetBool(slidingParameterName, _controller.IsSliding);
                animator.SetBool(punchingParameterName, _moveType == MoveType.HeavyMelee);
                animator.SetBool(punchingParameterName, _moveType == MoveType.HeavyMelee);
            }
            
            // --- VFX Logic ---
            UpdateVFX();

            transform.position = moveData.origin;
            prevPosition = transform.position;

            _colliderObject.transform.rotation = Quaternion.identity;

        }
        
        private void UpdateTestBinds () {

            if (Input.GetKeyDown (KeyCode.Backspace))
                ResetPosition ();

        }

        private void ResetPosition () {
            
            moveData.velocity = Vector3.zero;
            moveData.origin = _startPosition;

        }

        private void UpdateMoveData () {
            
            _moveData.verticalAxis = Input.GetAxisRaw ("Vertical");
            _moveData.horizontalAxis = Input.GetAxisRaw ("Horizontal");

            if (Input.GetButtonDown("Sprint"))
                _moveData.wishDash = true;
            else if (!Input.GetButton("Sprint"))
                _moveData.wishDash = false;
            
            if (Input.GetButtonDown ("Crouch"))
                _moveData.crouching = true;

            if (!Input.GetButton ("Crouch"))
                _moveData.crouching = false;
            
            bool moveLeft = _moveData.horizontalAxis < 0f;
            bool moveRight = _moveData.horizontalAxis > 0f;
            bool moveFwd = _moveData.verticalAxis > 0f;
            bool moveBack = _moveData.verticalAxis < 0f;
            bool jump = Input.GetButton ("Jump");

            if (!moveLeft && !moveRight)
                _moveData.sideMove = 0f;
            else if (moveLeft)
                _moveData.sideMove = -moveConfig.acceleration;
            else if (moveRight)
                _moveData.sideMove = moveConfig.acceleration;

            if (!moveFwd && !moveBack)
                _moveData.forwardMove = 0f;
            else if (moveFwd)
                _moveData.forwardMove = moveConfig.acceleration;
            else if (moveBack)
                _moveData.forwardMove = -moveConfig.acceleration;
            
            if (Input.GetButtonDown ("Jump")) {
                _moveData.wishJump = true;
                _moveData.wishJumpDown = true;
            } else {
                _moveData.wishJumpDown = false;
            }

            if (!Input.GetButton ("Jump"))
                _moveData.wishJump = false;
            
            _moveData.viewAngles = _angles;

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

        private void OnTriggerEnter (Collider other) {
            
            if (!triggers.Contains (other))
                triggers.Add (other);

        }

        private void OnTriggerExit (Collider other) {
            
            if (triggers.Contains (other))
                triggers.Remove (other);

        }

        private void OnCollisionStay (Collision collision) {

            if (collision.rigidbody == null)
                return;

            Vector3 relativeVelocity = collision.relativeVelocity * collision.rigidbody.mass / 50f;
            Vector3 impactVelocity = new Vector3 (relativeVelocity.x * 0.0025f, relativeVelocity.y * 0.00025f, relativeVelocity.z * 0.0025f);

            float maxYVel = Mathf.Max (moveData.velocity.y, 10f);
            Vector3 newVelocity = new Vector3 (moveData.velocity.x + impactVelocity.x, Mathf.Clamp (moveData.velocity.y + Mathf.Clamp (impactVelocity.y, -0.5f, 0.5f), -maxYVel, maxYVel), moveData.velocity.z + impactVelocity.z);

            newVelocity = Vector3.ClampMagnitude (newVelocity, Mathf.Max (moveData.velocity.magnitude, 30f));
            moveData.velocity = newVelocity;

        }

        private void UpdateMeleeLogic() {
            if (_moveType == MoveType.Walk) {
                // Input Listener for Melee (Q key)
                if (Input.GetKeyDown(KeyCode.Q)) {
                     _moveType = MoveType.HeavyMelee;
                     _moveData.meleeState = MoveData.MeleeState.Charging;
                     _moveData.meleeTimer = 0f;
                     // Retain fractional momentum
                     _moveData.velocity *= movementConfig.heavyMeleeChargeVelocityRetention;
                     _moveData.velocity.y = 0f; // Pause vertical momentum
                     SetTurnClamp(movementConfig.heavyMeleeTurnClamp);
                }
            } else if (_moveType == MoveType.HeavyMelee) {
                _moveData.meleeTimer += Time.deltaTime;
                
                if (_moveData.meleeState == MoveData.MeleeState.Charging) {
                    bool release = Input.GetKeyUp(KeyCode.Q) || _moveData.meleeTimer >= movementConfig.heavyMeleeChargeTime;
                    // Lunge if released generally. 
                    if (release) {
                        _moveData.meleeState = MoveData.MeleeState.Lunging;
                        _moveData.meleeTimer = 0f;
                        _moveData.hasHitTarget = false;
                        
                        // Calculate Lunge Direction
                        Vector3 lungeDir = viewTransform.forward;
                        if (_moveData.grounded && lungeDir.y < 0) {
                            lungeDir.y = 0;
                            lungeDir.Normalize();
                        }
                        
                        // Apply Velocity
                        moveData.velocity = lungeDir * movementConfig.heavyMeleeBaseSpeed * movementConfig.heavyMeleeSpeedMultiplier;
                        SetTurnClamp(movementConfig.heavyMeleeTurnClamp); 
                    }
                }
                else if (_moveData.meleeState == MoveData.MeleeState.Lunging) {
                    if (_moveData.meleeTimer > movementConfig.heavyMeleeLungeDuration) {
                        // Natural End -> Recovery
                        _moveData.meleeState = MoveData.MeleeState.Recovery;
                        _moveData.meleeTimer = 0f;
                        SetTurnClamp(_defaultTurnSpeed);
                    }
                    
                    if (!_moveData.hasHitTarget) CheckMeleeHit();

                    // Cancel Check (e.g. Crouch to Slide)
                    // "HMC" - If we crouch, we break out into slide (Walk state handles this if velocity high + crouch)
                    if (Input.GetButtonDown("Crouch") || Input.GetButton("Crouch")) { // Using GetButton for hold check
                         _moveType = MoveType.Walk;
                         _moveData.meleeState = MoveData.MeleeState.None;
                         _moveData.crouching = true; // Ensure crouch bit is set
                         SetTurnClamp(_defaultTurnSpeed);
                    }
                }
                else if (_moveData.meleeState == MoveData.MeleeState.Recovery) {
                    if (_moveData.meleeTimer > movementConfig.heavyMeleeRecoveryDuration) {
                        _moveType = MoveType.Walk;
                        _moveData.meleeState = MoveData.MeleeState.None;
                        SetTurnClamp(_defaultTurnSpeed);
                    }
                }
            }
        }

        private void CheckMeleeHit()
        {
            float range = movementConfig.heavyMeleeHitRange;
            float angleThreshold = movementConfig.heavyMeleeConeAngle;
            int layerMask = SurfPhysics.groundLayerMask; // Ideally enemy layer, but using ground for now or default? User said "enemyLayerMask". I'll use Default or All for now, or check for specific component.
            // Actually user code example: `enemyLayerMask`. I don't have it defined. I'll search for all colliders and filter.
            
            Collider[] hits = Physics.OverlapSphere(viewTransform.position + viewTransform.forward * range, range); // Center sphere further out? "Cast a sphere at the end of the lunge distance"
            // User: "Sphere Cast: Cast a sphere at the end of the lunge distance"
            // Actually Physics.OverlapSphere(transform.position, meleeRange) in user code.
            
            hits = Physics.OverlapSphere(transform.position, range);

            foreach (var hit in hits) {
                if (hit.transform == transform || hit.transform.IsChildOf(transform)) continue; // Don't hit self
                
                Vector3 dirToTarget = (hit.transform.position - transform.position).normalized;
                Vector3 lungeDir = moveData.velocity.normalized;
                
                if (Vector3.Dot(lungeDir, dirToTarget) > angleThreshold) {
                    // Hit!
                    // Apply Damage (Stub)
                    Debug.Log("Hit " + hit.name);
                    
                    // Add force to target?
                    Rigidbody targetRb = hit.GetComponent<Rigidbody>();
                    if (targetRb) {
                        targetRb.AddForce(lungeDir * 10f, ForceMode.Impulse);
                    }
                    
                    _moveData.hasHitTarget = true; 
                    // Should we stop lunge? User doesn't say. "TriggerImpactFrame".
                    // Only hit once per lunge? User code iterates all hits.
                }
            }
        }

        private void SetTurnClamp(float val) {
            if (_playerAiming != null) {
                _playerAiming.maxTurnSpeed = val;
            }
        }

        private int _prevJumpCount = 0;
        private bool _wasDashing = false;

        private void UpdateVFX() {
            // Sliding
            // Ensure particles are assigned to avoid ref errors
            if (slideTrailParticles != null) {
                if (_controller.IsSliding && !slideTrailParticles.isPlaying) slideTrailParticles.Play();
                else if (!_controller.IsSliding && slideTrailParticles.isPlaying) slideTrailParticles.Stop();
            }

            if (slideSparksParticles != null) {
                if (_controller.IsSliding && !slideSparksParticles.isPlaying) slideSparksParticles.Play();
                else if (!_controller.IsSliding && slideSparksParticles.isPlaying) slideSparksParticles.Stop();
            }

            // Punching (Gauntlet Flames)
            // Active during HeavyMelee mode
            if (gauntletFlameParticles != null) {
                if (_moveType == MoveType.HeavyMelee && !gauntletFlameParticles.isPlaying) {
                     gauntletFlameParticles.Play();
                     // Optional: If you want to parent them to hands specifically or just play a single system
                     // If the system is already on the player, Play() works. 
                     // If it depends on left/right hand usage, we might need more logic, but for "Punching mode" generally:
                }
                else if (_moveType != MoveType.HeavyMelee && gauntletFlameParticles.isPlaying) {
                     gauntletFlameParticles.Stop();
                }
            }

            // Dashing (Poof Cloud)
            // Detect rising edge of isDashing
            if (_moveData.isDashing && !_wasDashing) {
                if (dashPoofPrefab != null) {
                    Transform spawn = middleVfxSpawnPoint != null ? middleVfxSpawnPoint : transform;
                    Instantiate(dashPoofPrefab, spawn.position, spawn.rotation);
                }
            }
            _wasDashing = _moveData.isDashing;

            // Double Jump (Poof Cloud)
            // Detect increment in jump count > 1
            if (_moveData.jumpCount > _prevJumpCount && _moveData.jumpCount > 1) {
                if (jumpPoofPrefab != null) {
                     Transform spawn = middleVfxSpawnPoint != null ? middleVfxSpawnPoint : transform;
                     Instantiate(jumpPoofPrefab, spawn.position, spawn.rotation);
                }
            }
            _prevJumpCount = _moveData.jumpCount;
        }

    }

}

