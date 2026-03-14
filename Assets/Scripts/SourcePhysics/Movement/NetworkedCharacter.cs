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
                _rollback.ReceiveLocalInput(input, dt);

            } else {
                // Non-owners wait for Observe logic
                // The server receives RPC, simulates, and broadcasts ObserversRpc
            }
        }

        [ServerRpc]
        private void SendInputServerRpc(InputFrame input) {
            // Apply on Server
            float dt = (float)TimeManager.TickDelta;
            _rollback.ReceiveLocalInput(input, dt);

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
