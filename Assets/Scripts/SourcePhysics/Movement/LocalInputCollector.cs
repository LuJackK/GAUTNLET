using UnityEngine;
using FishNet.Object;

namespace Fragsurf.Movement {

    public class LocalInputCollector : MonoBehaviour {
        
        private byte _prevButtons;
        private NetworkObject _networkObject;
        private SurfCharacter _surfCharacter;
        private PlayerAiming _playerAiming;
        [SerializeField, Min(1)] private int _edgeInputLatchTicks = 3;
        private int _jumpEdgeTicks;
        private int _dashEdgeTicks;
        private int _meleeEdgeTicks;

        private void Awake() {
            _networkObject = GetComponent<NetworkObject>();
            _surfCharacter = GetComponent<SurfCharacter>();
            _playerAiming = GetComponentInChildren<PlayerAiming>(true);
            if (_networkObject == null) {
                Debug.LogWarning("[LocalInputCollector] NetworkObject not found on this GameObject.", this);
            }
        }

        public InputFrame GatherInput(int frame) {
            byte buttons = 0;
            if (Input.GetButton("Jump"))    buttons |= InputFrame.BTN_JUMP;
            if (Input.GetButton("Sprint"))  buttons |= InputFrame.BTN_DASH;
            if (Input.GetKey(KeyCode.Q))    buttons |= InputFrame.BTN_MELEE;
            if (Input.GetButton("Crouch"))  buttons |= InputFrame.BTN_CROUCH;

            byte rawJustPressed = (byte)(buttons & ~_prevButtons);
            byte justPressed = 0;

            if ((rawJustPressed & InputFrame.BTN_JUMP) != 0)
                _jumpEdgeTicks = Mathf.Max(_jumpEdgeTicks, _edgeInputLatchTicks);
            if ((rawJustPressed & InputFrame.BTN_DASH) != 0)
                _dashEdgeTicks = Mathf.Max(_dashEdgeTicks, _edgeInputLatchTicks);
            if ((rawJustPressed & InputFrame.BTN_MELEE) != 0)
                _meleeEdgeTicks = Mathf.Max(_meleeEdgeTicks, _edgeInputLatchTicks);

            if (_jumpEdgeTicks > 0) {
                justPressed |= InputFrame.BTN_JUMP;
                _jumpEdgeTicks--;
            }

            if (_dashEdgeTicks > 0) {
                justPressed |= InputFrame.BTN_DASH;
                _dashEdgeTicks--;
            }

            if (_meleeEdgeTicks > 0) {
                justPressed |= InputFrame.BTN_MELEE;
                _meleeEdgeTicks--;
            }

            _prevButtons = buttons;

            int characterObjectId = (_networkObject != null) ? _networkObject.ObjectId : -1;
            float yaw = transform.eulerAngles.y;
            float pitch = 0f;

            if (_playerAiming != null && _playerAiming.bodyTransform != null)
                yaw = _playerAiming.bodyTransform.eulerAngles.y;

            if (_surfCharacter != null && _surfCharacter.viewTransform != null)
                pitch = NormalizeSignedAngle(_surfCharacter.viewTransform.eulerAngles.x);

            return InputFrame.Create(
                frame,
                characterObjectId,
                buttons,
                (sbyte)(Input.GetAxisRaw("Horizontal") * 127),
                (sbyte)(Input.GetAxisRaw("Vertical")   * 127),
                justPressed,
                yaw,
                pitch
            );
        }

        private static float NormalizeSignedAngle(float angle) {
            angle %= 360f;
            if (angle > 180f)
                angle -= 360f;
            return angle;
        }
    }
}
