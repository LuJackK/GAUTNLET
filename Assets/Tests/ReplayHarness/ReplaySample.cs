using System;
using Fragsurf.Movement;
using UnityEngine;

namespace Fragsurf.ReplayHarness {

    [Serializable]
    public struct ReplayStateSample {
        public int frame;
        public Vector3 position;
        public Vector3 velocity;
        public float yaw;
        public float pitch;
        public bool grounded;
        public bool underwater;
        public bool cameraUnderwater;
        public bool climbingLadder;
        public bool isDashing;
        public bool canAirDash;
        public bool sliding;
        public bool wasSliding;
        public bool crouching;
        public bool jumping;
        public bool uncrouchDown;
        public bool groundedTemp;
        public bool hasHitTarget;
        public bool meleeHitResolved;
        public int jumpCount;
        public int lastConsumedJumpPressFrame;
        public int lastConsumedDashPressFrame;
        public int meleeHitTargetObjectId;
        public int meleeHitResolveTick;
        public float jumpTimer;
        public float stamina;
        public float staminaRegenTimer;
        public float dashTimer;
        public float currentDashDuration;
        public float dashCooldownTimer;
        public MoveType moveType;
        public float forwardMove;
        public float sideMove;
        public float verticalAxis;
        public float horizontalAxis;
        public float surfaceFriction;
        public float gravityFactor;
        public float walkFactor;
        public float fallingVelocity;
        public MoveData.MeleeState meleeState;
        public float meleeTimer;
        public float meleeCooldownTimer;
        public float crouchLerp;
        public float renderCrouchLerp;
        public float slideSpeedCurrent;
        public float slideDelay;
        public Vector3 slideDirection;
        public int debugFingerprint;

        public static ReplayStateSample FromMoveData(MoveData state) {
            return new ReplayStateSample {
                frame = (state != null) ? state.frame : -1,
                position = (state != null) ? state.origin : Vector3.zero,
                velocity = (state != null) ? state.velocity : Vector3.zero,
                yaw = (state != null) ? state.viewAngles.y : 0f,
                pitch = (state != null) ? state.viewAngles.x : 0f,
                grounded = state != null && state.grounded,
                underwater = state != null && state.underwater,
                cameraUnderwater = state != null && state.cameraUnderwater,
                climbingLadder = state != null && state.climbingLadder,
                isDashing = state != null && state.isDashing,
                canAirDash = state == null || state.canAirDash,
                sliding = state != null && state.sliding,
                wasSliding = state != null && state.wasSliding,
                crouching = state != null && state.crouching,
                jumping = state != null && state.jumping,
                uncrouchDown = state != null && state.uncrouchDown,
                groundedTemp = state != null && state.groundedTemp,
                hasHitTarget = state != null && state.hasHitTarget,
                meleeHitResolved = state != null && state.meleeHitResolved,
                jumpCount = (state != null) ? state.jumpCount : 0,
                lastConsumedJumpPressFrame = (state != null) ? state.lastConsumedJumpPressFrame : -1,
                lastConsumedDashPressFrame = (state != null) ? state.lastConsumedDashPressFrame : -1,
                meleeHitTargetObjectId = (state != null) ? state.meleeHitTargetObjectId : 0,
                meleeHitResolveTick = (state != null) ? state.meleeHitResolveTick : -1,
                jumpTimer = (state != null) ? state.jumpTimer : 0f,
                stamina = (state != null) ? state.stamina : 0f,
                staminaRegenTimer = (state != null) ? state.staminaRegenTimer : 0f,
                dashTimer = (state != null) ? state.dashTimer : 0f,
                currentDashDuration = (state != null) ? state.currentDashDuration : 0f,
                dashCooldownTimer = (state != null) ? state.dashCooldownTimer : 0f,
                moveType = (state != null) ? state.moveType : MoveType.None,
                forwardMove = (state != null) ? state.forwardMove : 0f,
                sideMove = (state != null) ? state.sideMove : 0f,
                verticalAxis = (state != null) ? state.verticalAxis : 0f,
                horizontalAxis = (state != null) ? state.horizontalAxis : 0f,
                surfaceFriction = (state != null) ? state.surfaceFriction : 0f,
                gravityFactor = (state != null) ? state.gravityFactor : 0f,
                walkFactor = (state != null) ? state.walkFactor : 0f,
                fallingVelocity = (state != null) ? state.fallingVelocity : 0f,
                meleeState = (state != null) ? state.meleeState : MoveData.MeleeState.None,
                meleeTimer = (state != null) ? state.meleeTimer : 0f,
                meleeCooldownTimer = (state != null) ? state.meleeCooldownTimer : 0f,
                crouchLerp = (state != null) ? state.crouchLerp : 0f,
                renderCrouchLerp = (state != null) ? state.renderCrouchLerp : 0f,
                slideSpeedCurrent = (state != null) ? state.slideSpeedCurrent : 0f,
                slideDelay = (state != null) ? state.slideDelay : 0f,
                slideDirection = (state != null) ? state.slideDirection : Vector3.zero,
                debugFingerprint = ComputeDebugFingerprint(state)
            };
        }

