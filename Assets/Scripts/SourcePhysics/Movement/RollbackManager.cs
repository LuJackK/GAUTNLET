using UnityEngine;

namespace Fragsurf.Movement {
    
    public class RollbackManager : MonoBehaviour {
        
        private const int BUFFER_SIZE = 256;

        private MoveData[]   _stateBuffer    = new MoveData[BUFFER_SIZE];
        private InputFrame[] _localInputs    = new InputFrame[BUFFER_SIZE];
        private InputFrame[] _remoteInputs   = new InputFrame[BUFFER_SIZE];
        private bool[]       _remoteConfirmed= new bool[BUFFER_SIZE];
        private int[]        _localInputFrameAtSlot = new int[BUFFER_SIZE];
        private int[]        _remoteInputFrameAtSlot = new int[BUFFER_SIZE];
        private int[]        _stateFrameAtSlot = new int[BUFFER_SIZE];

        [SerializeField] private SurfCharacter _character;
        private int _currentTick = 0;
        public int CurrentTick => _currentTick;

        [SerializeField] private int _maxPredictionLead = 120;
        [SerializeField, Min(1)] private int _predictionLookbackFrames = 120;
        [SerializeField, Min(0)] private int _stickMismatchThreshold = 1;

        public int RollbackCount { get; private set; }
        public int CorrectionCount { get; private set; }
        public int PredictedFillCount { get; private set; }
        public int MaxRemoteFrameLead { get; private set; }
        public int SlotMismatchFallbackCount { get; private set; }
        public int PredictionCapViolations { get; private set; }
        private bool _hasSimulationBaseline;

        public void Initialize(SurfCharacter character) {
            _character = character;
            _hasSimulationBaseline = false;
            // Initialize buffers to avoid nulls
            for (int i = 0; i < BUFFER_SIZE; i++) {
                _stateBuffer[i] = new MoveData();
                _localInputFrameAtSlot[i] = -1;
                _remoteInputFrameAtSlot[i] = -1;
                _stateFrameAtSlot[i] = -1;
                _remoteConfirmed[i] = false;
            }
        }

        // Called by owner to store their input
        public void ReceiveLocalInput(InputFrame input) {
            int slot = input.frame % BUFFER_SIZE;
            _localInputs[slot] = input;
            _localInputFrameAtSlot[slot] = input.frame;
        }

        // Called on non-owner clients when receiving inputs from the server, 
        // OR called on the server when receiving inputs from clients.
        public void ReceiveRemoteInput(InputFrame input, float deltaTime) {
            if (!CanSimulate())
                return;

            int f = input.frame;
            int slot = f % BUFFER_SIZE;

            if (!_hasSimulationBaseline) {
                // Spawn can happen after global tick has advanced significantly.
                // Rebase once so first remote frames are not treated as extreme lead.
                _currentTick = Mathf.Max(0, f - 1);
                _hasSimulationBaseline = true;
            }

            int frameLead = f - _currentTick;
            if (frameLead > MaxRemoteFrameLead)
                MaxRemoteFrameLead = frameLead;

            _remoteInputs[slot] = input;
            _remoteInputFrameAtSlot[slot] = f;
            _remoteConfirmed[slot] = true;

            // Check if we already simulated this frame with a prediction
            if (f < _currentTick) {
                // In a simple system, we just check if the actual input matches what was used (prediction)
                // For now, we'll force a rollback if the frame is in the past.
                InputFrame predicted = GetPrediction(f);
                if (predicted.buttons != input.buttons || 
                    Mathf.Abs(predicted.stickX - input.stickX) > _stickMismatchThreshold || 
                    Mathf.Abs(predicted.stickY - input.stickY) > _stickMismatchThreshold ||
                    predicted.lookYaw100 != input.lookYaw100 ||
                    predicted.lookPitch100 != input.lookPitch100) {
                    CorrectionCount++;
                    RollbackCount++;
                    Rollback(f, deltaTime);
                }
            } else if (f > _currentTick) {
                // Advance simulation to the new frame.
                // If there are missed frames between _currentTick and f, we'll fill them with predictions.
                int lead = f - _currentTick;
                if (lead > _maxPredictionLead) {
                    PredictionCapViolations++;
                    int boundedStart = Mathf.Max(0, f - _maxPredictionLead);
                    _currentTick = Mathf.Max(0, boundedStart - 1);
                }

                int fills = Mathf.Max(0, (f - _currentTick) - 1);
                PredictedFillCount += fills;
                for (int i = _currentTick + 1; i <= f; i++) {
                    InputFrame frameInput = (i == f) ? input : GetPrediction(i);
                    Tick(frameInput, deltaTime);
                }
            }

        }

        public void Tick(InputFrame input, float deltaTime) {
            if (!CanSimulate())
                return;

            int f = input.frame;
            int slot = f % BUFFER_SIZE;
            _currentTick = f;
            _hasSimulationBaseline = true;

            // Save state before simulating
            _stateBuffer[slot] = _character.moveData.Clone();
            _stateFrameAtSlot[slot] = f;

            // Store local input
            _localInputs[slot] = input;
            _localInputFrameAtSlot[slot] = f;

            // Generate remote input if this wasn't the owner? 
            // In our current setup, NetworkedCharacter calls this differently for local/remote.
            // Let's refine this to be a generic "Advance Simulation" call.
            _character.moveData = _character.SimulationTick(_character.moveData, input, deltaTime);
        }

