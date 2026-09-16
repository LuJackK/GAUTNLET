using System.Collections.Generic;
using Fragsurf.Movement;
using Fragsurf.TraceUtil;
using NUnit.Framework;
using UnityEngine;

namespace Fragsurf.ReplayHarness {
    public class MovementMotorTests {
        private readonly List<GameObject> _objects = new List<GameObject>();
        private Motor _motor;
        private MovementConfig _config;
        private SurfController _controller;
        private const float Dt = 1f / 60f;

        private sealed class Motor : ISurfControllable {
            public MoveData moveData { get; set; }
            public MoveType moveType => moveData.moveType;
            public Collider collider { get; set; }
            public GameObject groundObject { get; set; }
            public Vector3 forward => Vector3.forward;
            public Vector3 right => Vector3.right;
            public Vector3 up => Vector3.up;
            public Vector3 baseVelocity => Vector3.zero;
        }

        private GameObject Box(string name, Vector3 position, Vector3 size, bool trigger = false) {
            GameObject go = new GameObject(name);
            go.transform.position = position;
            BoxCollider box = go.AddComponent<BoxCollider>();
            box.size = size;
            box.isTrigger = trigger;
            _objects.Add(go);
            return go;
        }

        [SetUp]
        public void SetUp() {
            Box("Floor", new Vector3(0f, -0.5f, 0f), new Vector3(200f, 1f, 200f));
            GameObject player = Box("Motor", new Vector3(0f, 1f, 0f), new Vector3(1f, 2f, 1f), true);
            _motor = new Motor {
                collider = player.GetComponent<Collider>(),
                moveData = new MoveData {
                    origin = new Vector3(0f, 1f, 0f), grounded = true,
                    slidingEnabled = true, crouchingHeight = 0.5f
                }
            };
            _config = new MovementConfig();
            _controller = new SurfController();
            Physics.SyncTransforms();
        }

        [TearDown]
        public void TearDown() {
            foreach (GameObject go in _objects) Object.DestroyImmediate(go);
            _objects.Clear();
        }

        private void Tick() => _controller.ProcessMovement(_motor, _config, Dt);

        [Test]
        public void FastSlideJump_LeavesSlideAndRetainsMomentum() {
            MoveData s = _motor.moveData;
            s.velocity = new Vector3(0f, 0f, 30f);
            s.sliding = s.wasSliding = s.slideRequested = true;
            s.slideSpeedCurrent = 30f;
            s.wishJump = s.wishJumpDown = true;
            Tick();
            Assert.IsFalse(s.sliding);
            Assert.IsFalse(s.wasSliding);
            Assert.IsFalse(s.grounded);
            Assert.AreEqual(30f, s.velocity.z, 0.001f);
            Assert.Greater(s.velocity.y, 0f);
        }

        [Test]
        public void DashAndJump_SameTick_ComposeAndSpendOnce() {
            MoveData s = _motor.moveData;
            s.wishDash = s.wishJump = s.wishJumpDown = true;
            Tick();
            Assert.AreEqual(2f, s.stamina, 0.001f);
            Assert.AreEqual(_config.dashImpulse, s.velocity.z, 0.001f);
            Assert.Greater(s.velocity.y, 0f);
            Assert.IsFalse(s.isDashing);
        }

        [Test]
        public void SlideEntry_DoesNotMultiplyOrClampCarriedSpeed() {
            MoveData s = _motor.moveData;
            s.velocity = Vector3.forward * 30f;
            s.slideRequested = true;
            Tick();
            Assert.IsTrue(s.sliding);
            Assert.AreEqual(30f - _config.slideDrag * Dt, s.velocity.z, 0.001f);
        }

        [Test]
        public void SlideTimers_ExpireWhileSliding() {
            MoveData s = _motor.moveData;
            s.velocity = Vector3.forward * 20f;
            s.slideRequested = true;
            s.isDashing = true;
            s.dashTimer = s.dashCooldownTimer = Dt * 0.5f;
            Tick();
            Assert.IsTrue(s.sliding);
            Assert.IsFalse(s.isDashing);
            Assert.Zero(s.dashCooldownTimer);
        }

