namespace Fragsurf.Movement {

    internal sealed class CorrectionGateDiagnosticsSnapshot {
        public int ReplicateTicksProcessed;
        public int ReconcilePacketsReceived;
        public int SpectatorPresentationApplications;
        public int IgnoredCount;
        public int ObserveOnlyCount;
        public int HardCorrectCount;
        public int ForceCorrectCount;
        public int MissingPredictedStateCount;
        public int ConsecutiveObserveCount;
        public int LastFrame = InputFrame.InvalidFrame;
        public int LastReplicateFrame = InputFrame.InvalidFrame;
        public int LastSpectatorPresentationFrame = InputFrame.InvalidFrame;
        public CorrectionDecision LastDecision = CorrectionDecision.Ignore;
        public float LastWeightedScore;
        public string LastPrimaryReason = string.Empty;
        public string LastSummary = string.Empty;
    }
}
