using UnityEngine;

namespace Fragsurf.Movement {

    [System.Serializable]
    public struct InputFrame {
        public int    frame;
        public int    characterObjectId;  // Identifies which NetworkObject (character) this input belongs to
        public byte   buttons;
        public sbyte  stickX;   // -127 to 127
        public sbyte  stickY;
        public byte   justPressed;   // buttons that are newly down this frame
        public short  lookYaw100;    // yaw in degrees * 100
        public short  lookPitch100;  // pitch in degrees * 100

        public const byte BTN_JUMP   = 1 << 0;
        public const byte BTN_DASH   = 1 << 1;
        public const byte BTN_MELEE  = 1 << 2;
        public const byte BTN_CROUCH = 1 << 3;
        public const byte BTN_BLOCK  = 1 << 4;

        public bool HasButton(byte btn)    => (buttons & btn) != 0;
        public bool IsJustPressed(byte btn)=> (justPressed & btn) != 0;

        public float LookYaw => lookYaw100 / 100f;
        public float LookPitch => lookPitch100 / 100f;

        public static InputFrame Create(int frame, int characterObjectId, byte buttons, sbyte stickX, sbyte stickY, byte justPressed, float yaw, float pitch) {
            return new InputFrame {
                frame = frame,
                characterObjectId = characterObjectId,
                buttons = buttons,
                stickX = stickX,
                stickY = stickY,
                justPressed = justPressed,
                lookYaw100 = QuantizeAngle100(yaw),
                lookPitch100 = QuantizeAngle100(pitch)
            };
        }

        private static short QuantizeAngle100(float angle) {
            float normalized = NormalizeSignedAngle(angle);
            return (short)Mathf.RoundToInt(normalized * 100f);
        }

        private static float NormalizeSignedAngle(float angle) {
            angle %= 360f;
            if (angle > 180f)
                angle -= 360f;
            if (angle < -180f)
                angle += 360f;
            return angle;
        }
    }
}