        [Test]
        public void AirSteering_IsGradualAndPreservesForwardMomentum() {
            MoveData s = _motor.moveData;
            s.origin.y = 10f;
            s.grounded = false;
            s.velocity = Vector3.forward * 25f;
            s.horizontalAxis = 1f;
            Tick();
            Assert.Greater(s.velocity.x, 0f);
            Assert.Greater(s.velocity.z, 24f);
        }

        [Test]
        public void NonAutoBhop_HeldJumpDoesNotRetrigger() {
            _config.autoBhop = false;
            _motor.moveData.wishJump = true;
            Tick();
            Assert.IsTrue(_motor.moveData.grounded);
            Assert.Zero(_motor.moveData.jumpCount);
        }

        private Vector3 SimulateAir(float strength, int ticks, float yawPerTick = 0f, float axis = 1f) {
            _config.sourceAirStrafeStrength = strength;
            _config.gravity = 0f;
            _motor.moveData = new MoveData {
                origin = Vector3.up * 100f, grounded = false,
                velocity = Vector3.forward * 25f, horizontalAxis = axis
            };
            for (int i = 0; i < ticks; i++) {
                _motor.moveData.viewAngles = new Vector3(0f, yawPerTick * i, 0f);
                Tick();
            }
            return _motor.moveData.velocity;
        }

