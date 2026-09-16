using Fragsurf.Movement;
using UnityEngine;

namespace GAUNTLET.Gameplay.Pickups
{
    [CreateAssetMenu(menuName = "GAUNTLET/Pickups/Effects/Max Stamina Upgrade", fileName = "MaxStaminaUpgradeEffect")]
    public class MaxStaminaUpgradeEffectDefinition : PickupEffectDefinition
    {
        [SerializeField] private float maxStaminaIncrease = 2f;
        [SerializeField] private bool fillAddedCapacity = true;

        public override bool CanApply(PickupEffectContext context)
        {
            return base.CanApply(context) &&
                   context.Target.SurfCharacter != null &&
                   context.Target.SurfCharacter.movementConfig != null &&
                   context.Target.SurfCharacter.moveData != null;
        }

        protected override void Apply(PickupEffectContext context)
        {
            SurfCharacter character = context.Target.SurfCharacter;
            MoveData moveData = character.moveData;

            float increase = Mathf.Max(0f, maxStaminaIncrease);
            float previousMax = character.MaxStamina;
            float nextMax = previousMax + increase;

            moveData.maxStaminaBonus += increase;

            if (fillAddedCapacity)
                moveData.stamina = Mathf.Min(nextMax, moveData.stamina + increase);
            else
                moveData.stamina = Mathf.Min(moveData.stamina, nextMax);
        }
    }
}
