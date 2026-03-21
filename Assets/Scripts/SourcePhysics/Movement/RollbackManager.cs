using UnityEngine;
using FishNet.Object;
using FishNet.Managing.Timing;

namespace Fragsurf.Movement {
    
    public class RollbackManager : MonoBehaviour {
        
        private const int BUFFER_SIZE = 128;

        private MoveData[]   _stateBuffer    = new MoveData[BUFFER_SIZE];
        private InputFrame[] _localInputs    = new InputFrame[BUFFER_SIZE];
        private InputFrame[] _remoteInputs   = new InputFrame[BUFFER_SIZE];
        private bool[]       _remoteConfirmed= new bool[BUFFER_SIZE];

        [SerializeField] private SurfCharacter _character;
        private int _currentTick = 0;

        public void Initialize(SurfCharacter character) {
            _character = character;
            // Initialize buffers to avoid nulls
            for (int i = 0; i < BUFFER_SIZE; i++) {
                _stateBuffer[i] = new MoveData();
            }
        }

        // Called by owner to store their input
        public void ReceiveLocalInput(InputFrame input) {
            _localInputs[input.frame % BUFFER_SIZE] = input;
        }

        // Called on non-owner clients when receiving inputs from the server, 
        // OR called on the server when receiving inputs from clients.
        public void ReceiveRemoteInput(InputFrame input, float deltaTime) {
            int f = input.frame;
            _remoteInputs[f % BUFFER_SIZE] = input;
            _remoteConfirmed[f % BUFFER_SIZE] = true;

            // Check if we already simulated this frame with a prediction
            if (f < _currentTick) {
                // In a simple system, we just check if the actual input matches what was used (prediction)
                // For now, we'll force a rollback if the frame is in the past.
                // Improvement: Only rollback if misprediction detected.
                InputFrame predicted = GetPrediction(f);
                if (predicted.buttons != input.buttons || 
                    Mathf.Abs(predicted.stickX - input.stickX) > 1 || 
                    Mathf.Abs(predicted.stickY - input.stickY) > 1) {
                    Rollback(f, deltaTime);
                }
            }
        }

        public void Tick(InputFrame input, float deltaTime) {
            int f = input.frame;
            _currentTick = f;

            // Save state before simulating
            _stateBuffer[f % BUFFER_SIZE] = _character.moveData.Clone();

            // Store local input
            _localInputs[f % BUFFER_SIZE] = input;

            // Generate remote input if this wasn't the owner? 
            // In our current setup, NetworkedCharacter calls this differently for local/remote.
            // Let's refine this to be a generic "Advance Simulation" call.
            _character.moveData = _character.SimulationTick(_character.moveData, input, deltaTime);
        }

        private void Rollback(int toFrame, float deltaTime) {
            // Restore state
            _character.LoadState(_stateBuffer[toFrame % BUFFER_SIZE]);

            // Re-simulate from the corrected frame up to the present
            for (int f = toFrame; f <= _currentTick; f++) {
                // For owner re-sim: use stored local inputs
                // For proxy re-sim: use remote inputs
                
                // For now, let's assume this manager handles the character it's on.
                // If this is a proxy, we use _remoteInputs. If this is owner, we use _localInputs.
                // We'll use a helper to get the "active" input for this frame.
                InputFrame input = GetActiveInput(f);
                _character.moveData = _character.SimulationTick(_character.moveData, input, deltaTime);
            }
        }

        private InputFrame GetActiveInput(int frame) {
            // If we have a confirmed remote input, use it. Otherwise use local.
            if (_remoteConfirmed[frame % BUFFER_SIZE]) {
                return _remoteInputs[frame % BUFFER_SIZE];
            }
            return _localInputs[frame % BUFFER_SIZE];
        }

        private InputFrame GetPrediction(int frame) {
            // Repeat last confirmed remote input
            for (int i = frame - 1; i >= Mathf.Max(0, frame - 8); i--) {
                if (_remoteConfirmed[i % BUFFER_SIZE])
                    return _remoteInputs[i % BUFFER_SIZE];
            }
            return default;
        }
    }
}
