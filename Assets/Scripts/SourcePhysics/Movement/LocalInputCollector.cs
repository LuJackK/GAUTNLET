using UnityEngine;

namespace Fragsurf.Movement {

    public class LocalInputCollector : MonoBehaviour {
        
        private byte _prevButtons;

        public InputFrame GatherInput(int frame) {
            byte buttons = 0;
            if (Input.GetButton("Jump"))    buttons |= InputFrame.BTN_JUMP;
            if (Input.GetButton("Sprint"))  buttons |= InputFrame.BTN_DASH;
            if (Input.GetKey(KeyCode.Q))    buttons |= InputFrame.BTN_MELEE;
            if (Input.GetButton("Crouch"))  buttons |= InputFrame.BTN_CROUCH;

            byte justPressed = (byte)(buttons & ~_prevButtons);
            _prevButtons = buttons;

            return InputFrame.Create(
                frame,
                buttons,
                (sbyte)(Input.GetAxisRaw("Horizontal") * 127),
                (sbyte)(Input.GetAxisRaw("Vertical")   * 127),
                justPressed
            );
        }
    }
}
