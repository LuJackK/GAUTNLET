using System.Collections.Generic;

namespace Fragsurf.Movement {

    internal sealed class StateDivergenceReport {
        public bool HasPredictedState;
        public CorrectionDecision Decision;
        public float WeightedScore;
        public bool HasFatalMismatch;
        public bool HasKinematicMismatch;
        public int ConsecutiveObserveFrames;
        public string PrimaryReason = string.Empty;
        public string Summary = string.Empty;
        public readonly List<string> TopMismatches = new List<string>(6);
    }
}
