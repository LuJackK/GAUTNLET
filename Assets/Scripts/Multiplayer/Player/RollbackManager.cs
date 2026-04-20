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

        public void Initialize(SurfCharacter character) {
            _character = character;
            _predictedStateHistory = new RollbackStateHistory();
            _replayEngine = (_character != null) ? new RollbackReplayEngine(_character) : null;
            _currentTick = (_character != null && _character.moveData != null)
                ? _character.moveData.frame
                : InputFrame.InvalidFrame;
            RollbackCount = 0;
            LastCorrectedTick = InputFrame.InvalidFrame;

            if (_character != null && _character.moveData != null && _character.moveData.frame != InputFrame.InvalidFrame)
                _predictedStateHistory.Record(_character.moveData.frame, _character.moveData);
        }

        public void SimulatePredictedTick(InputFrame input, float deltaTime) {
            if (!CanSimulate() || !input.IsValid)
                return;

            _character.moveData = _character.SimulationTick(_character.moveData, input, deltaTime);
            _currentTick = input.frame;
            _predictedStateHistory.Record(input.frame, _character.moveData);
        }

        public void ApplyAuthoritativeCorrection(MoveData correctedState,
                                                 float deltaTime,
                                                 Func<int, InputFrame> replayInputProvider) {
            if (!CanSimulate() || correctedState == null)
                return;

            int correctedTick = correctedState.frame;
            int replayToTick = _currentTick;

            LastCorrectedTick = correctedTick;
            _replayEngine.LoadCorrectedState(correctedState);
            _predictedStateHistory.Record(correctedTick, _character.moveData);

            if (replayToTick <= correctedTick) {
                _currentTick = correctedTick;
                return;
            }

            RollbackCount++;
            _replayEngine.ReplayRange(correctedTick + 1,
                                      replayToTick,
                                      deltaTime,
                                      replayInputProvider,
                                      RecordPredictedState);
            _currentTick = replayToTick;
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
