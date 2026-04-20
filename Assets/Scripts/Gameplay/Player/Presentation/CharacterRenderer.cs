using UnityEngine;

namespace Fragsurf.Movement {

    public class CharacterRenderer : MonoBehaviour {
        
        [Header("Animation")]
        [SerializeField] private Animator animator;
        [SerializeField] private string speedParameterName = "Speed";
        [SerializeField] private string groundedParameterName = "Grounded";
        [SerializeField] private string jumpingParameterName = "Jumping";
        [SerializeField] private string crouchingParameterName = "Crouching";
        [SerializeField] private string slidingParameterName = "Sliding";
        [SerializeField] private string punchingParameterName = "Punching";
        [SerializeField] private string dashParameterName = "Dash";
        [SerializeField] private string dashDoneParameterName = "DashDone";
        [SerializeField] private string doubleJumpParameterName = "DoubleJump";

        [Header("VFX")]
        [SerializeField] private PlayerVFXController vfxController;

        // Called from SurfCharacter.Update() — reads state, never writes it
        public void ApplyState(MoveData state, MoveData prevState, bool isNewTick = true) {
            
            if (animator != null) {
                float hSpeed = new Vector3(state.velocity.x, 0, state.velocity.z).magnitude;
                animator.SetFloat(speedParameterName, hSpeed);
                animator.SetBool(groundedParameterName, state.grounded);
                animator.SetBool(jumpingParameterName, state.velocity.y > 0.1f && !state.grounded); // Using state velocity y instead of old jumping flag
                animator.SetBool(crouchingParameterName, state.crouching);
                animator.SetBool(slidingParameterName, state.sliding);
                
                bool isPunchingAnim = state.moveType == MoveType.HeavyMelee && state.meleeState != MoveData.MeleeState.Recovery;
                animator.SetBool(punchingParameterName, isPunchingAnim);
                
                if (isNewTick) {
                    animator.SetBool(dashParameterName, state.dashStartedThisFrame);
                    animator.SetBool(dashDoneParameterName, prevState.isDashing && !state.isDashing);
                    animator.SetBool(doubleJumpParameterName, state.doubleJumpedThisFrame);
                }
            } else {
                Debug.LogWarning("[CharacterRenderer] Animator is NULL! Please assign the Animator in the inspector.");
            }

            if (vfxController != null) {
                vfxController.ApplyState(state, prevState, isNewTick);

                if (isNewTick) {
                    if (state.dashStartedThisFrame) {
                        Vector3 dashDir = new Vector3(state.velocity.x, 0, state.velocity.z).normalized;
                        if (dashDir.sqrMagnitude < 0.001f) dashDir = transform.forward;
                        vfxController.OnDash(dashDir);
                    }
                    
                    if (state.doubleJumpedThisFrame) {
                        vfxController.OnDoubleJump();
                    }

                    if (state.moveType == MoveType.HeavyMelee && prevState.moveType != MoveType.HeavyMelee) {
                        vfxController.OnMeleeStart();
                    } else if (state.moveType != MoveType.HeavyMelee && prevState.moveType == MoveType.HeavyMelee) {
                        vfxController.OnMeleeEnd();
                    }
                }
            }
        }
    }
}
