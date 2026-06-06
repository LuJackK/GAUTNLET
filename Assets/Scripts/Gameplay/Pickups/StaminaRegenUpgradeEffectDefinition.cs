using Fragsurf.Movement;
using UnityEngine;

namespace GAUNTLET.Gameplay.Pickups
{
    [CreateAssetMenu(menuName = "GAUNTLET/Pickups/Effects/Stamina Regen Upgrade", fileName = "StaminaRegenUpgradeEffect")]
    public class StaminaRegenUpgradeEffectDefinition : PickupEffectDefinition
    {
        [SerializeField] private float regenRateIncrease = 1f;
        [SerializeField] private float maxRegenRate = 0f;
        [SerializeField] private bool consumeWhenAtCap = false;

        public override bool CanApply(PickupEffectContext context)
        {
            if (!base.CanApply(context))
                return false;

            SurfCharacter character = context.Target.SurfCharacter;
            if (character == null || character.movementConfig == null)
                return false;

            if (maxRegenRate <= 0f || consumeWhenAtCap)
                return true;

            return character.movementConfig.staminaRegenRate < maxRegenRate;
        }

        protected override void Apply(PickupEffectContext context)
        {
            MovementConfig config = context.Target.SurfCharacter.movementConfig;
            float nextRate = config.staminaRegenRate + Mathf.Max(0f, regenRateIncrease);

            if (maxRegenRate > 0f)
                nextRate = Mathf.Min(nextRate, maxRegenRate);

            config.staminaRegenRate = nextRate;
        }
    }
}
