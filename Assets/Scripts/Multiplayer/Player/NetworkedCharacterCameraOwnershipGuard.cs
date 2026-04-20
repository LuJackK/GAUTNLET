using UnityEngine;

namespace Fragsurf.Movement {

    internal sealed class NetworkedCharacterCameraOwnershipGuard {
        private const string MainCameraTag = "MainCamera";
        private const string UntaggedTag = "Untagged";

        private readonly NetworkedCharacter _owner;

        public NetworkedCharacterCameraOwnershipGuard(NetworkedCharacter owner) {
            _owner = owner;
        }

        public void ApplyOwnershipState() {
            bool shouldEnable = _owner.DebugHasLocalAuthority;

            ApplyVirtualCameraState(shouldEnable);

            SurfCharacter character = _owner.Character;
            if (character == null || character.viewTransform == null)
                return;

            Camera cam = character.viewTransform.GetComponentInChildren<Camera>(true);
            if (cam != null) {
                cam.enabled = shouldEnable;
                cam.tag = shouldEnable ? MainCameraTag : UntaggedTag;
            }

            AudioListener listener = character.viewTransform.GetComponentInChildren<AudioListener>(true);
            if (listener != null)
                listener.enabled = shouldEnable;
        }

        public void LateUpdate() {
            if (_owner.EnforceCameraOwnershipInvariant)
                EnsureCameraOwnershipInvariant();

            if (_owner.EnforceVirtualCameraOwnershipInvariant)
                EnsureVirtualCameraOwnershipInvariant();
        }

        private void EnsureCameraOwnershipInvariant() {
            SurfCharacter character = _owner.Character;
            if (character == null || character.viewTransform == null)
                return;

            if (!_owner.HasResolvedLocalConnection)
                return;

            bool shouldEnable = _owner.DebugHasLocalAuthority;
            Camera cam = character.viewTransform.GetComponentInChildren<Camera>(true);
            AudioListener listener = character.viewTransform.GetComponentInChildren<AudioListener>(true);

            if (cam != null) {
                cam.enabled = shouldEnable;
                cam.tag = shouldEnable ? MainCameraTag : UntaggedTag;
            }

            if (listener != null)
                listener.enabled = shouldEnable;
        }

        private void EnsureVirtualCameraOwnershipInvariant() {
            if (!_owner.HasResolvedLocalConnection)
                return;

            ApplyVirtualCameraState(_owner.DebugHasLocalAuthority);
        }

        private void ApplyVirtualCameraState(bool shouldEnable) {
            PlayerAiming playerAiming = _owner.PlayerAiming;
            if (playerAiming == null || playerAiming.freeLookCamera == null)
                return;

            var vcam = playerAiming.freeLookCamera;
            if (!vcam.transform.IsChildOf(_owner.transform))
                return;

            if (vcam.gameObject.activeSelf != shouldEnable)
                vcam.gameObject.SetActive(shouldEnable);

            if (vcam.enabled != shouldEnable)
                vcam.enabled = shouldEnable;
        }
    }
}
