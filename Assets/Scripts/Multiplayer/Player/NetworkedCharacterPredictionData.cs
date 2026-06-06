using FishNet.Object.Prediction;

namespace Fragsurf.Movement {

    internal struct NetworkedCharacterReplicateData : IReplicateData {
        public InputFrame Input;
        private uint _tick;

        public NetworkedCharacterReplicateData(InputFrame input) {
            Input = input;
            _tick = 0;
        }

        public uint GetTick() => _tick;
        public void SetTick(uint value) => _tick = value;
        public void Dispose() { }
    }

    internal struct NetworkedCharacterReconcileData : IReconcileData {
        public int Frame;
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
        public byte DashStartedThisFrame;
        public byte DoubleJumpedThisFrame;
        public byte MeleeHitThisFrame;
        public byte MeleeState;
        public float MeleeTimer;
        public float MeleeCooldownTimer;
        public byte HasHitTarget;
        public byte MeleeHitResolved;
        public int MeleeHitTargetObjectId;
        public int MeleeHitResolveTick;
        public byte IsParrying;
        public float ParryTimer;
        public byte ParryStartedThisFrame;
        public byte ParrySuccessThisFrame;
        public int LastConsumedParryPressFrame;
        public int LastConsumedJumpPressFrame;
        public int LastConsumedDashPressFrame;
        private uint _tick;

        public NetworkedCharacterReconcileData(int frame) {
            Frame = frame;
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
            DashStartedThisFrame = 0;
            DoubleJumpedThisFrame = 0;
            MeleeHitThisFrame = 0;
            MeleeState = 0;
            MeleeTimer = 0f;
            MeleeCooldownTimer = 0f;
            HasHitTarget = 0;
            MeleeHitResolved = 0;
            MeleeHitTargetObjectId = 0;
            MeleeHitResolveTick = -1;
            IsParrying = 0;
            ParryTimer = 0f;
            ParryStartedThisFrame = 0;
            ParrySuccessThisFrame = 0;
            LastConsumedParryPressFrame = -1;
            LastConsumedJumpPressFrame = -1;
            LastConsumedDashPressFrame = -1;
            _tick = 0;
        }

        public uint GetTick() => _tick;
        public void SetTick(uint value) => _tick = value;
        public void Dispose() { }
    }
}
