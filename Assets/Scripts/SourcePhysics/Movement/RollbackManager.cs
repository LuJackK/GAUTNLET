using UnityEngine;
using FishNet.Object;
using FishNet.Managing.Timing;

namespace Fragsurf.Movement {
    
    // Stub for now. Will be expanded in later phases to handle history buffers.
    public class RollbackManager : MonoBehaviour {
        
        [SerializeField] private SurfCharacter _character;

        public void Initialize(SurfCharacter character) {
            _character = character;
        }

        // Called by owner to simulate locally immediately.
        public void ReceiveLocalInput(InputFrame input, float deltaTime) {
            if (_character != null) {
                _character.moveData = _character.SimulationTick(_character.moveData, input, deltaTime);
            }
        }

        // Called on non-owner clients when receiving inputs from the server
        public void ReceiveRemoteInput(InputFrame input, float deltaTime) {
            // Stub: In a full rollback system, this would insert input into a history buffer
            // and trigger a re-simulation from that frame forward.
            // For now, it just applies.
            if (_character != null) {
                _character.moveData = _character.SimulationTick(_character.moveData, input, deltaTime);
            }
        }
    }
}
