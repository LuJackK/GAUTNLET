using System.Reflection;
using Fragsurf.Movement;
using NUnit.Framework;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Animations;
#endif

namespace Fragsurf.ReplayHarness {
    public class MovementPresentationContractTests {
#if UNITY_EDITOR
        private const string PlayerPath = "Assets/Prefabs/Player 1.prefab";

        [Test]
        public void PlayerPrefab_AllMovementAnimationBindingsExistWithCorrectTypes() {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
            Assert.NotNull(prefab);
            CharacterRenderer renderer = prefab.GetComponentInChildren<CharacterRenderer>(true);
            Assert.NotNull(renderer);
            SerializedObject bindings = new SerializedObject(renderer);
            Animator animator = (Animator)bindings.FindProperty("animator").objectReferenceValue;
            Assert.NotNull(animator);
            AnimatorController controller = animator.runtimeAnimatorController as AnimatorController;
            Assert.NotNull(controller);
            string[] boolFields = {
                "grounded", "jumping", "crouching", "sliding", "punching",
                "meleeCharging", "meleeLunging", "parrying", "dashing"
            };
            string[] triggerFields = {
                "meleeCharge", "meleeLunge", "parry", "parrySuccess", "dash", "dashDone", "doubleJump"
            };
            void Check(string field, AnimatorControllerParameterType type) {
                string name = bindings.FindProperty(field + "ParameterName").stringValue;
                Assert.IsTrue(System.Array.Exists(controller.parameters, p => p.name == name && p.type == type),
                    $"Missing or incorrectly typed Animator binding: {field} -> {name} ({type})");
            }
            Check("speed", AnimatorControllerParameterType.Float);
            foreach (string field in boolFields) Check(field, AnimatorControllerParameterType.Bool);
            foreach (string field in triggerFields) Check(field, AnimatorControllerParameterType.Trigger);
            Assert.NotNull(bindings.FindProperty("vfxController").objectReferenceValue);
            // CharacterRenderer.Awake supports an unassigned field via same-object lookup.
            Assert.NotNull(bindings.FindProperty("soundController").objectReferenceValue as PlayerSoundController
                ?? renderer.GetComponent<PlayerSoundController>());
        }

        [Test]
        public void Renderer_StillDrivesMeleeSlideAndDashStateWithoutChangingSimulation() {
            GameObject go = new GameObject("MovementPresentationContract");
            try {
                Animator animator = go.AddComponent<Animator>();
                animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/PlayerAnimator.controller");
                animator.Rebind();
                CharacterRenderer renderer = go.AddComponent<CharacterRenderer>();
                typeof(CharacterRenderer).GetField("animator", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(renderer, animator);
                MoveData previous = new MoveData();
                MoveData state = new MoveData {
                    origin = Vector3.up * 10f, velocity = Vector3.forward * 15f,
                    moveType = MoveType.HeavyMelee, meleeState = MoveData.MeleeState.Charging
                };
                renderer.ApplyState(state, previous);
                Assert.IsTrue(animator.GetBool("MeleeCharging"));
                Assert.IsTrue(animator.GetBool("Punching"));
                Assert.IsFalse(animator.GetBool("Jumping"));
                Assert.AreEqual(15f, animator.GetFloat("Speed"));
                Assert.AreEqual(Vector3.up * 10f, state.origin);
                Assert.AreEqual(Vector3.forward * 15f, state.velocity);
                previous = state.Clone();
                state.meleeState = MoveData.MeleeState.Lunging;
                renderer.ApplyState(state, previous);
                Assert.IsFalse(animator.GetBool("MeleeCharging"));
                Assert.IsTrue(animator.GetBool("MeleeLunging"));
                previous = state.Clone();
                state.moveType = MoveType.Walk;
                state.meleeState = MoveData.MeleeState.None;
                state.sliding = state.crouching = state.grounded = true;
                renderer.ApplyState(state, previous);
                Assert.IsFalse(animator.GetBool("Punching"));
                Assert.IsTrue(animator.GetBool("Sliding"));
                Assert.IsTrue(animator.GetBool("Crouching"));
                previous = state.Clone();
                state.sliding = state.grounded = false;
                state.dashStartedThisFrame = true;
                state.isDashing = false; // dash-jump begins and ends a dash in one tick
                state.velocity.y = 6f;
                renderer.ApplyState(state, previous);
                Assert.IsFalse(animator.GetBool("Sliding"));
                Assert.IsFalse(animator.GetBool("Dashing"));
                Assert.IsTrue(animator.GetBool("Jumping"));
            } finally {
                Object.DestroyImmediate(go);
            }
        }
#endif
    }
}
