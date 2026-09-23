using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Fragsurf.Movement {
    // Schema 1: explicit value-only copy in WorldMovementSerializer field order.
    // Float members contain IEEE-754 bits, not rounded JSON floats.
    [Serializable] public struct TraceVector {
        public int x, y, z;
        public static TraceVector Copy(Vector3 v) => new TraceVector { x = TraceMovementState.Bits(v.x), y = TraceMovementState.Bits(v.y), z = TraceMovementState.Bits(v.z) };
    }
    [Serializable] public sealed class TraceMovementState {
        [StructLayout(LayoutKind.Explicit)] private struct FloatBits {
            [FieldOffset(0)] public float value;
            [FieldOffset(0)] public int bits;
        }
        public static int Bits(float value) => new FloatBits { value = value }.bits;
        public int frame;
        public InputFrame lastInput;
        public int moveType;
        public TraceVector viewTransformDefaultLocalPos;
        public TraceVector origin;
        public TraceVector viewAngles;
        public TraceVector velocity;
        public int forwardMove;
        public int sideMove;
        public int upMove;
        public int surfaceFriction;
        public int gravityFactor;
        public int walkFactor;
        public int verticalAxis;
        public int horizontalAxis;
        public bool wishJump;
        public bool wishJumpDown;
        public int lastConsumedJumpPressFrame;
        public bool crouching;
        public bool jumping;
        public int crouchLerp;
        public int renderCrouchLerp;
        public bool uncrouchDown;
        public int maxStaminaBonus;
        public int staminaRegenBonus;
        public int stamina;
        public int staminaRegenTimer;
        public int dashTimer;
        public int currentDashDuration;
        public int dashCooldownTimer;
        public bool isDashing;
        public bool canAirDash;
        public bool wishDash;
        public int lastConsumedDashPressFrame;
        public int jumpCount;
        public int jumpTimer;
        public int coyoteTimer;
        public bool slideRequested;
        public bool sliding;
        public bool wasSliding;
        public int slideSpeedCurrent;
        public TraceVector slideDirection;
        public int slideDelay;
        public int slopeLimit;
        public int rigidbodyPushForce;
        public int defaultHeight;
        public int crouchingHeight;
        public int crouchingSpeed;
        public bool toggleCrouch;
        public bool slidingEnabled;
        public bool laddersEnabled;
        public bool angledLaddersEnabled;
        public bool climbingLadder;
        public TraceVector ladderNormal;
        public TraceVector ladderDirection;
        public TraceVector ladderClimbDir;
        public TraceVector ladderVelocity;
        public bool underwater;
        public bool cameraUnderwater;
        public bool grounded;
        public bool groundedTemp;
        public int fallingVelocity;
        public bool useStepOffset;
        public int stepOffset;
        public int meleeState;
        public int meleeTimer;
        public int meleeCooldownTimer;
        public bool wishMelee;
        public int meleeHoldTimer;
        public bool hasHitTarget;
        public bool meleeHitResolved;
        public int meleeHitTargetObjectId;
        public int meleeHitResolveTick;
        public bool wishParry;
        public bool isParrying;
        public int parryTimer;
        public int parryCooldownTimer;
        public int lastConsumedParryPressFrame;
        public bool dashStartedThisFrame;
        public bool doubleJumpedThisFrame;
        public bool meleeHitThisFrame;
        public bool meleeMinChargeReachedThisFrame;
        public bool meleeCooldownReadyThisFrame;
        public bool parryStartedThisFrame;
        public bool parrySuccessThisFrame;
        public bool gotParriedThisFrame;
        public bool parryCooldownReadyThisFrame;
        public static TraceMovementState Copy(MoveData s) => new TraceMovementState {
            frame = s.frame,
            lastInput = s.lastInput,
            moveType = (int)s.moveType,
            viewTransformDefaultLocalPos = TraceVector.Copy(s.viewTransformDefaultLocalPos),
            origin = TraceVector.Copy(s.origin),
            viewAngles = TraceVector.Copy(s.viewAngles),
            velocity = TraceVector.Copy(s.velocity),
            forwardMove = Bits(s.forwardMove),
            sideMove = Bits(s.sideMove),
            upMove = Bits(s.upMove),
            surfaceFriction = Bits(s.surfaceFriction),
            gravityFactor = Bits(s.gravityFactor),
            walkFactor = Bits(s.walkFactor),
            verticalAxis = Bits(s.verticalAxis),
            horizontalAxis = Bits(s.horizontalAxis),
            wishJump = s.wishJump,
            wishJumpDown = s.wishJumpDown,
            lastConsumedJumpPressFrame = s.lastConsumedJumpPressFrame,
            crouching = s.crouching,
            jumping = s.jumping,
            crouchLerp = Bits(s.crouchLerp),
            renderCrouchLerp = Bits(s.renderCrouchLerp),
            uncrouchDown = s.uncrouchDown,
            maxStaminaBonus = Bits(s.maxStaminaBonus),
            staminaRegenBonus = Bits(s.staminaRegenBonus),
            stamina = Bits(s.stamina),
            staminaRegenTimer = Bits(s.staminaRegenTimer),
            dashTimer = Bits(s.dashTimer),
            currentDashDuration = Bits(s.currentDashDuration),
            dashCooldownTimer = Bits(s.dashCooldownTimer),
            isDashing = s.isDashing,
            canAirDash = s.canAirDash,
            wishDash = s.wishDash,
            lastConsumedDashPressFrame = s.lastConsumedDashPressFrame,
            jumpCount = s.jumpCount,
            jumpTimer = Bits(s.jumpTimer),
            coyoteTimer = Bits(s.coyoteTimer),
            slideRequested = s.slideRequested,
            sliding = s.sliding,
            wasSliding = s.wasSliding,
            slideSpeedCurrent = Bits(s.slideSpeedCurrent),
            slideDirection = TraceVector.Copy(s.slideDirection),
            slideDelay = Bits(s.slideDelay),
            slopeLimit = Bits(s.slopeLimit),
            rigidbodyPushForce = Bits(s.rigidbodyPushForce),
            defaultHeight = Bits(s.defaultHeight),
            crouchingHeight = Bits(s.crouchingHeight),
            crouchingSpeed = Bits(s.crouchingSpeed),
            toggleCrouch = s.toggleCrouch,
            slidingEnabled = s.slidingEnabled,
            laddersEnabled = s.laddersEnabled,
            angledLaddersEnabled = s.angledLaddersEnabled,
            climbingLadder = s.climbingLadder,
            ladderNormal = TraceVector.Copy(s.ladderNormal),
            ladderDirection = TraceVector.Copy(s.ladderDirection),
            ladderClimbDir = TraceVector.Copy(s.ladderClimbDir),
            ladderVelocity = TraceVector.Copy(s.ladderVelocity),
            underwater = s.underwater,
            cameraUnderwater = s.cameraUnderwater,
            grounded = s.grounded,
            groundedTemp = s.groundedTemp,
            fallingVelocity = Bits(s.fallingVelocity),
            useStepOffset = s.useStepOffset,
            stepOffset = Bits(s.stepOffset),
            meleeState = (int)s.meleeState,
            meleeTimer = Bits(s.meleeTimer),
            meleeCooldownTimer = Bits(s.meleeCooldownTimer),
            wishMelee = s.wishMelee,
            meleeHoldTimer = Bits(s.meleeHoldTimer),
            hasHitTarget = s.hasHitTarget,
            meleeHitResolved = s.meleeHitResolved,
            meleeHitTargetObjectId = s.meleeHitTargetObjectId,
            meleeHitResolveTick = s.meleeHitResolveTick,
            wishParry = s.wishParry,
            isParrying = s.isParrying,
            parryTimer = Bits(s.parryTimer),
            parryCooldownTimer = Bits(s.parryCooldownTimer),
            lastConsumedParryPressFrame = s.lastConsumedParryPressFrame,
            dashStartedThisFrame = s.dashStartedThisFrame,
            doubleJumpedThisFrame = s.doubleJumpedThisFrame,
            meleeHitThisFrame = s.meleeHitThisFrame,
            meleeMinChargeReachedThisFrame = s.meleeMinChargeReachedThisFrame,
            meleeCooldownReadyThisFrame = s.meleeCooldownReadyThisFrame,
            parryStartedThisFrame = s.parryStartedThisFrame,
            parrySuccessThisFrame = s.parrySuccessThisFrame,
            gotParriedThisFrame = s.gotParriedThisFrame,
            parryCooldownReadyThisFrame = s.parryCooldownReadyThisFrame,
        };
    }
}
