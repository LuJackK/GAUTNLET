using Fragsurf.Movement;
using UnityEngine;

namespace GAUNTLET.Gameplay.Pickups
{
    [CreateAssetMenu(menuName = "GAUNTLET/Pickups/Effects/Stamina Refill", fileName = "StaminaRefillEffect")]
    public class StaminaRefillEffectDefinition : PickupEffectDefinition
    {
        [SerializeField] private bool refillToMax = true;
        [SerializeField] private float refillAmount = 1f;
        [SerializeField] private bool consumeAtFullStamina = false;
        [SerializeField] private bool clearRegenDelay = true;

        public override bool CanApply(PickupEffectContext context)
        {
            if (!base.CanApply(context))
                return false;

            SurfCharacter character = context.Target.SurfCharacter;
            if (character == null || character.movementConfig == null || character.moveData == null)
                return false;

            if (consumeAtFullStamina)
                return true;

            float maxStamina = character.MaxStamina;
            return character.moveData.stamina < maxStamina;
        }

        protected override void Apply(PickupEffectContext context)
        {
            SurfCharacter character = context.Target.SurfCharacter;
            MoveData moveData = character.moveData;
            float maxStamina = character.MaxStamina;

            if (refillToMax)
                moveData.stamina = maxStamina;
            else
                moveData.stamina = Mathf.Min(maxStamina, moveData.stamina + Mathf.Max(0f, refillAmount));

            if (clearRegenDelay)
                moveData.staminaRegenTimer = 0f;
        }
    }
}
