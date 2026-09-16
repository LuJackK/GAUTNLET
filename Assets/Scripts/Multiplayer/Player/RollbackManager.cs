using System;
using UnityEngine;

namespace Fragsurf.Movement {

    public class RollbackManager : MonoBehaviour {
        [SerializeField] private SurfCharacter _character;

        private RollbackStateHistory _predictedStateHistory;
        private RollbackReplayEngine _replayEngine;
        private int _currentTick = InputFrame.InvalidFrame;

        public int CurrentTick => _currentTick;
        public int RollbackCount { get; private set; }
        public int LastCorrectedTick { get; private set; } = InputFrame.InvalidFrame;
        public string LastReplayError { get; private set; } = string.Empty;

        public void Initialize(SurfCharacter character, bool recordInitialState = true) {
            _character = character;
            _predictedStateHistory = new RollbackStateHistory();
            _replayEngine = (_character != null) ? new RollbackReplayEngine(_character) : null;
            _currentTick = (recordInitialState && _character != null && _character.moveData != null)
                ? _character.moveData.frame
                : InputFrame.InvalidFrame;
            RollbackCount = 0;
            LastCorrectedTick = InputFrame.InvalidFrame;
            LastReplayError = string.Empty;

            if (recordInitialState && _character != null && _character.moveData != null && _character.moveData.frame != InputFrame.InvalidFrame)
                _predictedStateHistory.Record(_character.moveData.frame, _character.moveData);
        }

        public void SimulatePredictedTick(InputFrame input, float deltaTime, bool allowGameplaySideEffects = true) {
            if (!CanSimulate() || !input.IsValid || !_character.IsSimulationReady)
                return;

            _character.moveData = _character.SimulationTick(_character.moveData, input, deltaTime, allowGameplaySideEffects);
            _currentTick = input.frame;
            _predictedStateHistory.Record(input.frame, _character.moveData);
        }

        // FishNet owns replay in a connected session. Its reconcile callback only
        // restores the baseline; subsequent Replicate(Replayed) calls advance it.
        public void RestoreAuthoritativeState(MoveData correctedState) {
            if (!CanSimulate() || correctedState == null || correctedState.frame < 0)
                return;

            if (_currentTick > correctedState.frame)
                RollbackCount++;
            _replayEngine.LoadCorrectedState(correctedState);
            _currentTick = correctedState.frame;
            LastCorrectedTick = correctedState.frame;
            _predictedStateHistory.Record(_currentTick, _character.moveData);
        }

        // Standalone replay/harness entry point. Never call from a FishNet reconcile.
        // Preflight before loading or recording anything: bad input leaves the
        // previous valid timeline intact and reports failure to the caller.
        public bool ApplyAuthoritativeCorrection(MoveData correctedState,
                                                 float deltaTime,
                                                 Func<int, InputFrame> replayInputProvider,
                                                 int? expectedCharacterObjectId = null) {
            LastReplayError = string.Empty;
            if (!CanSimulate() || !_character.IsSimulationReady || correctedState == null || correctedState.frame < 0)
                return FailReplay("A valid character and corrected snapshot are required.");
            if (deltaTime <= 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
                return FailReplay("Replay requires a finite positive timestep.");

            int correctedTick = correctedState.frame;
            int replayToTick = _currentTick;
            long count = System.Math.Max(0L, (long)replayToTick - correctedTick);
            if (count >= RollbackStateHistory.BufferSize)
                return FailReplay("Requested replay exceeds retained state history.");
            InputFrame[] inputs = new InputFrame[(int)count];
            int? entityId = expectedCharacterObjectId;
            if (!entityId.HasValue && _character.moveData.lastInput.IsValid)
                entityId = _character.moveData.lastInput.characterObjectId;
            for (int i = 0; i < inputs.Length; i++) {
                int tick = correctedTick + 1 + i;
                if (replayInputProvider == null)
                    return FailReplay($"Missing replay input provider for tick {tick}.");
                InputFrame input = replayInputProvider(tick);
                if (!input.IsValid || input.frame != tick)
                    return FailReplay($"Missing or wrong-tick replay input for tick {tick}.");
                if (!entityId.HasValue)
                    entityId = input.characterObjectId;
                if (input.characterObjectId != entityId)
                    return FailReplay($"Wrong character in replay input for tick {tick}.");
                inputs[i] = input;
            }

            RestoreAuthoritativeState(correctedState);
            _replayEngine.Replay(inputs, deltaTime, RecordPredictedState);
            _currentTick = _character.moveData.frame;
            return true;
        }

        private bool FailReplay(string reason) {
            LastReplayError = reason;
            return false;
        }

        public bool TryGetPredictedState(int tick, out MoveData state) {
            if (_predictedStateHistory == null) {
                state = null;
                return false;
            }

            return _predictedStateHistory.TryGet(tick, out state);
        }

        private bool CanSimulate() {
            return _character != null && _replayEngine != null;
        }

        private void RecordPredictedState(int tick, MoveData state) {
            if (state == null || _predictedStateHistory == null)
                return;

            _predictedStateHistory.Record(tick, state);
        }
    }
}
