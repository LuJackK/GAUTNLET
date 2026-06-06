using UnityEngine;

namespace GAUNTLET.Gameplay.Pickups
{
    public abstract class PickupEffectDefinition : ScriptableObject
    {
        [SerializeField] private string effectId = "pickup_effect";
        [SerializeField] private bool consumeWhenApplied = true;

        public string EffectId => effectId;

        public virtual bool CanApply(PickupEffectContext context)
        {
            return context.Target.IsValid;
        }

        public bool TryApply(PickupEffectContext context)
        {
            if (!CanApply(context))
                return false;

            Apply(context);
            return consumeWhenApplied;
        }

        protected abstract void Apply(PickupEffectContext context);
    }
}
