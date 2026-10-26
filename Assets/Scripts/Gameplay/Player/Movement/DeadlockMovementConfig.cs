using UnityEngine;

namespace Fragsurf.Movement {
    // Values recovered from Deadlock (Research/DeadlockMovement/BEHAVIOURS.md, GAUNTLET_MAPPING.md).
    // Deadlock works in Source units; 1 unit = 0.0254 m, decoded from the game's own conversion.
    // Defaults are the base hero; per-hero values (e.g. Mina) are noted in tooltips.
    [System.Serializable]
    public class DeadlockMovementConfig {
        public const float Unit = 0.0254f;

        [Header("Ground")]
        [Tooltip("Hero EMaxMoveSpeed, m/s. Base hero 7.2; Mina about 8.1 (318.9 u/s measured).")]
        public float walkSpeed = 7.2f;
        [Tooltip("Hero ECrouchSpeed, m/s (4.75 = 187 u/s, confirmed as the crouched wish speed).")]
        public float crouchSpeed = 4.75f;
        [Tooltip("Hero EMoveAcceleration (dimensionless, per second). Confirmed: +25 u/s per tick at 64 Hz from rest.")]
        public float acceleration = 4f;
        [Tooltip("citadel_min_accel_speed, m/s: acceleration uses at least this wish speed (400 u/s).")]
        public float minAccelSpeed = 400f * Unit;
        [Tooltip("sv_friction (per second).")]
        public float friction = 4f;
        [Tooltip("sv_stopspeed, m/s (100 u/s): below this speed friction removes a constant amount.")]
        public float stopSpeed = 100f * Unit;
        [Tooltip("sv_gravity, m/s² (800 u/s²).")]
        public float gravity = 800f * Unit;

        [Header("Jumping")]
        [Tooltip("Ground jump sets vertical speed, m/s (VerticalSpeed 300 u/s, confirmed).")]
        public float jumpSpeed = 300f * Unit;
        [Tooltip("Late-jump (coyote) window after walking off ground while falling, s (m_flLateJumpGraceWindow, confirmed).")]
        public float lateJumpWindow = 0.15f;
        [Tooltip("Air jump sets vertical speed, m/s: 300 x (1 + AirJumpVerticalSpeedPercent 75%) = 525 u/s, confirmed.")]
        public float airJumpSpeed = 525f * Unit;
        [Tooltip("Air jump adds horizontal speed along input up to walk speed, at most this much, m/s (m_flMaxSpeedDelta 200 u/s, confirmed).")]
        public float airJumpMaxSpeedDelta = 200f * Unit;
        [Tooltip("Stamina per air jump.")]
        public float airJumpCost = 1f;

        [Header("Air")]
        [Tooltip("sv_airaccelerate (per second).")]
        public float airAccelerate = 10f;
        [Tooltip("Air wish-speed cap at low carried speed, m/s (citadel_aircontrol_speed_slow 80 u/s). Decoded; capture test pending.")]
        public float airControlSpeedSlow = 80f * Unit;
        [Tooltip("Air wish-speed cap at high carried speed, m/s (citadel_aircontrol_speed_fast 50 u/s). Decoded; capture test pending.")]
        public float airControlSpeedFast = 50f * Unit;
        [Tooltip("Carried horizontal speed where the cap starts and finishes blending from slow to fast, m/s (600 and 800 u/s).")]
        public Vector2 airControlBlendSpeeds = new Vector2(600f * Unit, 800f * Unit);
        [Tooltip("citadel_air_drag_min: horizontal air drag, same law as ground friction (confirmed 0.2).")]
        public float airDrag = 0.2f;

        [Header("Ground dash")]
        [Tooltip("EGroundDashDistanceInMeters. Same for all heroes (10 m).")]
        public float groundDashDistance = 10f;
        [Tooltip("EGroundDashDuration, s. Base hero 0.68 (14.7 m/s); Mina/bucket 1 0.62 (635 u/s, confirmed).")]
        public float groundDashDuration = 0.68f;
        [Tooltip("Stamina per ground dash.")]
        public float groundDashCost = 1f;

