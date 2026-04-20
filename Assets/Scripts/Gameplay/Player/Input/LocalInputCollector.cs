using UnityEngine;
using FishNet.Object;
using System;

namespace Fragsurf.Movement {

    public class LocalInputCollector : MonoBehaviour {

        private struct BufferedInputState {
            public byte Buttons;
            public sbyte StickX;
            public sbyte StickY;
            public float Yaw;
            public float Pitch;
        }

        private byte _prevButtons;
        private NetworkObject _networkObject;
        private SurfCharacter _surfCharacter;
        private PlayerAiming _playerAiming;
        private BufferedInputState _bufferedState;
        private BufferedInputState _pendingDirectionalState;
        private BufferedInputState _pendingReleaseDirectionalState;
        private byte _pendingPressedButtons;
        private byte _pendingJustPressed;
        private bool _hasPendingDirectionalState;
        private bool _hasPendingReleaseDirectionalState;

        public event Action<InputFrame> InputGathered;

        private void Awake() {
            _networkObject = GetComponent<NetworkObject>();
            _surfCharacter = GetComponent<SurfCharacter>();
            _playerAiming = GetComponentInChildren<PlayerAiming>(true);
            if (_networkObject == null) {
                Debug.LogWarning("[LocalInputCollector] NetworkObject not found on this GameObject.", this);
            }

            SampleBufferedState();
        }

        private void OnEnable() {
            SampleBufferedState();
        }

        private void Update() {
            SampleBufferedState();
        }

        public InputFrame GatherInput(int frame) {
            byte justPressed = _pendingJustPressed;
            bool usePressDirectionalSnapshot = _hasPendingDirectionalState
                && (justPressed & (InputFrame.BTN_DASH | InputFrame.BTN_MELEE)) != 0;
            byte buttons = (byte)(_bufferedState.Buttons | _pendingPressedButtons);
            bool useReleaseDirectionalSnapshot = !usePressDirectionalSnapshot
                && _hasPendingReleaseDirectionalState
                && (buttons & InputFrame.BTN_MELEE) == 0;
            BufferedInputState state = usePressDirectionalSnapshot
                ? _pendingDirectionalState
                : (useReleaseDirectionalSnapshot ? _pendingReleaseDirectionalState : _bufferedState);
            _pendingPressedButtons = 0;
            _pendingJustPressed = 0;
            _hasPendingDirectionalState = false;
            _hasPendingReleaseDirectionalState = false;

            int characterObjectId = (_networkObject != null) ? _networkObject.ObjectId : -1;
            InputFrame gathered = InputFrame.Create(
                frame,
                characterObjectId,
                buttons,
                state.StickX,
                state.StickY,
                justPressed,
                state.Yaw,
                state.Pitch
            );

            InputGathered?.Invoke(gathered);
            return gathered;
        }

        public void ResetState(bool preserveHeldButtons = false) {
            _pendingPressedButtons = 0;
            _pendingJustPressed = 0;
            _hasPendingDirectionalState = false;
            _hasPendingReleaseDirectionalState = false;
            _bufferedState = default;
            _pendingDirectionalState = default;
            _pendingReleaseDirectionalState = default;
            _prevButtons = preserveHeldButtons ? ReadCurrentButtons() : (byte)0;
            SampleBufferedState();
        }

        private void SampleBufferedState() {
            BufferedInputState state = default;
            state.Buttons = ReadCurrentButtons();
            state.StickX = ReadAxisByte("Horizontal");
            state.StickY = ReadAxisByte("Vertical");
            ReadCurrentLook(out state.Yaw, out state.Pitch);

            byte justPressed = (byte)(state.Buttons & ~_prevButtons);
            if (justPressed != 0) {
                _pendingPressedButtons |= justPressed;
                _pendingJustPressed |= justPressed;
                if ((justPressed & (InputFrame.BTN_DASH | InputFrame.BTN_MELEE)) != 0) {
                    _pendingDirectionalState = state;
                    _hasPendingDirectionalState = true;
                }
            }

            byte justReleased = (byte)(_prevButtons & ~state.Buttons);
            if ((justReleased & InputFrame.BTN_MELEE) != 0) {
                _pendingReleaseDirectionalState = state;
                _hasPendingReleaseDirectionalState = true;
            }

            _bufferedState = state;
            _prevButtons = state.Buttons;
        }

        private void ReadCurrentLook(out float yaw, out float pitch) {
            yaw = transform.eulerAngles.y;
            pitch = 0f;

            if (_playerAiming != null && _playerAiming.bodyTransform != null)
                yaw = _playerAiming.bodyTransform.eulerAngles.y;

            if (_surfCharacter != null && _surfCharacter.viewTransform != null)
                pitch = NormalizeSignedAngle(_surfCharacter.viewTransform.eulerAngles.x);
        }

        private static sbyte ReadAxisByte(string axisName) {
            return (sbyte)Mathf.RoundToInt(Mathf.Clamp(Input.GetAxisRaw(axisName), -1f, 1f) * 127f);
        }

        private static byte ReadCurrentButtons() {
            byte buttons = 0;
            if (Input.GetButton("Jump"))    buttons |= InputFrame.BTN_JUMP;
            if (Input.GetButton("Sprint"))  buttons |= InputFrame.BTN_DASH;
            if (Input.GetKey(KeyCode.Q))    buttons |= InputFrame.BTN_MELEE;
            if (Input.GetButton("Crouch"))  buttons |= InputFrame.BTN_CROUCH;
            return buttons;
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
