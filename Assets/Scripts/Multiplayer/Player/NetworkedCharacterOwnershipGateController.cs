using System.Collections;
using UnityEngine;

namespace Fragsurf.Movement {

    internal sealed class NetworkedCharacterOwnershipGateController {
        private readonly NetworkedCharacter _owner;

        private Coroutine _delayedGateCoroutine;
        private Coroutine _initialBootstrapCoroutine;
        private Coroutine _spawnPoseFailSafeCoroutine;

        public bool OwnershipGateOpen { get; private set; }
        public bool HasAuthoritativeSpawnPose { get; private set; }

        public bool IsLocalControlReady =>
            _owner.DebugHasLocalAuthority &&
            OwnershipGateOpen &&
            HasAuthoritativeSpawnPose;

        public NetworkedCharacterOwnershipGateController(NetworkedCharacter owner) {
            _owner = owner;
        }

        public void OnNetworkStart() {
            StopAllCoroutines();
            OwnershipGateOpen = false;
            HasAuthoritativeSpawnPose = false;
            _owner.RefreshOwnershipState();
        }

        public void OnNetworkStop() {
            StopAllCoroutines();
            OwnershipGateOpen = false;
            HasAuthoritativeSpawnPose = false;
            _owner.RefreshOwnershipState();
        }

        public void OnClientStart() {
            _owner.RemoveEventSystemsFromPlayerHierarchy("OnStartClient");
            StartInitialOwnershipBootstrap();
        }

        public void OnOwnershipClient() {
            if (_initialBootstrapCoroutine != null) {
                _owner.StopCoroutine(_initialBootstrapCoroutine);
                _initialBootstrapCoroutine = null;
            }

            StartDelayedOwnershipGateRefresh();
        }

        public void OnAuthoritativeSpawnPoseApplied() {
            HasAuthoritativeSpawnPose = true;

            if (_spawnPoseFailSafeCoroutine != null) {
                _owner.StopCoroutine(_spawnPoseFailSafeCoroutine);
                _spawnPoseFailSafeCoroutine = null;
            }

            _owner.RefreshOwnershipState();
        }

        private void StartInitialOwnershipBootstrap() {
            if (!_owner.IsClientInitialized)
                return;

            if (_initialBootstrapCoroutine != null)
                _owner.StopCoroutine(_initialBootstrapCoroutine);

            _initialBootstrapCoroutine = _owner.StartCoroutine(InitialOwnershipBootstrap());
        }

        private IEnumerator InitialOwnershipBootstrap() {
            const int maxFrames = 30;

            for (int waited = 0; waited < maxFrames; waited++) {
                if (!_owner.IsClientInitialized) {
                    _initialBootstrapCoroutine = null;
                    yield break;
                }

                if (_delayedGateCoroutine != null || OwnershipGateOpen) {
                    _initialBootstrapCoroutine = null;
                    yield break;
                }

                if (_owner.DebugHasLocalAuthority) {
                    StartDelayedOwnershipGateRefresh();
                    _initialBootstrapCoroutine = null;
                    yield break;
                }

                yield return null;
            }

            _initialBootstrapCoroutine = null;
        }

        private void StartDelayedOwnershipGateRefresh() {
            OwnershipGateOpen = false;
            _owner.RefreshOwnershipState();

            if (_owner.DebugHasLocalAuthority && !HasAuthoritativeSpawnPose)
                StartSpawnPoseFailSafe();

            if (_delayedGateCoroutine != null)
                _owner.StopCoroutine(_delayedGateCoroutine);

            _delayedGateCoroutine = _owner.StartCoroutine(DelayedOpenGate());
        }

        private IEnumerator DelayedOpenGate() {
            yield return null;

            OwnershipGateOpen = true;
            _owner.RefreshOwnershipState();
            _delayedGateCoroutine = null;
        }

        private void StartSpawnPoseFailSafe() {
            if (_spawnPoseFailSafeCoroutine != null)
                _owner.StopCoroutine(_spawnPoseFailSafeCoroutine);

            _spawnPoseFailSafeCoroutine = _owner.StartCoroutine(SpawnPoseFailSafe());
        }

        private IEnumerator SpawnPoseFailSafe() {
            const int maxFrames = 20;

            for (int waited = 0; waited < maxFrames; waited++) {
                if (!_owner.IsClientInitialized || !_owner.DebugHasLocalAuthority || HasAuthoritativeSpawnPose) {
                    _spawnPoseFailSafeCoroutine = null;
                    yield break;
                }

                yield return null;
            }

            if (_owner.IsClientInitialized && _owner.DebugHasLocalAuthority && !HasAuthoritativeSpawnPose) {
                HasAuthoritativeSpawnPose = true;
                _owner.RefreshOwnershipState();
            }

            _spawnPoseFailSafeCoroutine = null;
        }

        private void StopAllCoroutines() {
            if (_initialBootstrapCoroutine != null) {
                _owner.StopCoroutine(_initialBootstrapCoroutine);
                _initialBootstrapCoroutine = null;
            }

            if (_delayedGateCoroutine != null) {
                _owner.StopCoroutine(_delayedGateCoroutine);
                _delayedGateCoroutine = null;
            }

            if (_spawnPoseFailSafeCoroutine != null) {
                _owner.StopCoroutine(_spawnPoseFailSafeCoroutine);
                _spawnPoseFailSafeCoroutine = null;
            }
        }
    }
}
