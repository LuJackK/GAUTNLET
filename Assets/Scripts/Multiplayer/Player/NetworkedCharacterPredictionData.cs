using FishNet.Object.Prediction;

namespace Fragsurf.Movement {

    internal struct NetworkedCharacterReplicateData : IReplicateData {
        public InputFrame Input;
        public bool HasInput;
        public uint MovementEpoch;
        private uint _tick;

        public NetworkedCharacterReplicateData(InputFrame input, uint movementEpoch = 0) {
            Input = input;
            HasInput = input.IsValid;
            MovementEpoch = movementEpoch;
            _tick = 0;
        }

        public uint GetTick() => _tick;
        public void SetTick(uint value) => _tick = value;
        public void Dispose() { }
    }

    internal struct NetworkedCharacterReconcileData : IReconcileData {
        public int Frame;
        public uint MovementEpoch;
        public InputFrame LastInput;
        public float DefaultHeight;
        public float CrouchingHeight;
        public float CrouchingSpeed;
        public bool SlidingEnabled;
        public bool UseStepOffset;
        public float StepOffset;
        public float MaxStaminaBonus;
        public float StaminaRegenBonus;
        public UnityEngine.Vector3 Position;
        public UnityEngine.Vector3 Velocity;
        public float Yaw;
        public float Pitch;
        public float Stamina;
        public float StaminaRegenTimer;
        public float DashTimer;
        public float CurrentDashDuration;
        public float DashCooldownTimer;
        public byte IsDashing;
        public byte CanAirDash;
        public int JumpCount;
        public float JumpTimer;
        public float CoyoteTimer;
        public UnityEngine.Vector3 DashDirection;
        public byte AirDashing;
        public byte Grounded;
        public byte GroundedTemp;
        public byte Jumping;
        public byte MoveType;
        public float ForwardMove;
        public float SideMove;
        public float VerticalAxis;
        public float HorizontalAxis;
        public float SurfaceFriction;
        public float GravityFactor;
        public float WalkFactor;
        public float FallingVelocity;
        public byte Crouching;
        public float CrouchLerp;
        public float RenderCrouchLerp;
        public byte UncrouchDown;
        public byte Sliding;
        public byte WasSliding;
        public float SlideSpeedCurrent;
        public UnityEngine.Vector3 SlideDirection;
        public float SlideDelay;
        public float DashSlideTimer;
        public float SlideGraceTimer;
        public byte CanDashJump;
        public float DashJumpAirControlTimer;
        public UnityEngine.Vector3 WallNormal;
        public UnityEngine.Vector3 WallSeenOrigin;
        public float WallJumpFatigueTime;
        public int ConsecutiveWallJumps;
        public byte Mantling;
        public float MantleTime;
        public float MantlePushTimer;
        public UnityEngine.Vector3 MantleStart;
        public UnityEngine.Vector3 MantleDirection;
        public float MantleDistance;
        public float MantleHeight;
        public float MantleOffset;
        public float MantleVerticalTime;
        public byte MantleJumpBuffered;
        public byte DashStartedThisFrame;
        public byte DoubleJumpedThisFrame;
        public byte MeleeHitThisFrame;
        public byte MeleeState;
        public float MeleeTimer;
        public float MeleeCooldownTimer;
        public float MeleeHoldTimer;
        public byte HasHitTarget;
        public byte MeleeHitResolved;
        public int MeleeHitTargetObjectId;
        public int MeleeHitResolveTick;
        public byte IsParrying;
        public float ParryTimer;
        public float ParryCooldownTimer;
        public byte ParryStartedThisFrame;
        public byte ParrySuccessThisFrame;
        public int LastConsumedParryPressFrame;
        public int LastConsumedJumpPressFrame;
        public int LastConsumedDashPressFrame;
        private uint _tick;

        public NetworkedCharacterReconcileData(int frame) {
            Frame = frame;
            MovementEpoch = 0;
            LastInput = InputFrame.Empty();
            DefaultHeight = 2f;
            CrouchingHeight = 1f;
            CrouchingSpeed = 10f;
            SlidingEnabled = false;
            UseStepOffset = false;
            StepOffset = 0f;
            MaxStaminaBonus = 0f;
            StaminaRegenBonus = 0f;
            Position = default;
            Velocity = default;
            Yaw = 0f;
            Pitch = 0f;
            Stamina = 0f;
            StaminaRegenTimer = 0f;
            DashTimer = 0f;
            CurrentDashDuration = 0f;
            DashCooldownTimer = 0f;
            IsDashing = 0;
            CanAirDash = 0;
            JumpCount = 0;
            JumpTimer = 0f;
            CoyoteTimer = 0f;
            DashDirection = default;
            AirDashing = 0;
            Grounded = 0;
            GroundedTemp = 0;
            Jumping = 0;
            MoveType = 0;
            ForwardMove = 0f;
            SideMove = 0f;
            VerticalAxis = 0f;
            HorizontalAxis = 0f;
            SurfaceFriction = 0f;
            GravityFactor = 0f;
            WalkFactor = 0f;
            FallingVelocity = 0f;
            Crouching = 0;
            CrouchLerp = 0f;
            RenderCrouchLerp = 0f;
            UncrouchDown = 0;
            Sliding = 0;
            WasSliding = 0;
            SlideSpeedCurrent = 0f;
            SlideDirection = default;
            SlideDelay = 0f;
            DashSlideTimer = 0f;
            SlideGraceTimer = 0f;
            CanDashJump = 0;
            DashJumpAirControlTimer = 0f;
            WallNormal = default;
            WallSeenOrigin = default;
            WallJumpFatigueTime = 0f;
            ConsecutiveWallJumps = 0;
            Mantling = 0;
            MantleTime = 0f;
            MantlePushTimer = 0f;
            MantleStart = default;
            MantleDirection = default;
            MantleDistance = 0f;
            MantleHeight = 0f;
            MantleOffset = 0f;
            MantleVerticalTime = 0f;
            MantleJumpBuffered = 0;
            DashStartedThisFrame = 0;
            DoubleJumpedThisFrame = 0;
            MeleeHitThisFrame = 0;
            MeleeState = 0;
            MeleeTimer = 0f;
            MeleeCooldownTimer = 0f;
            MeleeHoldTimer = 0f;
            HasHitTarget = 0;
            MeleeHitResolved = 0;
            MeleeHitTargetObjectId = 0;
            MeleeHitResolveTick = -1;
            IsParrying = 0;
            ParryTimer = 0f;
            ParryCooldownTimer = 0f;
            ParryStartedThisFrame = 0;
            ParrySuccessThisFrame = 0;
            LastConsumedParryPressFrame = -1;
            LastConsumedJumpPressFrame = -1;
            LastConsumedDashPressFrame = -1;
            // FishNet supplies the owner-local/server tick on receipt. Direct
            // offline tests can use Frame until that transport tick is supplied.
            _tick = uint.MaxValue;
        }

        public uint GetTick() => _tick;
        public void SetTick(uint value) => _tick = value;
        public void Dispose() { }
    }
}
