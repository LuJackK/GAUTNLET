using System.Collections;
using System.Reflection;
using Fragsurf.Movement;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Fragsurf.ReplayHarness {

    public class AuthoritativeStateRegressionTests {
        private const BindingFlags NonPublicInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags PublicInstance = BindingFlags.Instance | BindingFlags.Public;

        [Test]
        public void BuildAndApplyReconcile_PreservesReplayCriticalMoveState() {
            GameObject root = new GameObject("AuthoritativeStateRoundTrip");
            try {
                GameObject body = new GameObject("Body");
                body.transform.SetParent(root.transform, worldPositionStays: false);
                body.transform.localPosition = Vector3.zero;

                GameObject view = new GameObject("View");
                view.transform.SetParent(body.transform, worldPositionStays: false);
                view.transform.localPosition = new Vector3(0f, 0.9f, 0f);

                PlayerAiming aiming = view.AddComponent<PlayerAiming>();
                aiming.bodyTransform = body.transform;
                aiming.enabled = false;

                SurfCharacter character = root.AddComponent<SurfCharacter>();
                character.viewTransform = view.transform;
                character.playerRotationTransform = body.transform;
                RollbackManager rollback = root.AddComponent<RollbackManager>();
                NetworkedCharacter networkedCharacter = root.AddComponent<NetworkedCharacter>();

                rollback.Initialize(character);

                MoveData source = new MoveData {
                    frame = 41,
                    origin = new Vector3(3.25f, 1.5f, -2.75f),
                    velocity = new Vector3(6.5f, -1.25f, 9.75f),
                    viewAngles = new Vector3(-12.5f, 123.25f, 0f),
                    stamina = 1.75f,
                    staminaRegenTimer = 0.3f,
                    dashTimer = 0.2f,
                    currentDashDuration = 0.25f,
                    dashCooldownTimer = 0.9f,
                    isDashing = true,
                    canAirDash = false,
                    jumpCount = 2,
                    jumpTimer = 0.4f,
                    grounded = true,
                    groundedTemp = true,
                    jumping = true,
                    moveType = MoveType.HeavyMelee,
                    forwardMove = 2.5f,
                    sideMove = -1.75f,
                    verticalAxis = 0.8f,
                    horizontalAxis = -0.35f,
                    surfaceFriction = 0.65f,
                    gravityFactor = 1.1f,
                    walkFactor = 0.9f,
                    fallingVelocity = 3.4f,
                    crouching = true,
                    crouchLerp = 0.85f,
                    renderCrouchLerp = 0.8f,
                    uncrouchDown = true,
                    sliding = true,
                    wasSliding = true,
                    slideSpeedCurrent = 11.5f,
                    slideDirection = new Vector3(0.6f, 0f, 0.8f),
                    slideDelay = 0.45f,
                    meleeState = MoveData.MeleeState.Lunging,
                    meleeTimer = 0.15f,
                    meleeCooldownTimer = 0.8f,
                    hasHitTarget = true,
                    meleeHitResolved = true,
                    meleeHitTargetObjectId = 1337,
                    meleeHitResolveTick = 40,
                    lastConsumedJumpPressFrame = 37,
                    lastConsumedDashPressFrame = 39
                };

                character.moveData = source.Clone();

                object reconcileService = GetPredictionReconcileService(networkedCharacter);
                Assert.NotNull(reconcileService, "Expected prediction reconcile service to be created by NetworkedCharacter.");

                object reconcileData = InvokeInstanceMethod(reconcileService, "BuildReconcileData", source.frame);
                Assert.NotNull(reconcileData, "Expected BuildReconcileData to return data.");

                character.moveData = new MoveData {
                    frame = 5,
                    origin = Vector3.zero,
                    velocity = Vector3.zero
                };

                InvokeInstanceMethod(reconcileService, "ApplyReconcile", reconcileData, 1f / 60f);

                MoveData restored = character.moveData;
                Assert.NotNull(restored);
                AssertReplayCriticalStateEqual(source, restored);
                Assert.That(Mathf.Abs(Mathf.DeltaAngle(source.viewAngles.y, body.transform.eulerAngles.y)), Is.LessThan(0.0001f));
            } finally {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ApplySpectatorPresentationYawFromCurrentState_RotatesBodyWithoutDrivingPitch() {
            GameObject root = new GameObject("SpectatorYawPresentation");
            try {
                GameObject body = new GameObject("Body");
                body.transform.SetParent(root.transform, worldPositionStays: false);
                body.transform.localPosition = Vector3.zero;

                GameObject view = new GameObject("View");
                view.transform.SetParent(body.transform, worldPositionStays: false);
                view.transform.localPosition = new Vector3(0f, 0.9f, 0f);
                view.transform.localRotation = Quaternion.Euler(18f, 0f, 0f);

                PlayerAiming aiming = view.AddComponent<PlayerAiming>();
                aiming.bodyTransform = body.transform;
                aiming.enabled = false;

                SurfCharacter character = root.AddComponent<SurfCharacter>();
                character.viewTransform = view.transform;
                character.playerRotationTransform = body.transform;
                root.AddComponent<RollbackManager>();
                NetworkedCharacter networkedCharacter = root.AddComponent<NetworkedCharacter>();

                character.moveData = new MoveData {
                    frame = 9,
                    origin = Vector3.zero,
                    velocity = Vector3.zero,
                    viewAngles = new Vector3(-35f, 127.5f, 0f)
                };

                InvokeInstanceMethod(networkedCharacter, "ApplySpectatorPresentationYawFromCurrentState");

                Assert.That(Mathf.Abs(Mathf.DeltaAngle(127.5f, body.transform.eulerAngles.y)), Is.LessThan(0.0001f));
                Assert.That(Mathf.Abs(Mathf.DeltaAngle(18f, view.transform.localEulerAngles.x)), Is.LessThan(0.0001f));
            } finally {
                Object.DestroyImmediate(root);
            }
        }

        [UnityTest]
        public IEnumerator LoadState_ReappliesCrouchColliderShape() {
            ReplayHarnessEnvironment environment = null;
            try {
                environment = ReplayHarnessEnvironment.Create(new ReplayTraceDefinition {
                    traceId = "load-state-crouch-shape",
                    excludeCrouch = false
                }, Vector3.zero);

                yield return WaitUntilReady(environment);
                environment.PrepareForRun();

                SurfCharacter character = environment.character;
                BoxCollider boxCollider = character.collider as BoxCollider;
                Assert.NotNull(boxCollider, "Replay harness character should use a box collider.");

                float defaultHeight = character.moveData.defaultHeight;
                float crouchedHeight = defaultHeight * character.moveData.crouchingHeight;

                MoveData crouched = character.moveData.Clone();
                crouched.crouching = true;
                crouched.crouchLerp = 1f;
                crouched.renderCrouchLerp = 1f;
                character.LoadState(crouched);

                Assert.That(boxCollider.size.y, Is.EqualTo(crouchedHeight).Within(0.0001f));

                MoveData standing = crouched.Clone();
                standing.crouching = false;
                standing.crouchLerp = 0f;
                standing.renderCrouchLerp = 0f;
                character.LoadState(standing);

                Assert.That(boxCollider.size.y, Is.EqualTo(defaultHeight).Within(0.0001f));
            } finally {
                environment?.Dispose();
            }

            yield return null;
        }

        private static object GetPredictionReconcileService(NetworkedCharacter networkedCharacter) {
            FieldInfo field = typeof(NetworkedCharacter).GetField("_predictionReconcileService", NonPublicInstance);
            Assert.NotNull(field, "Expected NetworkedCharacter to keep a private prediction reconcile service field.");
            return field.GetValue(networkedCharacter);
        }

        private static object InvokeInstanceMethod(object target, string methodName, params object[] args) {
            Assert.NotNull(target);
            MethodInfo method = target.GetType().GetMethod(methodName, PublicInstance | NonPublicInstance);
            Assert.NotNull(method, $"Expected method '{methodName}' on {target.GetType().Name}.");
            return method.Invoke(target, args);
        }

        private static IEnumerator WaitUntilReady(ReplayHarnessEnvironment environment) {
            int safetyFrames = 10;
            while (environment != null && !environment.IsReady && safetyFrames-- > 0)
                yield return null;

            Assert.IsNotNull(environment);
            Assert.IsTrue(environment.IsReady, "Replay harness environment did not finish initializing.");
        }

        private static void AssertReplayCriticalStateEqual(MoveData expected, MoveData actual) {
            Assert.AreEqual(expected.frame, actual.frame);
            AssertVector3Equal(expected.origin, actual.origin);
            AssertVector3Equal(expected.velocity, actual.velocity);
            AssertVector3Equal(expected.viewAngles, actual.viewAngles);
            Assert.AreEqual(expected.stamina, actual.stamina);
            Assert.AreEqual(expected.staminaRegenTimer, actual.staminaRegenTimer);
            Assert.AreEqual(expected.dashTimer, actual.dashTimer);
            Assert.AreEqual(expected.currentDashDuration, actual.currentDashDuration);
            Assert.AreEqual(expected.dashCooldownTimer, actual.dashCooldownTimer);
            Assert.AreEqual(expected.isDashing, actual.isDashing);
            Assert.AreEqual(expected.canAirDash, actual.canAirDash);
            Assert.AreEqual(expected.jumpCount, actual.jumpCount);
            Assert.AreEqual(expected.jumpTimer, actual.jumpTimer);
            Assert.AreEqual(expected.grounded, actual.grounded);
            Assert.AreEqual(expected.groundedTemp, actual.groundedTemp);
            Assert.AreEqual(expected.jumping, actual.jumping);
            Assert.AreEqual(expected.moveType, actual.moveType);
            Assert.AreEqual(expected.forwardMove, actual.forwardMove);
            Assert.AreEqual(expected.sideMove, actual.sideMove);
            Assert.AreEqual(expected.verticalAxis, actual.verticalAxis);
            Assert.AreEqual(expected.horizontalAxis, actual.horizontalAxis);
            Assert.AreEqual(expected.surfaceFriction, actual.surfaceFriction);
            Assert.AreEqual(expected.gravityFactor, actual.gravityFactor);
            Assert.AreEqual(expected.walkFactor, actual.walkFactor);
            Assert.AreEqual(expected.fallingVelocity, actual.fallingVelocity);
            Assert.AreEqual(expected.crouching, actual.crouching);
            Assert.AreEqual(expected.crouchLerp, actual.crouchLerp);
            Assert.AreEqual(expected.renderCrouchLerp, actual.renderCrouchLerp);
            Assert.AreEqual(expected.uncrouchDown, actual.uncrouchDown);
            Assert.AreEqual(expected.sliding, actual.sliding);
            Assert.AreEqual(expected.wasSliding, actual.wasSliding);
            Assert.AreEqual(expected.slideSpeedCurrent, actual.slideSpeedCurrent);
            AssertVector3Equal(expected.slideDirection, actual.slideDirection);
            Assert.AreEqual(expected.slideDelay, actual.slideDelay);
            Assert.AreEqual(expected.meleeState, actual.meleeState);
            Assert.AreEqual(expected.meleeTimer, actual.meleeTimer);
            Assert.AreEqual(expected.meleeCooldownTimer, actual.meleeCooldownTimer);
            Assert.AreEqual(expected.hasHitTarget, actual.hasHitTarget);
            Assert.AreEqual(expected.meleeHitResolved, actual.meleeHitResolved);
            Assert.AreEqual(expected.meleeHitTargetObjectId, actual.meleeHitTargetObjectId);
            Assert.AreEqual(expected.meleeHitResolveTick, actual.meleeHitResolveTick);
            Assert.AreEqual(expected.lastConsumedJumpPressFrame, actual.lastConsumedJumpPressFrame);
            Assert.AreEqual(expected.lastConsumedDashPressFrame, actual.lastConsumedDashPressFrame);
        }

        private static void AssertVector3Equal(Vector3 expected, Vector3 actual) {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.0001f));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.0001f));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(0.0001f));
        }
    }
}
