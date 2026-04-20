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

        public void ReplayRange(int startTick,
                                int endTick,
                                float deltaTime,
                                Func<int, InputFrame> replayInputProvider,
                                Action<int, MoveData> recordPredictedState) {
            if (_character == null || startTick > endTick)
                return;

            for (int tick = startTick; tick <= endTick; tick++) {
                if (replayInputProvider == null) {
                    Debug.LogError($"[RollbackReplayEngine] Missing replay input provider for tick {tick}. Replay aborted.", _character);
                    return;
                }

                InputFrame input = replayInputProvider(tick);
                if (!input.IsValid) {
                    Debug.LogError($"[RollbackReplayEngine] Missing canonical replay input for tick {tick}. Replay aborted.", _character);
                    return;
                }

                _character.moveData = _character.SimulationTick(_character.moveData, input, deltaTime, false);
                recordPredictedState?.Invoke(tick, _character.moveData);
            }
        }
    }
}
