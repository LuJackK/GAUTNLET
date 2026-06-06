using UnityEngine;

namespace Fragsurf.Movement {

    public enum MoveType {
        None,
        Walk,
        Noclip, // not implemented
        Ladder, // not implemented
        HeavyMelee
    }

    public class MoveData {
        
        public int frame = -1;
        
        ///// Types /////

        public enum MeleeState { None, Charging, Lunging, Recovery }

        ///// Fields /////

        public MoveType moveType = MoveType.Walk;
        
        public Transform playerTransform;
        public Transform viewTransform;
        public Vector3 viewTransformDefaultLocalPos;
        
        public Vector3 origin;
        public Vector3 viewAngles;
        public Vector3 velocity;
        public float forwardMove;
        public float sideMove;
        public float upMove;
        public float surfaceFriction = 1f;
        public float gravityFactor = 1f;
        public float walkFactor = 1f;
        public float verticalAxis = 0f;
        public float horizontalAxis = 0f;
        public bool wishJump = false;
        public bool wishJumpDown = false;
        public int lastConsumedJumpPressFrame = -1;
        public bool crouching = false;
        public bool jumping = false;
        public float crouchLerp = 0f;
        public float renderCrouchLerp = 0f;
        public bool uncrouchDown = false;

        // Stamina & Dash
        public float stamina = 3f;
        public float staminaRegenTimer = 0f;
        public float dashTimer = 0f;
        public float currentDashDuration = 0f;
        public float dashCooldownTimer = 0f;
        public bool isDashing = false;
        public bool canAirDash = true;
        public bool wishDash = false;
        public int lastConsumedDashPressFrame = -1;
        public int jumpCount = 0;
        public float jumpTimer = 0f;

        // Sliding
        public bool slideRequested = false;
        public bool sliding = false;
        public bool wasSliding = false;
        public float slideSpeedCurrent = 0f;
        public Vector3 slideDirection = Vector3.forward;
        public float slideDelay = 0f;


        public float slopeLimit = 45f;

        public float rigidbodyPushForce = 1f;

        public float defaultHeight = 2f;
        public float crouchingHeight = 1f;
        public float crouchingSpeed = 10f;
        public bool toggleCrouch = false;

        public bool slidingEnabled = false;
        public bool laddersEnabled = false;
        public bool angledLaddersEnabled = false;
        
        public bool climbingLadder = false;
        public Vector3 ladderNormal = Vector3.zero;
        public Vector3 ladderDirection = Vector3.forward;
        public Vector3 ladderClimbDir = Vector3.up;
        public Vector3 ladderVelocity = Vector3.zero;

        public bool underwater = false;
        public bool cameraUnderwater = false;

        public bool grounded = false;
        public bool groundedTemp = false;
        public float fallingVelocity = 0f;

        public bool useStepOffset = false;
        public float stepOffset = 0f;

        // Heavy Melee
        public MeleeState meleeState = MeleeState.None;
        public float meleeTimer = 0f;
        public float meleeCooldownTimer = 0f;
        public bool wishMelee = false;
        public float meleeHoldTimer = 0f;
        public bool hasHitTarget = false;
        public bool meleeHitResolved = false;
        public int meleeHitTargetObjectId = 0;
        public int meleeHitResolveTick = -1;
        public bool wishParry = false;
        public bool isParrying = false;
        public float parryTimer = 0f;
        public int lastConsumedParryPressFrame = -1;
        // One-frame events for animation/VFX
        public bool dashStartedThisFrame = false;
        public bool doubleJumpedThisFrame = false;
        public bool meleeHitThisFrame = false;
        public bool parryStartedThisFrame = false;
        public bool parrySuccessThisFrame = false;

        ///// Methods /////

        public MoveData Clone() {
            return (MoveData)this.MemberwiseClone();
        }

    }
}