        private void Rollback(int toFrame, float deltaTime) {
            if (!CanSimulate())
                return;

            // Restore state to the target frame
            int stateSlot = toFrame % BUFFER_SIZE;
            if (_stateFrameAtSlot[stateSlot] == toFrame) {
                _character.LoadState(_stateBuffer[stateSlot]);
            } else {
                // If exact historical state is unavailable, log it but continue
                // Don't reset to zero - try to preserve current velocity for smoother recovery
                SlotMismatchFallbackCount++;
                // Keep current state but reset frame counter
                if (_character.moveData != null) {
                    _character.moveData.frame = toFrame;
                }
            }

            // Re-simulate from the corrected frame up to the present
            for (int f = toFrame; f <= _currentTick; f++) {
                InputFrame input = GetActiveInput(f);
                _character.moveData = _character.SimulationTick(_character.moveData, input, deltaTime, false);
            }
        }

        private InputFrame GetActiveInput(int frame) {
            int slot = frame % BUFFER_SIZE;

            // If we have a confirmed remote input, use it. Otherwise use local.
            if (_remoteConfirmed[slot] && _remoteInputFrameAtSlot[slot] == frame) {
                return _remoteInputs[slot];
            }

            if (_localInputFrameAtSlot[slot] == frame)
                return _localInputs[slot];

            SlotMismatchFallbackCount++;
            return GetPrediction(frame);
        }

        private InputFrame GetPrediction(int frame) {
            // Repeat last confirmed remote input, preserving held button state
            // but NOT repeating justPressed flags (which are edge-triggered)
            int lookback = Mathf.Max(1, _predictionLookbackFrames);
            for (int i = frame - 1; i >= Mathf.Max(0, frame - lookback); i--) {
                int slot = i % BUFFER_SIZE;
                if (_remoteConfirmed[slot] && _remoteInputFrameAtSlot[slot] == i) {
                    InputFrame predicted = _remoteInputs[slot];
                    predicted.frame = frame;
                    // IMPORTANT: Clear justPressed to prevent edge-triggered actions from repeating
                    // This prevents melee from re-triggering, dash from re-triggering, etc.
                    // The held buttons (buttons field) are preserved for melee charging, sliding, etc.
                    predicted.justPressed = 0;
                    return predicted;
                }
            }

            // Fallback: repeat the most recent input we know about (held buttons only)
            // This ensures stick and button hold state is maintained even without confirmed input
            int recentSlot = (frame - 1) % BUFFER_SIZE;
            if (_localInputFrameAtSlot[recentSlot] == frame - 1) {
                InputFrame fallback = _localInputs[recentSlot];
                fallback.frame = frame;
                fallback.justPressed = 0; // Never repeat edge flags in prediction
                return fallback;
            }

            // Final fallback: completely empty input but keep frame number monotonic
            InputFrame empty = default;
            empty.frame = frame;
            return empty;
        }

        /// <summary>
        /// Reconciles owner prediction to an authoritative state at a specific frame,
        /// then replays stored local inputs up to current frame.
        /// </summary>
        public void ReconcileAuthoritativeFrame(int frame, Vector3 position, Vector3 velocity, float yaw, float pitch, float deltaTime) {
            if (!CanSimulate())
                return;

            MoveData authoritative = _character.moveData != null ? _character.moveData.Clone() : new MoveData();
            authoritative.frame = frame;
            authoritative.origin = position;
            authoritative.velocity = velocity;
            authoritative.viewAngles.y = yaw;
            authoritative.viewAngles.x = pitch;

            // Future-or-current authoritative update.
            if (frame >= _currentTick) {
                _character.LoadState(authoritative);
                _currentTick = frame;
                _hasSimulationBaseline = true;
                int slotNow = frame % BUFFER_SIZE;
                _stateBuffer[slotNow] = authoritative.Clone();
                _stateFrameAtSlot[slotNow] = frame;
                return;
            }

            // Past frame: rewind and replay local timeline.
            _character.LoadState(authoritative);
            int replayTo = _currentTick;

            for (int f = frame + 1; f <= replayTo; f++) {
                int slot = f % BUFFER_SIZE;

                InputFrame input;
                if (_localInputFrameAtSlot[slot] == f) {
                    input = _localInputs[slot];
                } else {
                    input = default;
                    input.frame = f;
                }

                _stateBuffer[slot] = _character.moveData.Clone();
                _stateFrameAtSlot[slot] = f;

                _character.moveData = _character.SimulationTick(_character.moveData, input, deltaTime, false);
            }
        }

        private bool CanSimulate() {
            if (_character == null || _character.moveData == null || !_character.IsSimulationReady) {
                return false;
            }
            return true;
        }
    }
}
