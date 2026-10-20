using UnityEngine;
namespace Fragsurf.Movement {

    public enum MovementModel {
        // GAUNTLET's original movement rules and tuning.
        Legacy,
        // Rules recovered from Deadlock (Research/DeadlockMovement/BEHAVIOURS.md), values in DeadlockMovementConfig.
        Deadlock
    }

    [System.Serializable]
    public class MovementConfig {
        public float GetMaxStamina(MoveData state) => Mathf.Max(0f, maxStamina + (state?.maxStaminaBonus ?? 0f));
        public float GetStaminaRegenRate(MoveData state) => Mathf.Max(0f, staminaRegenRate + (state?.staminaRegenBonus ?? 0f));

        [Header ("Movement model")]
        [Tooltip("Legacy keeps GAUNTLET's original movement. Deadlock uses the rules and values recovered from Deadlock (see Deadlock Movement below). Heavy melee and parry use the legacy rules in both models. Use identical settings on client and server.")]
        public MovementModel movementModel = MovementModel.Legacy;
        public DeadlockMovementConfig deadlock = new DeadlockMovementConfig();

        [Header ("Jumping and gravity")]
        public bool autoBhop = true;
        public float gravity = 20f;
        public float jumpForce = 6.5f;
        [Tooltip("Seconds after losing walkable ground in which a fresh jump press uses the normal jump, without stamina cost. 0 disables coyote jumping. Keep identical on client and server.")]
        [Min(0f)] public float coyoteTime = 0.1f;
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
        [Tooltip ("Offset from the player's origin (feet) for the roll animation pivot center.")]
        public float dashRollPivotOffset = 1f;
        [Tooltip ("Multiplier for the visual 360 rotation speed during a ground dash.")]
        public float dashAnimationSpeedMultiplier = 1f;
        [Tooltip ("Multiplier for the visual 360 rotation speed during an air dash.")]
        public float airDashAnimationSpeedMultiplier = 1f;

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
        [HideInInspector] public bool clampAirSpeed = true;
        [HideInInspector] public float airCap = 0.4f;
        public float airAcceleration = 12f;
        [HideInInspector] public float airFriction = 0.4f;
        [Tooltip("Lateral steering acceleration in metres/second squared. Preserves carried air speed.")]
        public float airControlAcceleration = 18f;
        [Tooltip("0 keeps current air movement exactly. 1 removes direct steering and uses Source-style capped directional acceleration. Higher values make carried momentum harder to redirect. Applies to normal air movement and air dashes, not melee.")]
        [Range(0f, 1f)] public float sourceAirStrafeStrength = 0f;
        [Tooltip("At full Source strafe strength, maximum speed along the input direction that air acceleration can build (m/s). Does not clamp carried speed. Lower values require more deliberate camera/strafe coordination.")]
        [Min(0f)] public float sourceAirSpeedCap = 0.4f;

        [Header ("Ground movement")]
        public float walkSpeed = 7f;
        [HideInInspector] public float sprintSpeed = 12f;
        public float acceleration = 14f;
        public float deceleration = 10f;

        [Header("Ground Detection")]
        public bool useGroundSnapping = true;
        public float groundSnapDistance = 0.3f;

        [Header ("Crouch movement")]
        public float crouchSpeed = 4f;
        public float crouchAcceleration = 8f;
        public float crouchDeceleration = 4f;
        public float crouchFriction = 3f;

        [Header ("Sliding")]
        public float minimumSlideSpeed = 9f;
        [HideInInspector] public float maximumSlideSpeed = 18f;
        [HideInInspector] public float slideSpeedMultiplier = 1.75f;
        [HideInInspector] public float slideFriction = 14f;
        [HideInInspector] public float downhillSlideSpeedMultiplier = 2.5f;
        public float slideDelay = 0.5f;
        [HideInInspector] public float slideSteerForce = 5f;
        public float slideTiltAngle = 15f;
        public float slideTiltSpeed = 10f;
        [Tooltip("Slide braking in metres/second squared. No speed multiplier is applied on entry.")]
        public float slideDrag = 3f;
        public float slideControlAcceleration = 10f;
        public float slideSlopeAcceleration = 1f;

        // Serialized legacy fields are retained for asset compatibility, not simulated.
        [HideInInspector] public float swimUpSpeed = 12f;
        [HideInInspector] public float underwaterSwimSpeed = 3f;
        [HideInInspector] public float underwaterAcceleration = 6f;
        [HideInInspector] public float underwaterDeceleration = 3f;
        [HideInInspector] public float underwaterFriction = 2f;
        [HideInInspector] public float underwaterGravity = 6f;
        [HideInInspector] public float underwaterVelocityDampening = 2f;

        [Header("Heavy Melee")]
        public float heavyMeleeChargeTime = 1.0f; // Max charge duration
        public float heavyMeleeMinCharge = 0.2f; // Min charge to activate
        public float heavyMeleeBaseSpeed = 20f;
        public float heavyMeleeSpeedMultiplier = 1.5f; // With "Item" equivalent
        public float heavyMeleeLungeDuration = 0.4f;
        public float heavyMeleeRecoveryDuration = 0.5f;
        public float heavyMeleeDrag = 0f;
        public float heavyMeleeRecoveryDrag = 3f;
        public float heavyMeleeTurnClamp = 0.7f; // Max degrees per frame or similar metric
        public float heavyMeleeCooldown = 2.0f; // Time between end of recovery and next charge
        
        [Header("Heavy Melee Physics")]
        [Tooltip("Fraction of velocity retained when entering charge state (0-1)")]
        [Range(0f, 1f)]
        [HideInInspector] public float heavyMeleeChargeVelocityRetention = 0.1f;
        [Tooltip("Friction applied while sliding during charge")]
        public float heavyMeleeChargeFriction = 6.0f;
        [Range(0f, 1f)] public float meleeChargeMomentumRetention = 0.8f;

        [Header("Parry")]
        [Tooltip("How long the parry window stays active after pressing parry.")]
        public float parryDuration = 0.35f;
        [Tooltip("Minimum seconds after a parry starts before another parry can start.")]
        public float parryCooldown = 0.35f;

        [Header("Hitboxes & Hurtboxes")]
        public HitboxDefinition heavyMeleeHitbox = new HitboxDefinition {
            id = "gauntlet_heavy",
            shape = ShapeType.Cone,
            radius = 3.0f,
            coneAngle = 0.7f, // ~45 degrees
            offset = new Vector3(0f, 0f, 0.5f),
            damage = 50f,
            hitForce = 15f
        };

        public HurtboxDefinition playerHurtbox = new HurtboxDefinition {
            id = "player",
            shape = ShapeType.Box,
            size = new Vector3(1.0f, 2.0f, 1.0f),
            offset = new Vector3(0f, 1.0f, 0f)
        };
    }

    public enum ShapeType {
        Box,
        Sphere,
        Cone
    }

    [System.Serializable]
    public struct HitboxDefinition {
        public string id;
        public ShapeType shape;
        public Vector3 size;
        public float radius;
        public Vector3 offset;
        public Vector3 rotationOffset;
        public float damage;
        public float hitForce;
        [Tooltip("Used for Cone shape. 1 = narrow, 0 = 90 deg, -1 = 180 deg")]
        public float coneAngle;
    }

    [System.Serializable]
    public struct HurtboxDefinition {
        public string id;
        public ShapeType shape;
        public Vector3 size;
        public float radius;
        public Vector3 offset;
    }
}
