using Fragsurf.Movement;
using UnityEngine;

namespace Fragsurf.ReplayHarness {

    public enum ReplayHarnessBootstrapMode {
        SyntheticCharacter,
        RealPlayerPrefab
    }

    public sealed class ReplayHarnessEnvironmentOptions {
        public ReplayHarnessBootstrapMode bootstrapMode = ReplayHarnessBootstrapMode.SyntheticCharacter;
        public GameObject playerPrefab;
    }

    public sealed class ReplayHarnessEnvironment {
        private readonly ReplayTraceDefinition _trace;
        private readonly ReplayHarnessEnvironmentOptions _options;
        private Vector3 _arenaOffset;
        private Vector3 _spawnOrigin;
        private bool _spawnGrounded;

        public GameObject arenaRoot { get; private set; }
        public GameObject groundObject { get; private set; }
        public GameObject characterRoot { get; private set; }
        public SurfCharacter character { get; private set; }
        public RollbackManager rollback { get; private set; }

        private ReplayHarnessEnvironment(ReplayTraceDefinition trace, ReplayHarnessEnvironmentOptions options) {
            _trace = trace;
            _options = options ?? new ReplayHarnessEnvironmentOptions();
        }

        public static ReplayHarnessEnvironment Create(ReplayTraceDefinition trace, Vector3 arenaOffset) {
            return Create(trace, arenaOffset, null);
        }

        public static ReplayHarnessEnvironment Create(ReplayTraceDefinition trace,
                                                      Vector3 arenaOffset,
                                                      ReplayHarnessEnvironmentOptions options) {
            ReplayHarnessEnvironment environment = new ReplayHarnessEnvironment(trace, options);
            environment.Build(arenaOffset);
            return environment;
        }

        public bool IsReady {
            get {
                return character != null &&
                       rollback != null &&
                       character.IsSimulationReady;
            }
        }

        public void PrepareForRun() {
            if (character == null)
                return;

            Physics.SyncTransforms();
            RefreshSpawnState();

            MoveData state = character.moveData != null
                ? character.moveData.Clone()
                : new MoveData();
            state.frame = -1;
            state.origin = _spawnOrigin;
            state.velocity = Vector3.zero;
            state.viewAngles = new Vector3(_trace.initialPitch, _trace.initialYaw, 0f);
            state.forwardMove = 0f;
            state.sideMove = 0f;
            state.upMove = 0f;
            state.verticalAxis = 0f;
            state.horizontalAxis = 0f;
            state.wishJump = false;
            state.wishJumpDown = false;
            state.lastConsumedJumpPressFrame = -1;
            state.grounded = _spawnGrounded;
            state.underwater = false;
            state.cameraUnderwater = false;
            state.climbingLadder = false;
            state.crouching = false;
            state.jumping = false;
            state.renderCrouchLerp = 0f;
            state.crouchLerp = 0f;
            state.uncrouchDown = false;
            state.laddersEnabled = !_trace.excludeLadders;
            state.slidingEnabled = false;
            state.angledLaddersEnabled = false;
            state.fallingVelocity = 0f;
            state.isDashing = false;
            state.canAirDash = true;
            state.wishDash = false;
            state.lastConsumedDashPressFrame = -1;
            state.jumpCount = 0;
            state.jumpTimer = 0f;
            state.stamina = 3f;
            state.staminaRegenTimer = 0f;
            state.dashTimer = 0f;
            state.currentDashDuration = 0f;
            state.dashCooldownTimer = 0f;
            state.sliding = false;
            state.wasSliding = false;
            state.slideSpeedCurrent = 0f;
            state.slideDirection = Vector3.forward;
            state.slideDelay = 0f;
            state.moveType = MoveType.Walk;
            state.meleeState = MoveData.MeleeState.None;
            state.meleeTimer = 0f;
            state.meleeCooldownTimer = 0f;
            state.wishMelee = false;
            state.hasHitTarget = false;
            state.dashStartedThisFrame = false;
            state.doubleJumpedThisFrame = false;
            state.meleeHitThisFrame = false;

            character.moveData = state;
            character.LoadState(state);
            character.transform.position = _spawnOrigin;
            Physics.SyncTransforms();
            rollback.Initialize(character);
        }

        public void Dispose() {
            if (arenaRoot != null)
                Object.Destroy(arenaRoot);
        }

