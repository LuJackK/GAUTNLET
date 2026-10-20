using System.Collections.Generic;
using Fragsurf.Movement;
using NUnit.Framework;
using UnityEngine;

namespace Fragsurf.ReplayHarness {
    // Deadlock movement model against values measured in Deadlock captures
    // (Research/DeadlockMovement/BEHAVIOURS.md, "Capture checks"). Runs at Deadlock's 64 Hz and
    // uses Mina's hero values, which the captures recorded. Units: 1 Source unit = 0.0254 m.
    public class DeadlockMovementTests {
        private const float Dt = 1f / 64f;
        private const float U = DeadlockMovementConfig.Unit;
        private const float MinaWalk = 318.909f * U;
        private readonly List<GameObject> _objects = new List<GameObject>();
        private Motor _motor;
        private MovementConfig _config;
        private SurfController _controller;

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
            // 200 m like the other motor tests: the ground sweep misses a 400 m box in both models.
            Box("Floor", new Vector3(0f, -0.5f, 0f), new Vector3(200f, 1f, 200f));
            GameObject player = Box("Motor", new Vector3(0f, 1f, 0f), new Vector3(1f, 2f, 1f), true);
            _motor = new Motor {
                collider = player.GetComponent<Collider>(),
                moveData = new MoveData {
                    origin = new Vector3(0f, 1f, 0f), grounded = true, slidingEnabled = true,
                    crouchingHeight = 0.5f, stamina = 3f, canAirDash = true
                }
            };
            _config = new MovementConfig { movementModel = MovementModel.Deadlock };
            _config.deadlock.walkSpeed = MinaWalk;
            _config.deadlock.groundDashDuration = 0.62f;
            _config.deadlock.airDashDuration = 0.43f;
            _config.dashCooldown = 0f;
            _controller = new SurfController();
            Physics.SyncTransforms();
            S.jumpTimer = 1f;
        }

        [TearDown]
        public void TearDown() {
            foreach (GameObject go in _objects) Object.DestroyImmediate(go);
            _objects.Clear();
        }

        private MoveData S => _motor.moveData;
        private float Speed => new Vector3(S.velocity.x, 0f, S.velocity.z).magnitude;

        private void Tick() {
            _controller.ProcessMovement(_motor, _config, Dt);
            S.wishJumpDown = S.wishDash = false;
            S.dashStartedThisFrame = S.doubleJumpedThisFrame = false;
        }

        private void Airborne(Vector3 velocity) {
            S.origin = new Vector3(0f, 60f, 0f);
            S.grounded = false;
            S.velocity = velocity;
            Physics.SyncTransforms();
        }

        [Test]
        public void LegacyRemainsTheDefaultModel() {
            Assert.AreEqual(MovementModel.Legacy, new MovementConfig().movementModel);
        }

        [Test]
        public void WalkFromRestGains25UnitsPerTickAndReaches95PercentIn23Ticks() {
            // B-GND-02: max(wish, 400) x 4 / 64 = 25 u/s per tick; capture: 25.0 and 23 ticks.
            S.verticalAxis = 1f;
            Tick();
            Assert.AreEqual(25f * U, Speed, 1e-4f);
            int ticks = 1;
            while (Speed < 0.95f * MinaWalk && ticks < 200) { Tick(); ticks++; }
            Assert.AreEqual(23, ticks);
            for (int i = 0; i < 30; i++) Tick();
            Assert.AreEqual(MinaWalk, Speed, 1e-4f);
        }

        [Test]
        public void ReleaseFromWalkSpeedStopsIn34Ticks() {
            // B-GND-01: capture 33/34/33 ticks to below 1 u/s, model 34 from 318.909 u/s.
            S.velocity = Vector3.forward * MinaWalk;
            int ticks = 0;
            while (Speed >= 1f * U && ticks < 200) { Tick(); ticks++; }
            Assert.AreEqual(34, ticks);
        }

