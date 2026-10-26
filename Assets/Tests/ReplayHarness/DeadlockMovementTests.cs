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

        // A wall 5 cm in front (+z) of an airborne motor; its face normal is -z.
        private void BesideWall(float fallSpeed) {
            Airborne(new Vector3(0f, -fallSpeed, 0f));
            Box("Wall", new Vector3(0f, 60f, 1.05f), new Vector3(10f, 10f, 1f));
            Physics.SyncTransforms();
            S.jumpCount = 1;
        }

        [Test]
        public void WallJumpWithNoInputPushes267OffAndUp525KeepingThirtyPercentOfTheFall() {
            // B-WAL-01: capture with no input: 267.5 out (266.6 after the tick), vz 0.3 x -175 + 525 = 472.5 (460.0 after).
            BesideWall(175f * U);
            S.wishJumpDown = true;
            Tick();
            Assert.AreEqual(-267.5f * U * (1f - 0.2f / 64f), S.velocity.z, 1e-3f);
            Assert.AreEqual(0f, S.velocity.x, 1e-4f);
            Assert.AreEqual(460f * U, S.velocity.y, 1e-3f);
            Assert.AreEqual(3f, S.stamina, 1e-5f, "the first wall jump is free");
            Assert.AreEqual(1, S.consecutiveWallJumps);
            Assert.AreEqual(1, S.jumpCount, "a wall jump leaves the air jump available");
        }

        [Test]
        public void WallJumpIntoTheWallGives225AndAirControlTakesItTo212() {
            // Capture: forward held into the wall: 225 out, 211.8-211.9 after the tick (air acceleration 12.5 u/s).
            BesideWall(0f);
            S.verticalAxis = 1f;
            S.wishJumpDown = true;
            Tick();
            Assert.AreEqual(-211.8f * U, S.velocity.z, 0.1f * U);
        }

        [Test]
        public void WallJumpAwayFromTheWallGives310() {
            BesideWall(0f);
            S.verticalAxis = -1f;
            S.wishJumpDown = true;
            Tick();
            Assert.AreEqual(-310f * U * (1f - 0.2f / 64f), S.velocity.z, 1e-3f);
        }

        [Test]
        public void WallJumpWithInputAlongTheWallAdds100Lateral() {
            // Capture: input along the wall gives 267.5 out and +100 along the wall (falloff 1 below 250 u/s).
            BesideWall(0f);
            S.horizontalAxis = 1f;
            S.wishJumpDown = true;
            Tick();
            Assert.AreEqual(-267.5f * U * (1f - 0.2f / 64f), S.velocity.z, 1e-3f);
            Assert.AreEqual(100f * U * (1f - 0.2f / 64f), S.velocity.x, 1e-3f);
        }

        [TestCase(64, 0.5676f)]
        [TestCase(51, 0.3797f)]
        [TestCase(36, 0.2414f)]
        [TestCase(83, 1f)]
        public void ChainedWallJumpPowerRecoversWithTimeAndCostsHalfAStamina(int ticksSinceLast, float power) {
            // B-WAL-01 fatigue: capture powers 0.5676 / 0.3797 / 0.2414 / 1 at 1.0 / 0.797 / 0.5625 / 1.297 s.
            BesideWall(175f * U);
            S.consecutiveWallJumps = 1;
            S.wallJumpFatigueTime = (ticksSinceLast - 1) / 64f;
            S.wishJumpDown = true;
            Tick();
            Assert.AreEqual((-52.5f + power * 525f - 12.5f) * U, S.velocity.y, 0.05f * U);
            Assert.AreEqual(2.5f, S.stamina, 1e-5f, "wall jumps after the first cost 0.5");
        }

        [Test]
        public void NoWallJumpWithoutStaminaAboveItsCost() {
            BesideWall(175f * U);
            S.consecutiveWallJumps = 1;
            S.stamina = 0.45f;
            S.wishJumpDown = true;
            Tick();
            Assert.AreEqual(1, S.consecutiveWallJumps);
            Assert.Less(S.velocity.y, 0f);
        }

        // A block whose face is 2 cm in front (+z) of the grounded motor.
        private void LedgeAhead(float height) {
            Box("Ledge", new Vector3(0f, height * 0.5f, 1.52f), new Vector3(4f, height, 2f));
            Physics.SyncTransforms();
            S.verticalAxis = 1f;
        }

        private void PushUntilMantle() {
            int guard = 0;
            while (!S.mantling && guard++ < 20) Tick();
            Assert.IsTrue(S.mantling, "pushing into the ledge starts a mantle");
        }

        [Test]
        public void MantleOnto64UnitLedgeRises19TicksThenCrosses13AndExitsAt191() {
            // B-MNT-01: capture type 1 (64 u): start tick still, 19 ticks up, 13 across at 177 u/s,
            // then ordinary ground movement: 177 -> 191.1 on the exit tick with forward held.
            float height = 66f * U;
            LedgeAhead(height);
            PushUntilMantle();
            Assert.AreEqual(64f * U, S.mantleHeight, 1e-5f);
            Assert.AreEqual(19f / 64f, S.mantleVerticalTime, 1e-6f);
            Assert.AreEqual(Vector3.zero, S.velocity, "the start tick holds still");
            Vector3 start = S.mantleStart;
            for (int i = 0; i < 19; i++) Tick();
            Assert.AreEqual(height, S.mantleHeight + S.mantleOffset, 0.005f, "ledge height from the box cast");
            Assert.AreEqual(S.mantleHeight + S.mantleOffset + _config.deadlock.mantleClearance, S.origin.y - start.y, 0.001f);
            Assert.AreEqual(start.z, S.origin.z, 0.002f, "no horizontal movement while rising");
            for (int i = 0; i < 13; i++) Tick();
            Assert.IsTrue(S.mantling);
            Assert.AreEqual(S.mantleDistance, S.origin.z - start.z, 0.002f);
            Tick();
            Assert.IsFalse(S.mantling);
            Assert.IsTrue(S.grounded);
            Assert.AreEqual(191.1f * U, Speed, 1f * U);
        }

        [Test]
        public void MantleWithNoForwardInputAtTheEndStops() {
            // Capture M04: type 0/1 mantles with the keys released exit at 0.
            LedgeAhead(66f * U);
            PushUntilMantle();
            for (int i = 0; i < 32; i++) Tick();
            S.verticalAxis = 0f;
            Tick();
            Assert.IsFalse(S.mantling);
            Assert.AreEqual(0f, Speed, 1e-4f);
        }

        [Test]
        public void MantleSlideAdds400AndSlidesFromTheNextTick() {
            // B-MNT-02: capture crouch + forward: 177 + 400 = 577 at the exit, ordinary ground friction on
            // the exit tick (541), then the slide with grace friction 3/64.
            LedgeAhead(66f * U);
            PushUntilMantle();
            S.slideRequested = true;
            for (int i = 0; i < 32; i++) Tick();
            float exit = S.mantleDistance / (13f / 64f);
            Tick();
            Assert.IsFalse(S.sliding, "the slide starts the tick after the exit");
            Assert.AreEqual((exit + 400f * U) * (1f - 4f / 64f), Speed, 1e-3f);
            float previous = Speed;
            Tick();
            Assert.IsTrue(S.sliding);
            Assert.AreEqual(previous * (1f - 0.05f * 4f / 64f), Speed, 1e-3f);
        }

        [Test]
        public void TallLedgeNeedsAJumpThenMantlesAsType128() {
            // A 156 u ledge is above the 134 u ground limit; one tick after a jump it mantles (capture M01/M04).
            LedgeAhead(156f * U);
            for (int i = 0; i < 10; i++) Tick();
            Assert.IsFalse(S.mantling);
            S.wishJump = S.wishJumpDown = true;
            Tick();
            S.wishJump = false;
            Tick();
            Assert.IsTrue(S.mantling);
            Assert.AreEqual(128f * U, S.mantleHeight, 1e-5f);
            Assert.AreEqual(32f / 64f, S.mantleVerticalTime, 1e-6f);
            for (int i = 0; i < 45; i++) Tick();
            Assert.IsTrue(S.mantling, "32 ticks up and 13 across after the start tick");
            Tick();
            Assert.IsFalse(S.mantling, "the exit is the 46th tick after the start");
        }

        [TestCase(5, true)]
        [TestCase(17, false)]
        public void JumpPressedNearTheMantleEndFiresOnTheNextTick(int ticksBeforeEnd, bool jumps) {
            // Capture M06: presses 3-8 ticks before the end jump on the first tick after; 17-22 are dropped.
            LedgeAhead(66f * U);
            PushUntilMantle();
            for (int i = 0; i < 32 - ticksBeforeEnd; i++) Tick();
            S.wishJumpDown = true;
            Tick();
            while (S.mantling) Tick();
            Tick();
            if (jumps) Assert.AreEqual(287.5f * U, S.velocity.y, 1e-3f);
            else Assert.IsTrue(S.grounded);
        }

        private void DashThenJumpAt(int ticks) {
            S.verticalAxis = 1f;
            S.wishDash = true;
            Tick();
            for (int i = 1; i < ticks; i++) Tick();
            S.wishJump = S.wishJumpDown = true;
            Tick();
            S.wishJump = false;
        }

        [Test]
        public void DashJumpInsideTheWindowSets704And425AndCostsOne() {
            // B-DSH-04: capture vz 425 and horizontal 704 along the dash at 0.31-0.33 s; 1 extra stamina.
            S.verticalAxis = 1f;
            S.wishDash = true;
            Tick();
            for (int i = 1; i < 20; i++) Tick();
            float stamina = S.stamina;
            S.wishJump = S.wishJumpDown = true;
            Tick();
            S.wishJump = false;
            Assert.IsFalse(S.grounded);
            Assert.AreEqual(412.5f * U, S.velocity.y, 1e-3f);
            Assert.AreEqual(704f * U * (1f - 0.2f / 64f), Speed, 1e-3f);
            Assert.AreEqual(stamina - 1f, S.stamina, 0.01f);
            Assert.AreEqual(0.6f - 20f / 64f, S.dashJumpAirControlTimer, 1e-4f);
        }

        [Test]
        public void DashJumpExtraAirControlIs30Percent() {
            // B-AIR-02 addendum: about 1.3x air acceleration until about 0.6 s after the dash.
            DashThenJumpAt(20);
            S.verticalAxis = 0f;
            S.horizontalAxis = 1f;
            float x = S.velocity.x;
            float carried = Speed * (1f - 0.2f / 64f);
            float cap = Mathf.Lerp(80f, 50f, Mathf.InverseLerp(600f, 800f, carried / U)) * U;
            Tick();
            Assert.AreEqual(x * (1f - 0.2f / 64f) + 1.3f * 10f * cap * Dt, S.velocity.x, 1e-3f);
        }

        [Test]
        public void JumpBeforeTheDashJumpWindowKeepsAtMost250() {
            // B-DSH-04 (decoded): a jump outside the window keeps at most m_flDashJumpMissMaxSpeed.
            DashThenJumpAt(10);
            Assert.AreEqual(287.5f * U, S.velocity.y, 1e-3f);
            Assert.AreEqual(250f * U * (1f - 0.2f / 64f), Speed, 1e-3f);
            Assert.IsFalse(S.canDashJump);
        }

        [Test]
        public void SlideTurnsTowardInputAt90DegreesPerSecond() {
            // B-SLD-05 (decoded): rotate by clamp(angle, -1, 1) x 90 deg/s x dt, then friction, then crouch acceleration.
            Vector3 start = Vector3.forward * 600f * U;
            S.velocity = start;
            S.sliding = S.wasSliding = S.slideRequested = true;
            S.slideGraceTimer = 1f;
            S.horizontalAxis = 1f;
            Tick();
            Vector3 expected = Quaternion.Euler(0f, 90f * Dt, 0f) * start * (1f - 0.05f * 4f * Dt) +
                               Vector3.right * 400f * U * 4f * Dt;
            Assert.AreEqual(expected.x, S.velocity.x, 1e-3f);
            Assert.AreEqual(expected.z, S.velocity.z, 1e-3f);
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
