using UnityEngine;
using FishNet.Object;
using FishNet.Managing.Timing;

namespace Fragsurf.Movement {

    public class NetworkedCharacter : NetworkBehaviour {
        
        [SerializeField] private SurfCharacter _character;
        [SerializeField] private RollbackManager _rollback;
        [SerializeField] private LocalInputCollector _inputCollector;

        private void Awake() {
            if (_character == null) _character = GetComponent<SurfCharacter>();
            if (_rollback == null) _rollback = GetComponent<RollbackManager>();
            if (_inputCollector == null) _inputCollector = GetComponent<LocalInputCollector>();

            if (_rollback != null && _character != null) {
                _rollback.Initialize(_character);
            }
        }

        public override void OnStartClient() {
            base.OnStartClient();
            
            if (IsOwner) {
                // Setup local camera
                if (_character != null && _character.viewTransform != null) {
                    Camera cam = _character.viewTransform.GetComponentInChildren<Camera>(true);
                    if (cam != null) cam.enabled = true;
                    
                    AudioListener listener = _character.viewTransform.GetComponentInChildren<AudioListener>(true);
                    if (listener != null) listener.enabled = true;
                }
            } else {
                // Disable components for remote players
                if (_inputCollector != null) _inputCollector.enabled = false;
                
                if (_character != null && _character.viewTransform != null) {
                    Camera cam = _character.viewTransform.GetComponentInChildren<Camera>(true);
                    if (cam != null) cam.enabled = false;
                    
                    AudioListener listener = _character.viewTransform.GetComponentInChildren<AudioListener>(true);
                    if (listener != null) listener.enabled = false;
                }
                
                // Also disable PlayerAiming (don't want remote inputs spinning our view)
                PlayerAiming aiming = GetComponentInChildren<PlayerAiming>();
                if (aiming != null) aiming.enabled = false;
            }
        }

        public override void OnStartNetwork() {
            base.OnStartNetwork();
            
            // Register tick callback
            if (TimeManager != null) {
                TimeManager.OnTick += TimeManager_OnTick;
            }
        }

        public override void OnStopNetwork() {
            base.OnStopNetwork();
            
            // Unregister tick callback
            if (TimeManager != null) {
                TimeManager.OnTick -= TimeManager_OnTick;
            }
        }

        private void TimeManager_OnTick() {
            if (IsOwner) {
                // Determine tick frame
                int tick = (int)TimeManager.Tick;
                
                // Get input
                InputFrame input = default;
                if (_inputCollector != null) {
                    input = _inputCollector.GatherInput(tick);
                }

                // Send to server
                SendInputServerRpc(input);

                // Simulate locally
                float dt = (float)TimeManager.TickDelta;
                _rollback.Tick(input, dt);

            } else {
                // Non-owners wait for Observe logic
                // The server receives RPC, simulates, and broadcasts ObserversRpc
            }
        }

        [ServerRpc]
        private void SendInputServerRpc(InputFrame input) {
            // Apply on Server (Skip if we are the owner, as we already simulated in OnTick)
            if (!IsOwner) {
                float dt = (float)TimeManager.TickDelta;
                _rollback.Tick(input, dt);
            }

            // Broadcast to other clients
            BroadcastInputObserversRpc(input);
        }

        [ObserversRpc(ExcludeOwner = true)]
        private void BroadcastInputObserversRpc(InputFrame input) {
            // Apply on other clients
            float dt = (float)TimeManager.TickDelta;
            _rollback.ReceiveRemoteInput(input, dt);
        }
    }
}
