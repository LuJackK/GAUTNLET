using Fragsurf.Movement;
using UnityEngine;

namespace GAUNTLET.Gameplay.Pickups
{
    public readonly struct PickupEffectTarget
    {
        public PickupEffectTarget(GameObject gameObject, SurfCharacter surfCharacter, NetworkedCharacter networkedCharacter)
        {
            GameObject = gameObject;
            SurfCharacter = surfCharacter;
            NetworkedCharacter = networkedCharacter;
        }

        public GameObject GameObject { get; }
        public SurfCharacter SurfCharacter { get; }
        public NetworkedCharacter NetworkedCharacter { get; }
        public Transform Transform => GameObject != null ? GameObject.transform : null;
        public bool IsValid => GameObject != null && (SurfCharacter != null || NetworkedCharacter != null);

        public static bool TryResolve(Collider collider, out PickupEffectTarget target)
        {
            target = default;
            if (collider == null)
                return false;

            NetworkedCharacter networkedCharacter = collider.GetComponentInParent<NetworkedCharacter>();
            SurfCharacter surfCharacter = collider.GetComponentInParent<SurfCharacter>();

            if (surfCharacter == null && networkedCharacter != null)
                surfCharacter = networkedCharacter.GetComponent<SurfCharacter>();

            GameObject targetObject = null;
            if (networkedCharacter != null)
                targetObject = networkedCharacter.gameObject;
            else if (surfCharacter != null)
                targetObject = surfCharacter.gameObject;
            else if (collider.attachedRigidbody != null)
                targetObject = collider.attachedRigidbody.gameObject;

            target = new PickupEffectTarget(targetObject, surfCharacter, networkedCharacter);
            return target.IsValid;
        }
    }
}