        [Test]
        public void GroundJumpSetsVerticalSpeedAndKeepsHorizontal() {
            // B-JMP-01: capture vz 287.5 = 300 - 800/64 after the jump tick.
            S.velocity = Vector3.forward * 300f * U;
            S.wishJump = S.wishJumpDown = true;
            Tick();
            Assert.IsFalse(S.grounded);
            Assert.AreEqual(287.5f * U, S.velocity.y, 1e-4f);
            Assert.AreEqual(300f * U * (1f - 0.2f / 64f), Speed, 1e-4f, "air drag on the takeoff tick");
        }

        [Test]
        public void AirDragIsTheFrictionLawAtPointTwo() {
            // B-AIR-03: capture ratio 0.99688 per tick (0.2 drag).
            Airborne(Vector3.forward * 600f * U);
            Tick();
            Assert.AreEqual(600f * U * (1f - 0.2f / 64f), Speed, 1e-4f);
        }

        [TestCase(9, true)]
        [TestCase(10, false)]
        public void LateJumpWithinPointOneFiveSecondsIsAFreeGroundJump(int airborneTicks, bool groundJump) {
            // B-JMP-03: capture ground jump at <= 9 ticks after leaving, air jump at >= 10.
            Object.DestroyImmediate(_objects[0]);
            _objects.RemoveAt(0);
            Box("Ledge", new Vector3(0f, -0.5f, 0f), new Vector3(4f, 1f, 4f));
            S.origin = new Vector3(1.6f, 1f, 0f);
            S.velocity = Vector3.right * MinaWalk;
            S.viewAngles.y = 90f;
            S.verticalAxis = 1f;
            Physics.SyncTransforms();
            int guard = 0;
            while (S.grounded && guard++ < 60) Tick();
            Assert.IsFalse(S.grounded);
            // The loop's last tick was airborne tick 1; press on airborne tick airborneTicks.
            for (int i = 2; i < airborneTicks; i++) Tick();
            float stamina = S.stamina;
            S.wishJumpDown = true;
            Tick();
            if (groundJump) {
                Assert.AreEqual(stamina, S.stamina, 1e-5f, "a late jump is free");
                Assert.AreEqual(287.5f * U, S.velocity.y, 1e-4f);
            } else {
                Assert.AreEqual(stamina - 1f, S.stamina, 1e-5f, "past the window it is an air jump");
                Assert.AreEqual(512.5f * U, S.velocity.y, 1e-4f);
            }
        }

        [Test]
        public void AirJumpSetsVerticalSpeedAndAdds200AlongInput() {
            // B-JMP-02: capture vz 525 (512.5 after the tick) and +200 u/s along input.
            Airborne(new Vector3(0f, -3f, 0f));
            S.jumpCount = 1;
            S.jumpTimer = 1f;
            S.horizontalAxis = 1f;
            S.wishJumpDown = true;
            Tick();
            Assert.AreEqual(512.5f * U, S.velocity.y, 1e-4f);
            Assert.AreEqual(200f * U * (1f - 0.2f / 64f), Speed, 1e-4f);
            Assert.Greater(S.velocity.x, 0f, "right of a forward-facing view");
            Assert.AreEqual(2f, S.stamina, 1e-5f);
        }

        [Test]
        public void GroundDashDiscardsRunVelocityAndHoldsDashSpeed() {
            // B-DSH-01: capture velocity zeroed at cast, then 635 u/s (10 m / 0.62 s) along input.
            S.velocity = Vector3.forward * MinaWalk;
            S.horizontalAxis = 1f;
            S.wishDash = true;
            Tick();
            Assert.AreEqual(10f / 0.62f, Speed, 1e-3f);
            Assert.AreEqual(1f, S.velocity.normalized.x, 1e-4f, "along the input, not the run");
            for (int i = 0; i < 30; i++) {
                Tick();
                Assert.AreEqual(10f / 0.62f, Speed, 1e-3f, "no friction or input during the dash");
            }
            for (int i = 0; i < 20; i++) Tick();
            Assert.Less(Speed, 10f / 0.62f, "ground friction after the dash");
        }