        private static int ComputeDebugFingerprint(MoveData state) {
            if (state == null)
                return 0;

            unchecked {
                int hash = 17;
                HashInt(ref hash, state.frame);
                HashInt(ref hash, Quantize(state.origin.x));
                HashInt(ref hash, Quantize(state.origin.y));
                HashInt(ref hash, Quantize(state.origin.z));
                HashInt(ref hash, Quantize(state.velocity.x));
                HashInt(ref hash, Quantize(state.velocity.y));
                HashInt(ref hash, Quantize(state.velocity.z));
                HashInt(ref hash, Quantize(state.viewAngles.x));
                HashInt(ref hash, Quantize(state.viewAngles.y));
                HashInt(ref hash, (int)state.moveType);
                HashInt(ref hash, (int)state.meleeState);
                HashInt(ref hash, BoolToInt(state.grounded));
                HashInt(ref hash, BoolToInt(state.groundedTemp));
                HashInt(ref hash, BoolToInt(state.isDashing));
                HashInt(ref hash, BoolToInt(state.canAirDash));
                HashInt(ref hash, BoolToInt(state.sliding));
                HashInt(ref hash, BoolToInt(state.wasSliding));
                HashInt(ref hash, BoolToInt(state.crouching));
                HashInt(ref hash, BoolToInt(state.jumping));
                HashInt(ref hash, BoolToInt(state.uncrouchDown));
                HashInt(ref hash, BoolToInt(state.hasHitTarget));
                HashInt(ref hash, BoolToInt(state.meleeHitResolved));
                HashInt(ref hash, state.jumpCount);
                HashInt(ref hash, state.lastConsumedJumpPressFrame);
                HashInt(ref hash, state.lastConsumedDashPressFrame);
                HashInt(ref hash, state.meleeHitTargetObjectId);
                HashInt(ref hash, state.meleeHitResolveTick);
                HashInt(ref hash, Quantize(state.jumpTimer));
                HashInt(ref hash, Quantize(state.stamina));
                HashInt(ref hash, Quantize(state.staminaRegenTimer));
                HashInt(ref hash, Quantize(state.dashTimer));
                HashInt(ref hash, Quantize(state.currentDashDuration));
                HashInt(ref hash, Quantize(state.dashCooldownTimer));
                HashInt(ref hash, Quantize(state.forwardMove));
                HashInt(ref hash, Quantize(state.sideMove));
                HashInt(ref hash, Quantize(state.verticalAxis));
                HashInt(ref hash, Quantize(state.horizontalAxis));
                HashInt(ref hash, Quantize(state.surfaceFriction));
                HashInt(ref hash, Quantize(state.gravityFactor));
                HashInt(ref hash, Quantize(state.walkFactor));
                HashInt(ref hash, Quantize(state.fallingVelocity));
                HashInt(ref hash, Quantize(state.meleeTimer));
                HashInt(ref hash, Quantize(state.meleeCooldownTimer));
                HashInt(ref hash, Quantize(state.crouchLerp));
                HashInt(ref hash, Quantize(state.renderCrouchLerp));
                HashInt(ref hash, Quantize(state.slideSpeedCurrent));
                HashInt(ref hash, Quantize(state.slideDelay));
                HashInt(ref hash, Quantize(state.slideDirection.x));
                HashInt(ref hash, Quantize(state.slideDirection.y));
                HashInt(ref hash, Quantize(state.slideDirection.z));
                return hash;
            }
        }

        private static int Quantize(float value) {
            return Mathf.RoundToInt(value * 1000f);
        }

        private static void HashInt(ref int hash, int value) {
            unchecked {
                hash = (hash * 31) + value;
            }
        }

        private static int BoolToInt(bool value) {
            return value ? 1 : 0;
        }
    }

    [Serializable]
    public sealed class ReplayTickRecord {
        public InputFrame input;
        public ReplayStateSample sample;
        public MoveData state;
        public string querySnapshot;
    }
}
