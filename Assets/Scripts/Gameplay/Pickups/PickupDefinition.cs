using UnityEngine;

namespace GAUNTLET.Gameplay.Pickups
{
    [CreateAssetMenu(menuName = "GAUNTLET/Pickups/Pickup Definition", fileName = "PickupDefinition")]
    public class PickupDefinition : ScriptableObject
    {
        [SerializeField] private string pickupId = "pickup";
        [SerializeField] private string displayName = "Pickup";
        [SerializeField] private PickupUpgradeKind upgradeKind = PickupUpgradeKind.Custom;
        [SerializeField] private GameObject pickupPrefab;
        [SerializeField] private PickupEffectDefinition effect;
        [SerializeField] private bool consumeWhenEffectMissing = true;

        public string PickupId => pickupId;
        public string DisplayName => displayName;
        public PickupUpgradeKind UpgradeKind => upgradeKind;
        public GameObject PickupPrefab => pickupPrefab;
        public PickupEffectDefinition Effect => effect;

        public bool TryApply(PickupEffectContext context)
        {
            if (effect != null)
                return effect.TryApply(context);

            Debug.LogWarning($"PickupDefinition '{name}' has no effect assigned. Consuming={consumeWhenEffectMissing}.", context.Pickup);
            return consumeWhenEffectMissing;
        }
    }
}