        [Test]
        public void AirDashAcceleratesTowardAShrinkingTargetWithGravityOff() {
            // B-DSH-02: capture first-tick gain 68.66 u/s; cap 626 at tick 5 from a run; vz held near 0.
            Airborne(Vector3.forward * 297.7f * U);
            S.verticalAxis = 1f;
            S.wishDash = true;
            Tick();
            float airDashSpeed = 8f / 0.43f;
            Assert.AreEqual(297.7f * U + 10f * 0.6f * airDashSpeed * Dt, Speed, 1e-3f);
            Assert.AreEqual(100f * U * Dt, S.velocity.y, 1e-5f);
            for (int i = 0; i < 5; i++) Tick();
            Assert.AreEqual(airDashSpeed * Mathf.Lerp(1f, 0.2f, 5f / 64f / 0.43f), Speed, 2f * U);
            float y = S.origin.y;
            for (int i = 0; i < 10; i++) Tick();
            Assert.AreEqual(y, S.origin.y, 0.02f, "gravity is off during the air dash");
        }

        [TestCase(21, 650f)]
        [TestCase(16, 450f)]
        public void DashSlideTimerSelectsEntrySpeed(int crouchTick, float entrySpeed) {
            // B-SLD-08: capture <= 16 ticks -> 450, >= 21 ticks -> 650; 642.89 / 445.08 after the entry tick.
            S.verticalAxis = 1f;
            S.wishDash = true;
            Tick();
            for (int i = 1; i < crouchTick; i++) Tick();
            S.slideRequested = true;
            Tick();
            Assert.IsTrue(S.sliding);
            Assert.IsFalse(S.isDashing);
            Assert.AreEqual(entrySpeed * U * (1f - 0.175f * 4f / 64f), Speed, 1e-3f);
        }

        [Test]
        public void DashSlideFrictionRegimesAndSustainStop() {
            // B-SLD-03 / B-SLD-02: 0.175 for 1 s, then 0.8; the slide ends below 170 u/s with no input.
            S.wishDash = true;
            Tick();
            for (int i = 1; i < 25; i++) Tick();
            S.verticalAxis = 0f;
            S.slideRequested = true;
            Tick();
            float previous = Speed;
            for (int i = 1; i < 64; i++) {
                Tick();
                Assert.AreEqual(previous * (1f - 0.175f * 4f * Dt), Speed, 1e-3f, $"dash-slide friction, tick {i}");
                previous = Speed;
            }
            // The entry tick and 63 more used 0.175 (64 ticks, 1.0 s); the next uses 0.8.
            Tick();
            Assert.AreEqual(previous * (1f - 0.8f * 4f * Dt), Speed, 1e-3f, "then 0.8");
            int guard = 0;
            while (S.sliding && guard++ < 200) { previous = Speed; Tick(); }
            Assert.IsFalse(S.sliding);
            Assert.Less(previous, 170f * U, "the slide ended on the tick after speed fell below 170 u/s");
        }

        [Test]
        public void SlideWithForwardHeldSettlesAtCrouchSpeed() {
            // Capture: speed holds at 187.0 u/s (ECrouchSpeed 4.75 m/s) while crouch and forward are held.
            S.wishDash = true;
            S.verticalAxis = 1f;
            Tick();
            for (int i = 1; i < 25; i++) Tick();
            S.slideRequested = true;
            for (int i = 0; i < 200; i++) Tick();
            Assert.IsTrue(S.sliding);
            Assert.AreEqual(4.75f, Speed, 0.01f);
        }

        [Test]
        public void FlatSlideNeedsActivationSpeed() {
            // B-SLD-01: running at walk speed is below the 350 u/s flat activation speed.
            S.velocity = Vector3.forward * MinaWalk;
            S.verticalAxis = 1f;
            S.slideRequested = true;
            Tick();
            Assert.IsFalse(S.sliding);
            S.velocity = Vector3.forward * 360f * U;
            Tick();
            Assert.IsTrue(S.sliding);
        }
    }
}
