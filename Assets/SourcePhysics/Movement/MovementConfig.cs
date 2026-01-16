using UnityEngine;
namespace Fragsurf.Movement {

    [System.Serializable]
    public class MovementConfig {

        [Header ("Jumping and gravity")]
        public bool autoBhop = true;
        public float gravity = 20f;
        public float jumpForce = 6.5f;
        public int maxJumps = 2;
        public float doubleJumpForce = 8f;
        public float doubleJumpDelay = 0.2f;

        [Header ("Stamina")]
        public float maxStamina = 3f;
        public float staminaRegenRate = 1f; // bars per second
        public float staminaRegenDelay = 1.0f; // delay after last consumer

        [Header ("Dash")]
        public float dashImpulse = 20f;
        public float dashDuration = 0.3f;
        public float dashCooldown = 0.5f;
        public float dashFrictionOverride = 0.5f;
        public float airDashDuration = 0.2f;
        public float airDashVelocity = 25f;

        [Header ("Costs")]
        public float dashCost = 1f;
        public float airDashCost = 1f;
        public float doubleJumpCost = 1f;
        
        [Header ("General physics")]
        public float friction = 6f;
        public float maxSpeed = 6f;
        public float maxVelocity = 50f;
        [Range (30f, 75f)] public float slopeLimit = 45f;

        [Header ("Air movement")]
        public bool clampAirSpeed = true;
        public float airCap = 0.4f;
        public float airAcceleration = 12f;
        public float airFriction = 0.4f;

        [Header ("Ground movement")]
        public float walkSpeed = 7f;
        public float sprintSpeed = 12f;
        public float acceleration = 14f;
        public float deceleration = 10f;

        [Header ("Crouch movement")]
        public float crouchSpeed = 4f;
        public float crouchAcceleration = 8f;
        public float crouchDeceleration = 4f;
        public float crouchFriction = 3f;

        [Header ("Sliding")]
        public float minimumSlideSpeed = 9f;
        public float maximumSlideSpeed = 18f;
        public float slideSpeedMultiplier = 1.75f;
        public float slideFriction = 14f;
        public float downhillSlideSpeedMultiplier = 2.5f;
        public float slideDelay = 0.5f;
        public float slideSteerForce = 5f;
        public float slideTiltAngle = 15f;
        public float slideTiltSpeed = 10f;

        [Header ("Underwater")]
        public float swimUpSpeed = 12f;
        public float underwaterSwimSpeed = 3f;
        public float underwaterAcceleration = 6f;
        public float underwaterDeceleration = 3f;
        public float underwaterFriction = 2f;
        public float underwaterGravity = 6f;
        public float underwaterVelocityDampening = 2f;

        [Header("Heavy Melee")]
        public float heavyMeleeChargeTime = 1.0f; // Max charge duration
        public float heavyMeleeMinCharge = 0.2f; // Min charge to activate
        public float heavyMeleeBaseSpeed = 20f;
        public float heavyMeleeSpeedMultiplier = 1.5f; // With "Item" equivalent
        public float heavyMeleeLungeDuration = 0.4f;
        public float heavyMeleeRecoveryDuration = 0.5f;
        public float heavyMeleeDrag = 0f;
        public float heavyMeleeRecoveryDrag = 3f;
        public float heavyMeleeHitRange = 3f;
        public float heavyMeleeConeAngle = 0.5f; // Dot product threshold (~60 deg)
        public float heavyMeleeDamage = 50f;
        public float heavyMeleeTurnClamp = 0.7f; // Max degrees per frame or similar metric

        
    }

}
