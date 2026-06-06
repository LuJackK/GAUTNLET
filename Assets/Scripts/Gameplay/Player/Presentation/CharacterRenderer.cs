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
        [SerializeField] private string meleeChargingParameterName = "MeleeCharging";
        [SerializeField] private string meleeLungingParameterName = "MeleeLunging";
        [SerializeField] private string parryingParameterName = "Parrying";
        [SerializeField] private string dashingParameterName = "Dashing";
        [SerializeField] private string meleeChargeParameterName = "MeleeCharge";
        [SerializeField] private string meleeLungeParameterName = "MeleeLunge";
        [SerializeField] private string parryParameterName = "Parry";
        [SerializeField] private string parrySuccessParameterName = "ParrySuccess";
        [SerializeField] private string dashParameterName = "Dash";
        [SerializeField] private string dashDoneParameterName = "DashDone";
        [SerializeField] private string doubleJumpParameterName = "DoubleJump";

        [Header("VFX")]
        [SerializeField] private PlayerVFXController vfxController;

        [Header("Audio")]
        [SerializeField] private PlayerSoundController soundController;

        private void Awake() {
            if (soundController == null)
                soundController = GetComponent<PlayerSoundController>();
        }

        // Called from SurfCharacter.Update(); reads state, never writes it.
        public void ApplyState(MoveData state, MoveData prevState, bool isNewTick = true) {
            
            if (animator != null) {
                float hSpeed = new Vector3(state.velocity.x, 0, state.velocity.z).magnitude;
                animator.SetFloat(speedParameterName, hSpeed);
                animator.SetBool(groundedParameterName, state.grounded);
                animator.SetBool(jumpingParameterName, state.velocity.y > 0.1f && !state.grounded); // Using state velocity y instead of old jumping flag
                animator.SetBool(crouchingParameterName, state.crouching);
                animator.SetBool(slidingParameterName, state.sliding);
                
                bool isPunchingAnim = state.moveType == MoveType.HeavyMelee && state.meleeState != MoveData.MeleeState.Recovery;
                bool isMeleeCharging = state.moveType == MoveType.HeavyMelee && state.meleeState == MoveData.MeleeState.Charging;
                bool isMeleeLunging = state.moveType == MoveType.HeavyMelee && state.meleeState == MoveData.MeleeState.Lunging;
                animator.SetBool(punchingParameterName, isPunchingAnim);
                animator.SetBool(meleeChargingParameterName, isMeleeCharging);
                animator.SetBool(meleeLungingParameterName, isMeleeLunging);
                animator.SetBool(parryingParameterName, state.isParrying);
                animator.SetBool(dashingParameterName, state.isDashing);
                
                if (isNewTick) {
                    if (isMeleeCharging && (prevState.moveType != MoveType.HeavyMelee || prevState.meleeState != MoveData.MeleeState.Charging))
                        animator.SetTrigger(meleeChargeParameterName);
                    if (isMeleeLunging && (prevState.moveType != MoveType.HeavyMelee || prevState.meleeState != MoveData.MeleeState.Lunging))
                        animator.SetTrigger(meleeLungeParameterName);
                    if (state.dashStartedThisFrame)
                        animator.SetTrigger(dashParameterName);
                    if (prevState.isDashing && !state.isDashing)
                        animator.SetTrigger(dashDoneParameterName);
                    if (state.doubleJumpedThisFrame)
                        animator.SetTrigger(doubleJumpParameterName);
                    if (state.parryStartedThisFrame)
                        animator.SetTrigger(parryParameterName);
                    if (state.parrySuccessThisFrame)
                        animator.SetTrigger(parrySuccessParameterName);
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

                    if (state.parryStartedThisFrame) {
                        Debug.Log($"[CharacterRenderer] Parry VFX requested. frame={state.frame}, parryTimer={state.parryTimer:0.000}, isParrying={state.isParrying}, isNewTick={isNewTick}", this);
                        vfxController.OnParry(state.parryTimer);
                    }

                    if (state.moveType == MoveType.HeavyMelee && prevState.moveType != MoveType.HeavyMelee) {
                        vfxController.OnMeleeStart();
                    } else if (state.moveType != MoveType.HeavyMelee && prevState.moveType == MoveType.HeavyMelee) {
                        vfxController.OnMeleeEnd();
                    }
                }
            }

            if (soundController != null)
                soundController.ApplyState(state, prevState, isNewTick);
        }
    }
}