        private void Build(Vector3 arenaOffset) {
            _arenaOffset = arenaOffset;
            arenaRoot = new GameObject("ReplayHarnessArena");

            groundObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            groundObject.name = "ReplayGround";
            groundObject.transform.SetParent(arenaRoot.transform, worldPositionStays: true);
            groundObject.transform.position = arenaOffset + new Vector3(0f, -0.5f, 0f);
            groundObject.transform.localScale = new Vector3(100f, 1f, 100f);
            groundObject.layer = 0;

            if (_options.bootstrapMode == ReplayHarnessBootstrapMode.RealPlayerPrefab && _options.playerPrefab != null) {
                BuildFromRealPlayerPrefab(arenaOffset);
            } else {
                BuildSyntheticCharacter(arenaOffset);
            }

            ConfigureCharacterForTrace();
        }

        private void BuildSyntheticCharacter(Vector3 arenaOffset) {
            characterRoot = new GameObject("ReplayCharacter");
            characterRoot.transform.SetParent(arenaRoot.transform, worldPositionStays: true);
            characterRoot.transform.position = arenaOffset + _trace.initialPosition;
            characterRoot.layer = 0;

            GameObject body = new GameObject("Body");
            body.transform.SetParent(characterRoot.transform, worldPositionStays: false);
            body.transform.localPosition = Vector3.zero;

            GameObject view = new GameObject("View");
            view.transform.SetParent(body.transform, worldPositionStays: false);
            view.transform.localPosition = new Vector3(0f, 0.9f, 0f);

            PlayerAiming aiming = view.AddComponent<PlayerAiming>();
            aiming.bodyTransform = body.transform;
            aiming.enabled = false;

            character = characterRoot.AddComponent<SurfCharacter>();
            character.viewTransform = view.transform;
            character.playerRotationTransform = body.transform;
            character.movementConfig = CreateMovementConfig();

            rollback = characterRoot.AddComponent<RollbackManager>();
        }

        private void BuildFromRealPlayerPrefab(Vector3 arenaOffset) {
            characterRoot = Object.Instantiate(_options.playerPrefab,
                                               arenaOffset + _trace.initialPosition,
                                               Quaternion.identity,
                                               arenaRoot.transform);
            characterRoot.name = "ReplayCharacter";
            characterRoot.layer = 0;

            character = characterRoot.GetComponent<SurfCharacter>();
            rollback = characterRoot.GetComponent<RollbackManager>();

            DisableBehaviourByTypeName(characterRoot, "UnityEngine.InputSystem.PlayerInput");

            if (characterRoot.TryGetComponent(out AudioListener audioListener))
                audioListener.enabled = false;
        }

        private void ConfigureCharacterForTrace() {
            if (character == null)
                return;

            character.crouchingEnabled = !_trace.excludeCrouch;
            character.slidingEnabled = false;
            character.laddersEnabled = !_trace.excludeLadders;
            character.supportAngledLadders = false;
            character.useStepOffset = false;

            if (character.movementConfig == null)
                character.movementConfig = CreateMovementConfig();
        }

        private static void DisableBehaviourByTypeName(GameObject root, string fullTypeName) {
            if (root == null || string.IsNullOrWhiteSpace(fullTypeName))
                return;

            MonoBehaviour[] behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++) {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour != null && behaviour.GetType().FullName == fullTypeName)
                    behaviour.enabled = false;
            }
        }

        private void RefreshSpawnState() {
            Vector3 requestedSpawn = _arenaOffset + _trace.initialPosition;
            float colliderHalfHeight = 1f;
            if (character != null && character.collider != null)
                colliderHalfHeight = character.collider.bounds.extents.y;

            _spawnOrigin = requestedSpawn;
            _spawnGrounded = false;

            if (Physics.Raycast(requestedSpawn,
                                Vector3.down,
                                out RaycastHit snapHit,
                                5f,
                                SurfPhysics.groundLayerMask,
                                QueryTriggerInteraction.Ignore)) {
                _spawnOrigin = snapHit.point + Vector3.up * colliderHalfHeight;
                _spawnGrounded = true;
            }
        }

        private static MovementConfig CreateMovementConfig() {
            return new MovementConfig {
                gravity = 20f,
                jumpForce = 6.5f,
                maxJumps = 2,
                doubleJumpForce = 8f,
                doubleJumpDelay = 0.2f,
                dashDuration = 0.25f,
                airDashDuration = 0.2f,
                dashCooldown = 0.45f,
                airDashVelocity = 25f,
                heavyMeleeChargeTime = 0.25f,
                heavyMeleeLungeDuration = 0.25f,
                heavyMeleeRecoveryDuration = 0.25f,
                heavyMeleeCooldown = 0.75f,
                heavyMeleeTurnClamp = 0.7f
            };
        }
    }
}
