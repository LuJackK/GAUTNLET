using UnityEngine;

namespace Fragsurf.Movement {

    [System.Serializable]
    public struct InputFrame {
        public int    frame;
        public byte   buttons;
        public sbyte  stickX;   // -127 to 127
        public sbyte  stickY;
        private byte  _justPressed;   // buttons that are newly down this frame

        public const byte BTN_JUMP   = 1 << 0;
        public const byte BTN_DASH   = 1 << 1;
        public const byte BTN_MELEE  = 1 << 2;
        public const byte BTN_CROUCH = 1 << 3;
        public const byte BTN_BLOCK  = 1 << 4;

        public bool HasButton(byte btn)    => (buttons & btn) != 0;
        public bool IsJustPressed(byte btn)=> (_justPressed & btn) != 0;

        public static InputFrame Create(int frame, byte buttons, sbyte stickX, sbyte stickY, byte justPressed) {
            return new InputFrame {
                frame = frame,
                buttons = buttons,
                stickX = stickX,
                stickY = stickY,
                _justPressed = justPressed
            };
        }
    }
}