        [Header("Air dash")]
        [Tooltip("EAirDashDistanceInMeters (8 m).")]
        public float airDashDistance = 8f;
        [Tooltip("EAirDashDuration, s. Base hero 0.47; Mina/bucket 1 0.43 (confirmed).")]
        public float airDashDuration = 0.47f;
        [Tooltip("m_flAirDashEndVelocityScale: the dash's target speed shrinks linearly to this fraction over the dash.")]
        public float airDashEndVelocityScale = 0.2f;
        [Tooltip("1 + m_flAirDashAccPct (-40%): air acceleration multiplier during the dash.")]
        public float airDashAccelerationScale = 0.6f;
        [Tooltip("Vertical speed is held at max(vz, this) x dt during the dash, m/s (100 u/s; m_flDuringDrag 1).")]
        public float airDashVerticalHold = 100f * Unit;
        [Tooltip("Stamina per air dash.")]
        public float airDashCost = 1f;

        [Header("Stamina")]
        [Tooltip("EStaminaRegenPerSecond, bars per second (base hero 0.222 = 4.5 s per bar; Mina 4.2 s, measured 0.24).")]
        public float staminaRegenRate = 1f / 4.5f;

        [Header("Slide")]
        [Tooltip("m_flMinActivationSpeed, m/s (350 u/s): flat-ground speed needed to start a slide.")]
        public float slideActivationSpeed = 350f * Unit;
        [Tooltip("m_flDashMinActivationSpeed, m/s (250 u/s): speed needed while ground dashing.")]
        public float slideDashActivationSpeed = 250f * Unit;
        [Tooltip("m_flMinSustainSpeed, m/s (170 u/s): the slide ends below this on flat ground (confirmed).")]
        public float slideSustainSpeed = 170f * Unit;
        [Tooltip("m_flFrictionFlatGround: friction coefficient while sliding on flat ground (confirmed 0.8).")]
        public float slideFriction = 0.8f;
        [Tooltip("m_flFrictionFlatGroundGrace: coefficient during the start-of-slide grace (decoded 0.05; capture test pending).")]
        public float slideGraceFriction = 0.05f;
        [Tooltip("m_flFlatGroundFrictionGraceTime, s (decoded 0.6).")]
        public float slideGraceTime = 0.6f;
        [Tooltip("m_flLandedFlatGroundFrictionGraceTime, s, when the slide starts on landing (decoded 0.75).")]
        public float slideLandedGraceTime = 0.75f;
        [Tooltip("m_flDashSlideStartTime, s: crouching at least this long into a ground dash gives the full dash-slide speed (confirmed 0.3).")]
        public float dashSlideStartTime = 0.3f;
        [Tooltip("m_flDashSlideSpeed, m/s (650 u/s, confirmed).")]
        public float dashSlideSpeed = 650f * Unit;
        [Tooltip("m_flDashSlideFailSpeed, m/s (450 u/s, confirmed): speed when crouching too early in the dash.")]
        public float dashSlideFailSpeed = 450f * Unit;
        [Tooltip("m_flDashSlideFriction (confirmed 0.175; the game stores 11/64).")]
        public float dashSlideFriction = 0.175f;
        [Tooltip("m_flDashSlideFrictionTime, s (confirmed 1.0).")]
        public float dashSlideFrictionTime = 1f;

        [Header("Slide on slopes (decoded; capture test pending)")]
        [Tooltip("m_flMinAngleToConsiderASlope, degrees.")]
        public float slopeMinAngle = 8f;
        [Tooltip("m_flAccMinSlopeDeg / m_flAccMaxSlopeDeg, degrees: slope values blend between these angles.")]
        public Vector2 slopeAngleRange = new Vector2(5f, 45f);
        [Tooltip("m_flSlideMin/MaxSlopeAcceleration, m/s² (500 and 850 u/s²).")]
        public Vector2 slopeAcceleration = new Vector2(500f * Unit, 850f * Unit);
        [Tooltip("m_flSlideMin/MaxSlopeMaxAccSpeed, m/s (1200 and 1500 u/s): slope acceleration stops at this speed.")]
        public Vector2 slopeMaxAccelerationSpeed = new Vector2(1200f * Unit, 1500f * Unit);
        [Tooltip("m_flFrictionMinSlope / MaxSlope going downhill.")]
        public Vector2 slopeFriction = new Vector2(0.15f, 0.15f);
        [Tooltip("m_flFrictionUphillMinSlope / MaxSlope going uphill.")]
        public Vector2 slopeUphillFriction = new Vector2(1f, 1.5f);