        [Test]
        public void SourceStrafe_ZeroMatchesOriginalAirMovementExactly() {
            Assert.Zero(_config.sourceAirStrafeStrength);
            // Frozen pre-feature air algorithm, replayed across changing look/input.
            Vector3 expected = Vector3.forward * 25f;
            _config.gravity = 0f;
            _motor.moveData.origin = Vector3.up * 100f;
            _motor.moveData.grounded = false;
            _motor.moveData.velocity = expected;
            for (int i = 0; i < 120; i++) {
                float horizontal = i % 3 == 0 ? -0.5f : 1f;
                float vertical = i % 4 == 0 ? 0.3f : 0f;
                float yaw = i * 2f;
                Vector3 wish = Vector3.ClampMagnitude(Quaternion.Euler(0f, yaw, 0f) *
                    new Vector3(horizontal, 0f, vertical), 1f);
                float speed = expected.magnitude;
                float heading = Mathf.Atan2(expected.x, expected.z) * Mathf.Rad2Deg;
                float target = Mathf.Atan2(wish.x, wish.z) * Mathf.Rad2Deg;
                float angle = Mathf.MoveTowardsAngle(heading, target,
                    (_config.airControlAcceleration * Dt) * wish.magnitude / Mathf.Max(speed, 1f) * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                expected = new Vector3(Mathf.Sin(angle) * speed, 0f, Mathf.Cos(angle) * speed);
                Vector3 direction = wish / wish.magnitude;
                float missing = _config.maxSpeed * wish.magnitude - Vector3.Dot(expected, direction);
                if (missing > 0f)
                    expected += direction * Mathf.Min(missing, (_config.airAcceleration * Dt) * wish.magnitude);
                expected = Vector3.ClampMagnitude(expected, _config.maxVelocity);
                _motor.moveData.horizontalAxis = horizontal;
                _motor.moveData.verticalAxis = vertical;
                _motor.moveData.viewAngles = new Vector3(0f, yaw, 0f);
                Tick();
                Assert.AreEqual(expected, _motor.moveData.velocity, "Tick " + i);
            }
        }

        [Test]
        public void SourceStrafe_StrengthProgressivelyReducesHeldSidewaysTurning() {
            Vector3 current = SimulateAir(0f, 60);
            Vector3 middle = SimulateAir(0.5f, 60);
            Vector3 source = SimulateAir(1f, 60);
            Assert.Greater(current.x, middle.x);
            Assert.Greater(middle.x, source.x);
            Assert.AreEqual(_config.sourceAirSpeedCap, source.x, 0.00001f);
            Assert.AreEqual(25f, source.z, 0.00001f);
        }

        [Test]
        public void SourceStrafe_CameraTurnAllowsFurtherAccelerationAfterDirectionalCap() {
            Vector3 held = SimulateAir(1f, 60);
            // With right strafe held, turn right so the wish direction stays
            // just behind perpendicular to the carried forward velocity.
            Vector3 turning = SimulateAir(1f, 60, 0.4f);
            Assert.Greater(turning.x, held.x + 1f);
            Vector3 wrongWay = SimulateAir(1f, 60, -0.4f);
            Assert.LessOrEqual(wrongWay.x, held.x);
        }

        [TestCase(0f)]
        [TestCase(0.5f)]
        [TestCase(1f)]
        public void SourceStrafe_NoInputPreservesMomentum(float strength) {
            Assert.AreEqual(Vector3.forward * 25f, SimulateAir(strength, 60, axis: 0f));
        }

        [Test]
        public void SourceStrafe_CapIsConfigurableAndDoesNotTruncateCarriedSpeed() {
            _config.sourceAirSpeedCap = 1f;
            Assert.AreEqual(1f, SimulateAir(1f, 60).x, 0.00001f);
            _motor.moveData.horizontalAxis = 0f;
            _motor.moveData.verticalAxis = 1f;
            Tick();
            Assert.AreEqual(25f, _motor.moveData.velocity.z, 0.00001f);
        }

        [Test]
        public void SourceStrafe_AnalogInputUsesCapAfterWishSpeedScaling() {
            Assert.AreEqual(_config.sourceAirSpeedCap, SimulateAir(1f, 60, axis: 0.5f).x, 0.00001f);
        }

        [Test]
        public void SourceStrafe_SnapshotReplayWithRecreatedControllerMatches() {
            SimulateAir(0.75f, 15);
            MoveData snapshot = _motor.moveData.Clone();
            for (int i = 0; i < 30; i++) Tick();
            Vector3 expectedVelocity = _motor.moveData.velocity;
            Vector3 expectedOrigin = _motor.moveData.origin;
            _motor.moveData = snapshot;
            for (int i = 0; i < 30; i++) {
                _controller = new SurfController();
                Tick();
            }
            Assert.AreEqual(expectedVelocity, _motor.moveData.velocity);
            Assert.AreEqual(expectedOrigin, _motor.moveData.origin);
        }

        [Test]
        public void LandingWhileHoldingCrouch_ReentersSlideWithAirMomentum() {
            MoveData s = _motor.moveData;
            s.grounded = false;
            s.velocity = new Vector3(0f, -1f, 24f);
            s.slideRequested = true;
            Tick();
            Assert.IsTrue(s.sliding);
            Assert.Greater(s.velocity.z, 23f);
        }

        [Test]
        public void ThinWall_StopsFastSlideAndDoesNotRestoreStaleSpeed() {
            Box("ThinWall", new Vector3(0f, 2f, 1f), new Vector3(10f, 4f, 0.02f));
            Physics.SyncTransforms();
            MoveData s = _motor.moveData;
            s.velocity = Vector3.forward * 50f;
            s.slideRequested = true;
            Tick();
            Assert.Less(s.origin.z, 0.51f);
            Assert.AreEqual(0f, s.velocity.z, 0.001f);
            Tick();
            Assert.Less(s.origin.z, 0.51f);
            Assert.IsFalse(s.sliding);
        }

        [Test]
        public void CrouchReplay_RestoresShapeAndKeepsFeetFixed() {
            MoveData snapshot = _motor.moveData.Clone();
            _motor.moveData.slideRequested = true;
            _controller.Crouch(_motor, _config, Dt);
            Assert.IsTrue(_motor.moveData.crouching);
            Assert.AreEqual(0.5f, _motor.moveData.origin.y, 0.001f);
            _motor.moveData = snapshot;
            _controller.Crouch(_motor, _config, Dt);
            Assert.AreEqual(2f, ((BoxCollider)_motor.collider).size.y);
            Assert.AreEqual(1f, _motor.moveData.origin.y);
        }

        [Test]
        public void LowCeiling_PreventsUncrouch() {
            _motor.moveData.slideRequested = true;
            _controller.Crouch(_motor, _config, Dt);
            Box("Ceiling", new Vector3(0f, 1.6f, 0f), new Vector3(3f, 0.2f, 3f));
            Physics.SyncTransforms();
            _motor.moveData.slideRequested = false;
            _controller.Crouch(_motor, _config, Dt);
            Assert.IsTrue(_motor.moveData.crouching);
            Assert.AreEqual(1f, ((BoxCollider)_motor.collider).size.y);
        }

        [Test]
        public void CapsuleTrace_UsesFullHeight() {
            GameObject go = new GameObject("Capsule");
            _objects.Add(go);
            CapsuleCollider cap = go.AddComponent<CapsuleCollider>();
            cap.isTrigger = true;
            cap.height = 2f;
            cap.radius = 0.5f;
            Trace trace = Tracer.TraceCollider(cap, new Vector3(3f, 1.02f, 0f),
                new Vector3(3f, 0.92f, 0f), SurfPhysics.groundLayerMask);
            Assert.IsNotNull(trace.hitCollider);
            Assert.AreEqual(0.02f, trace.distance, 0.005f);
        }

        [TestCase(-12f)]
        [TestCase(8f)]
        public void MeleeCharge_HoversWithoutFreezingDashCooldown(float verticalSpeed) {
            MoveData s = _motor.moveData;
            s.origin.y = 10f;
            s.grounded = false;
            s.moveType = MoveType.HeavyMelee;
            s.meleeState = MoveData.MeleeState.Charging;
            s.velocity = new Vector3(0f, verticalSpeed, 10f);
            s.dashCooldownTimer = 0.1f;
            Tick();
            Assert.Zero(s.velocity.y);
            Assert.AreEqual(10f, s.origin.y, 0.0001f);
            Assert.Greater(s.origin.z, 0f);
            Assert.Less(s.dashCooldownTimer, 0.1f);
        }

        [TestCase(MoveData.MeleeState.Lunging)]
        [TestCase(MoveData.MeleeState.Recovery)]
        [TestCase(MoveData.MeleeState.None)]
        public void LeavingCharge_RestoresGravity(MoveData.MeleeState phase) {
            MoveData s = _motor.moveData;
            s.origin.y = 10f;
            s.grounded = false;
            s.moveType = MoveType.HeavyMelee;
            s.meleeState = MoveData.MeleeState.Charging;
            Tick();
            s.meleeState = phase;
            if (phase == MoveData.MeleeState.None) s.moveType = MoveType.Walk;
            Tick();
            Assert.AreEqual(-_config.gravity * Dt, s.velocity.y, 0.0001f);
            Assert.Less(s.origin.y, 10f);
        }

        [Test]
        public void ChargeHover_ReplaysFromSnapshotWithoutControllerHistory() {
            MoveData s = _motor.moveData;
            s.origin.y = 10f;
            s.grounded = false;
            s.moveType = MoveType.HeavyMelee;
            s.meleeState = MoveData.MeleeState.Charging;
            s.velocity = new Vector3(0f, -8f, 15f);
            MoveData snapshot = s.Clone();
            for (int i = 0; i < 20; i++) Tick();
            MoveData expected = s.Clone();
            _motor.moveData = snapshot.Clone();
            _controller = new SurfController();
            for (int i = 0; i < 20; i++) Tick();
            Assert.AreEqual(expected.origin, _motor.moveData.origin);
            Assert.AreEqual(expected.velocity, _motor.moveData.velocity);
            Assert.AreEqual(10f, _motor.moveData.origin.y, 0.0001f);
        }

        [Test]
        public void LegacyEnvironmentFlags_DoNotSelectDifferentPhysics() {
            MoveData initial = _motor.moveData.Clone();
            initial.velocity = Vector3.forward * 12f;
            _motor.moveData = initial.Clone();
            Tick();
            MoveData expected = _motor.moveData.Clone();
            _motor.moveData = initial.Clone();
            _motor.moveData.underwater = _motor.moveData.climbingLadder = _motor.moveData.laddersEnabled = true;
            Tick();
            Assert.AreEqual(expected.origin, _motor.moveData.origin);
            Assert.AreEqual(expected.velocity, _motor.moveData.velocity);
        }

        [Test]
        public void SnapshotReplay_AfterUnrelatedState_ProducesIdenticalSlideJump() {
            _motor.moveData.velocity = Vector3.forward * 24f;
            _motor.moveData.slideRequested = true;
            Tick();
            MoveData snapshot = _motor.moveData.Clone();
            _motor.moveData.wishJump = true;
            Tick();
            MoveData expected = _motor.moveData.Clone();
            _motor.moveData = new MoveData { origin = new Vector3(10f, 5f, 0f), velocity = Vector3.left * 20f };
            Tick();
            _motor.moveData = snapshot.Clone();
            _motor.moveData.wishJump = true;
            _controller = new SurfController();
            Tick();
            Assert.AreEqual(expected.origin, _motor.moveData.origin);
            Assert.AreEqual(expected.velocity, _motor.moveData.velocity);
            Assert.AreEqual(expected.stamina, _motor.moveData.stamina);
            Assert.AreEqual(expected.sliding, _motor.moveData.sliding);
        }

        [Test]
        public void WallContact_PreservesTangentialMotion() {
            Box("Wall", new Vector3(0f, 2f, 1f), new Vector3(10f, 4f, 0.1f));
            Physics.SyncTransforms();
            _motor.moveData.velocity = new Vector3(15f, 0f, 40f);
            _motor.moveData.slideRequested = true;
            Tick();
            Assert.Greater(_motor.moveData.origin.x, 0.2f);
            Assert.Less(_motor.moveData.origin.z, 0.46f);
            Assert.Greater(_motor.moveData.velocity.x, 14f);
            Assert.AreEqual(0f, _motor.moveData.velocity.z, 0.001f);
        }

        [Test]
        public void SmallStep_IsTraversedWhenEnabled() {
            Box("Step", new Vector3(0f, 0.1f, 1f), new Vector3(5f, 0.2f, 1f));
            Physics.SyncTransforms();
            _motor.moveData.useStepOffset = true;
            _motor.moveData.stepOffset = 0.3f;
            _motor.moveData.velocity = Vector3.forward * 15f;
            _motor.moveData.slideRequested = true;
            Tick();
            Assert.Greater(_motor.moveData.origin.y, 1.15f);
            Assert.Greater(_motor.moveData.origin.z, 0.2f);
        }

        [Test]
        public void Depenetration_DoesNotPushLiveRigidbody() {
            GameObject crate = Box("Crate", new Vector3(0.6f, 1f, 0f), Vector3.one);
            Rigidbody body = crate.AddComponent<Rigidbody>();
            body.useGravity = false;
            Physics.SyncTransforms();
            _motor.moveData.velocity = Vector3.right * 10f;
            Tick();
            Assert.AreEqual(Vector3.zero, body.linearVelocity);
            Assert.LessOrEqual(_motor.moveData.velocity.x, 0.001f);
        }

        [Test]
        public void HeldBhop_PreservesMomentumAcrossMultipleLandings() {
            MoveData s = _motor.moveData;
            s.velocity = Vector3.forward * 24f;
            s.wishJump = s.slideRequested = true;
            int takeoffs = 0;
            for (int i = 0; i < 150; i++) {
                float previousY = s.velocity.y;
                Tick();
                if (s.velocity.y > 0f && previousY <= 0f) takeoffs++;
                Assert.IsFalse(s.sliding);
            }
            Assert.GreaterOrEqual(takeoffs, 3);
            Assert.AreEqual(24f, s.velocity.z, 0.001f);
        }

        [Test]
        public void WalkingOffLedge_WithSingleJumpLimit_DoesNotGrantDoubleJump() {
            _config.maxJumps = 1;
            _motor.moveData.origin.y = 8f;
            _motor.moveData.grounded = false;
            _motor.moveData.jumpTimer = 1f;
            _motor.moveData.wishJumpDown = true;
            Tick();
            Assert.Less(_motor.moveData.velocity.y, 0f);
            Assert.AreEqual(3f, _motor.moveData.stamina);
        }
    }
}
