using System;
using UnityEngine;

namespace Fragsurf.Movement {

    internal sealed class RollbackReplayEngine {
        private readonly SurfCharacter _character;

        public RollbackReplayEngine(SurfCharacter character) {
            _character = character;
        }

        public void LoadCorrectedState(MoveData correctedState) {
            if (_character == null || correctedState == null)
                return;

            _character.LoadState(correctedState);
        }

        // Inputs have already been materialized and validated by RollbackManager.
        public void Replay(InputFrame[] inputs, float deltaTime, Action<int, MoveData> recordPredictedState) {
            foreach (InputFrame input in inputs) {
                _character.moveData = _character.SimulationTick(_character.moveData, input, deltaTime, false);
                recordPredictedState?.Invoke(input.frame, _character.moveData);
            }
        }
    }
}