        [Tooltip("Slide steering: velocity turns toward the input direction at up to this rate, degrees per second (m_flTurnSpeed 90). Decoded; capture test pending.")]
        public float slideTurnSpeed = 90f;

        [Header("Dash-jump")]
        [Tooltip("m_flDashJumpStartTime / m_flDashJumpEndTime, s after a ground dash starts. Confirmed working at 0.31-0.33 s; the end (0.5 or 0.55) is untested.")]
        public Vector2 dashJumpWindow = new Vector2(0.3f, 0.55f);
        [Tooltip("Dash-jump sets vertical speed, m/s (m_flDashJumpVerticalSpeed 425 u/s, confirmed).")]
        public float dashJumpVerticalSpeed = 425f * Unit;
        [Tooltip("Dash-jump sets horizontal speed along the dash, m/s: 19 m over the flat flight time 2 x 425 / 800 s = 704 u/s (confirmed).")]
        public float dashJumpHorizontalSpeed = 704f * Unit;
        [Tooltip("Stamina per dash-jump (confirmed 1, on top of the dash).")]
        public float dashJumpCost = 1f;
        [Tooltip("m_flDashJumpMissMaxSpeed, m/s (250 u/s): a jump during the dash outside the window keeps at most this horizontal speed and locks the dash-jump out. Decoded; capture test pending.")]
        public float dashJumpMissMaxSpeed = 250f * Unit;
        [Tooltip("Extra air control after a dash-jump lasts until this long after the dash started, s (measured 37-38 ticks).")]
        public float dashJumpAirControlTime = 0.6f;
        [Tooltip("Air acceleration multiplier during the extra air control (measured 1.3).")]
        public float dashJumpAirControlScale = 1.3f;

        [Header("Wall jump")]
        [Tooltip("m_flWallJumpMinOutSpeed / MaxOutSpeed, m/s (225 and 310 u/s): speed added away from the wall, by input direction (into the wall = min, none = midpoint, away = max). Confirmed.")]
        public Vector2 wallJumpOutSpeed = new Vector2(225f * Unit, 310f * Unit);
        [Tooltip("m_flWallJumpUpSpeed, m/s (525 u/s), scaled by fatigue power. Confirmed.")]
        public float wallJumpUpSpeed = 525f * Unit;
        [Tooltip("Falling below this vertical speed keeps a fraction of it (100 u/s, confirmed).")]
        public float wallJumpKeepBelowSpeed = 100f * Unit;
        [Tooltip("Fraction of vertical speed kept when below the threshold (0.3, confirmed).")]
        public float wallJumpVerticalKeep = 0.3f;
        [Tooltip("m_flWallJumpMaxLateralSpeed, m/s (100 u/s): speed added along the wall by input along it. Confirmed.")]
        public float wallJumpLateralSpeed = 100f * Unit;
        [Tooltip("Along-wall speeds where the lateral push starts and finishes fading out, m/s (250 and 550 u/s, m_WallJumpLateralSpeedFalloffVsAlongSpeed).")]
        public Vector2 wallJumpLateralFalloffSpeeds = new Vector2(250f * Unit, 550f * Unit);
        [Tooltip("Hermite tangents of that falloff curve at its two ends, scaled to the curve span (vdata slopes -0.000483 and -0.00019 per u/s, times 300).")]
        public Vector2 wallJumpLateralFalloffTangents = new Vector2(-0.1449f, -0.057f);
        [Tooltip("m_flWallJumpFullPowerRechargeTime, s: power recovers from the minimum to full over this time after a wall jump. Confirmed.")]
        public float wallJumpFatigueTime = 1.25f;
        [Tooltip("m_flWallJumpPowerMin (0.2) and m_flWallJumpPowerBias (0.2, Schlick bias). Confirmed to 4 decimals.")]
        public Vector2 wallJumpPowerMinAndBias = new Vector2(0.2f, 0.2f);
        [Tooltip("citadel_wall_jump_num_free_wall_jumps: wall jumps since landing that cost the initial cost (1, confirmed).")]
        public int wallJumpFreeCount = 1;
        [Tooltip("citadel_initial_wall_jump_stamina_cost (0) and citadel_wall_jump_stamina_cost (0.5). Confirmed. A wall jump needs stamina above its cost.")]
        public Vector2 wallJumpCosts = new Vector2(0f, 0.5f);
        [Tooltip("Wall detection distance by speed: m_flRemapSpeedToWallJumpVelocityDist [300, 600] u/s -> [10, 30] u. x/y are the speeds, z/w the distances, m/s and m.")]
        public Vector4 wallDetectionBySpeed = new Vector4(300f * Unit, 600f * Unit, 10f * Unit, 30f * Unit);
        [Tooltip("citadel_wall_detection_skin, m (8 u): the confirming trace toward the wall.")]
        public float wallDetectionSkin = 8f * Unit;
        [Tooltip("Steepest wall normal accepted, as |normal.y| (0.707 = within 45 degrees of vertical).")]
        public float wallMaxNormalY = 0.707f;

