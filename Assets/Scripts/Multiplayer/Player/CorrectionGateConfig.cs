namespace Fragsurf.Movement {

    internal sealed class CorrectionGateConfig {
        public float PositionHardThreshold = 0.25f;
        public float VelocityHardThreshold = 0.40f;
        public float ForeignMeleeChargeHorizontalPositionHardThreshold = 0.35f;
        public float ForeignMeleeChargeVerticalPositionHardThreshold = 1.25f;
        public float ForeignMeleeChargeHorizontalVelocityHardThreshold = 1.25f;
        public float ForeignMeleeChargeVerticalVelocityHardThreshold = 10f;
        public float YawHardThreshold = 1.5f;
        public float PitchHardThreshold = 1.5f;
        public float FallingVelocityHardThreshold = 0.60f;
        public float ForeignMeleeChargeFallingVelocityHardThreshold = 10f;
        public float SlideDirectionHardThreshold = 5f;
        public float SlideSpeedHardThreshold = 0.20f;

        public float ScalarTolerance = 0.05f;
        public float TimerTolerance = 0.05f;
        public float ForeignMeleeChargeTimerTolerance = 0.12f;
        public float FactorTolerance = 0.04f;
        public float LerpTolerance = 0.08f;
        public float InputEchoTolerance = 0.15f;
        public float ObserveWeightedScoreThreshold = 4f;
        public float HardWeightedScoreThreshold = 10f;
        public int ConsecutiveObserveFramesBeforeHardCorrect = 3;
        public int ForeignMeleeChargeConsecutiveObserveFramesBeforeHardCorrect = 8;

        public float StaminaWeight = 2f;
        public float TimerWeight = 1.5f;
        public float FactorWeight = 1.25f;
        public float CrouchStateWeight = 2f;
        public float CrouchLerpWeight = 1.25f;
        public float InputEchoWeight = 0.1f;

        public static CorrectionGateConfig CreateConservativeDefaults() {
            return new CorrectionGateConfig();
        }
    }
}
