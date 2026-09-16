using System;
using System.Collections.Generic;
using Fragsurf.Movement;

namespace GAUNTLET.Rollback {
    // One owner of replay for the complete interaction set. The simulation
    // delegate operates on a private candidate; Commit is the integration boundary.
    public sealed class WorldRollbackHistory {
        public const int Capacity = 256;
        private readonly SortedDictionary<int, WorldSnapshot> _states = new SortedDictionary<int, WorldSnapshot>();
        private readonly SortedDictionary<int, InputFrame[]> _inputs = new SortedDictionary<int, InputFrame[]>();
        private readonly Action<WorldSnapshot, InputFrame[]> _simulate;
        private WorldSnapshot _current;
        public int CurrentTick => _current.Tick;
        public string LastError { get; private set; } = string.Empty;

        public WorldRollbackHistory(WorldSnapshot initial, Action<WorldSnapshot, InputFrame[]> simulate) {
            if (initial == null) throw new ArgumentNullException(nameof(initial));
            _simulate = simulate ?? throw new ArgumentNullException(nameof(simulate));
            _current = initial.Clone();
            _states.Add(_current.Tick, _current.Clone());
        }

        public WorldSnapshot Capture() => _current.Clone();

        public bool TryGet(int tick, out WorldSnapshot snapshot) {
            snapshot = _states.TryGetValue(tick, out WorldSnapshot saved) ? saved.Clone() : null;
            return snapshot != null;
        }

        public bool Advance(InputFrame[] inputs) {
            LastError = string.Empty;
            int next = checked(_current.Tick + 1);
            if (!ValidateInputs(_current, next, inputs)) return false;
            var candidate = _current.Clone();
            var ownedInputs = (InputFrame[])inputs.Clone();
            _simulate(candidate, ownedInputs);
            if (candidate.Tick != next)
                throw new InvalidOperationException("World simulation did not advance exactly one tick.");
            _inputs[next] = ownedInputs;
            _current = candidate;
            _states[next] = candidate.Clone();
            Trim();
            return true;
        }

        public bool Correct(WorldSnapshot baseline, Func<int, InputFrame[]> inputProvider = null) {
            LastError = string.Empty;
            if (baseline == null || baseline.Tick < -1 || baseline.Tick > _current.Tick)
                return Fail("Correction must be a valid retained or earlier baseline, not a future tick.");
            long count = (long)_current.Tick - baseline.Tick;
            if (count >= Capacity) return Fail("Correction exceeds the world history window.");
            var replay = new List<InputFrame[]>((int)count);
            for (int tick = baseline.Tick + 1; tick <= _current.Tick; tick++) {
                InputFrame[] inputs = inputProvider != null ? inputProvider(tick)
                    : _inputs.TryGetValue(tick, out InputFrame[] saved) ? saved : null;
                if (!ValidateInputs(baseline, tick, inputs)) return false;
                replay.Add((InputFrame[])inputs.Clone());
            }
            // No retained timeline is overwritten until the complete pass succeeds.
            var candidate = baseline.Clone();
            var replacementStates = new List<WorldSnapshot>(replay.Count + 1) { candidate.Clone() };
            foreach (InputFrame[] inputs in replay) {
                int next = candidate.Tick + 1;
                _simulate(candidate, inputs);
                if (candidate.Tick != next)
                    throw new InvalidOperationException("World replay did not advance exactly one tick.");
                replacementStates.Add(candidate.Clone());
            }
            foreach (WorldSnapshot state in replacementStates) _states[state.Tick] = state;
            for (int i = 0; i < replay.Count; i++) _inputs[baseline.Tick + 1 + i] = replay[i];
            _current = candidate;
            Trim();
            return true;
        }

        private bool ValidateInputs(WorldSnapshot world, int tick, InputFrame[] inputs) {
            if (inputs == null || inputs.Length != world.Characters.Length)
                return Fail($"Tick {tick} needs one input for every world character.");
            var ids = new HashSet<int>();
            foreach (WorldCharacterState character in world.Characters) ids.Add(character.Id);
            if (ids.Count != world.Characters.Length) return Fail("World contains duplicate character IDs.");
            foreach (InputFrame input in inputs)
                if (!input.IsValid || input.frame != tick || !ids.Remove(input.characterObjectId))
                    return Fail($"Tick {tick} has a missing, duplicate or wrong-identity input.");
            return true;
        }

        private bool Fail(string error) { LastError = error; return false; }

        private void Trim() {
            int oldest = _current.Tick - Capacity + 1;
            var remove = new List<int>();
            foreach (int tick in _states.Keys) if (tick < oldest) remove.Add(tick);
            foreach (int tick in remove) _states.Remove(tick);
            remove.Clear();
            foreach (int tick in _inputs.Keys) if (tick <= oldest) remove.Add(tick);
            foreach (int tick in remove) _inputs.Remove(tick);
        }
    }
}