        [Header("Mantle")]
        [Tooltip("Mantle types by ledge height (m): x = type height (EMantle32/64/96/128), y = min, z = max, w = vertical time (s). The type whose height is nearest the ledge is used. Times are Deadlock's 0.2/0.3/0.4/0.5 s rounded to whole 64 Hz ticks as the game does (13, 19, 26, 32 ticks, confirmed).")]
        public Vector4[] mantleTypes = {
            new Vector4(32f * Unit, 0f, 64f * Unit, 13f / 64f),
            new Vector4(64f * Unit, 32f * Unit, 96f * Unit, 19f / 64f),
            new Vector4(96f * Unit, 64f * Unit, 128f * Unit, 26f / 64f),
            new Vector4(128f * Unit, 96f * Unit, 160f * Unit, 32f / 64f),
        };
        [Tooltip("Highest ledge mantled from the ground, m (citadel_mantle_max_height 134 u). In the air the highest type's max applies (160 u, measured 156).")]
        public float mantleMaxGroundHeight = 134f * Unit;
        [Tooltip("Lowest ledge mantled, m: ledges within step height are walked (sv_stepsize 18 u).")]
        public float mantleMinHeight = 18f * Unit;
        [Tooltip("citadel_mantle_probe_depth, m (32 u): how far ahead a wall must be.")]
        public float mantleProbeDepth = 32f * Unit;
        [Tooltip("Horizontal distance moved onto the ledge, m (measured 36 u).")]
        public float mantleHorizontalDistance = 36f * Unit;
        [Tooltip("Time for the horizontal part, s (0.2 s rounded to 13 ticks, measured).")]
        public float mantleHorizontalTime = 13f / 64f;
        [Tooltip("m_flAutoMantlePushTime, s: pushing into a ledge this long starts the mantle.")]
        public float mantlePushTime = 0.06f;
        [Tooltip("m_flAutoMantleBlockedMaxSpeed, m/s (60 u/s): pushing counts while speed toward the wall is below this.")]
        public float mantleBlockedMaxSpeed = 60f * Unit;
        [Tooltip("Height kept above the ledge while moving onto it, m.")]
        public float mantleClearance = 0.02f;
        [Tooltip("Mantle slide: speed added at the exit when crouch and forward are held, m/s (measured +400 u/s).")]
        public float mantleSlideBoost = 400f * Unit;
        [Tooltip("A jump pressed this long before the mantle ends fires as a ground jump when it ends, s (measured: 3-8 ticks work, 17-22 do not).")]
        public float mantleJumpBuffer = 0.125f;

        public float GroundDashSpeed => groundDashDuration > 0f ? groundDashDistance / groundDashDuration : 0f;
        public float AirDashSpeed => airDashDuration > 0f ? airDashDistance / airDashDuration : 0f;
    }
}
