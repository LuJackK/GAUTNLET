using UnityEngine;

namespace GAUNTLET.Gameplay.Pickups
{
    [CreateAssetMenu(menuName = "GAUNTLET/Pickups/Effects/Health Pickup", fileName = "HealthPickupEffect")]
    public class HealthPickupEffectDefinition : PickupEffectDefinition
    {
        [SerializeField] private int healAmount = 1;
        [SerializeField] private bool consumeAtFullHealth = false;

        public override bool CanApply(PickupEffectContext context)
        {
            if (!base.CanApply(context))
                return false;

            if (context.Target.NetworkedCharacter == null)
                return false;

            if (consumeAtFullHealth)
                return true;

            return context.Target.NetworkedCharacter.CurrentHealth < context.Target.NetworkedCharacter.MaxHealth;
        }

        protected override void Apply(PickupEffectContext context)
        {
            context.Target.NetworkedCharacter.TryRestoreHealth(
                Mathf.Max(1, healAmount),
                $"pickup:{context.Definition.PickupId}");
        }
    }
}
