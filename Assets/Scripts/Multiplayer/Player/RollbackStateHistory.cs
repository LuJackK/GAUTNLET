namespace Fragsurf.Movement {

    internal sealed class RollbackStateHistory {
        private const int BufferSize = 256;

        private readonly MoveData[] _states = new MoveData[BufferSize];
        private readonly int[] _ticks = new int[BufferSize];

        public RollbackStateHistory() {
            Clear();
        }

        public void Clear() {
            for (int i = 0; i < BufferSize; i++) {
                _states[i] = null;
                _ticks[i] = InputFrame.InvalidFrame;
            }
        }

        public void Record(int tick, MoveData state) {
            if (state == null || tick == InputFrame.InvalidFrame)
                return;

            int slot = FrameToSlot(tick);
            _states[slot] = state.Clone();
            _ticks[slot] = tick;
        }

        public bool TryGet(int tick, out MoveData state) {
            int slot = FrameToSlot(tick);
            if (_ticks[slot] == tick && _states[slot] != null) {
                state = _states[slot].Clone();
                return true;
            }

            state = null;
            return false;
        }

        private static int FrameToSlot(int frame) {
            int slot = frame % BufferSize;
            return (slot < 0) ? slot + BufferSize : slot;
        }
    }
}
