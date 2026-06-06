using UnityEngine;

namespace GAUNTLET.Gameplay.Pickups
{
    public class PickupSpawnLocation : MonoBehaviour
    {
        [SerializeField] private string locationId = "pickup_location";
        [SerializeField] private Transform spawnPointOverride;
        [SerializeField] private float spawnIntervalSeconds = 20f;
        [SerializeField] private float initialSpawnDelaySeconds = 0f;
        [SerializeField] private bool randomizePoolSelection = true;
        [SerializeField] private PickupDefinition[] pickupPool;

        [Header("Gizmos")]
        [SerializeField] private bool drawGizmo = true;
        [SerializeField] private Color gizmoColor = new Color(0.3f, 0.9f, 1f, 1f);
        [SerializeField] private float gizmoRadius = 0.35f;

        public string LocationId => locationId;
        public Vector3 SpawnPosition => spawnPointOverride != null ? spawnPointOverride.position : transform.position;
        public Quaternion SpawnRotation => spawnPointOverride != null ? spawnPointOverride.rotation : transform.rotation;
        public float SpawnIntervalSeconds => Mathf.Max(0.1f, spawnIntervalSeconds);
        public float InitialSpawnDelaySeconds => Mathf.Max(0f, initialSpawnDelaySeconds);
        public bool HasPickupPool => pickupPool != null && pickupPool.Length > 0;

        public PickupDefinition ChoosePickupDefinition()
        {
            if (!HasPickupPool)
                return null;

            if (!randomizePoolSelection)
                return FirstValidPickupDefinition();

            int startIndex = Random.Range(0, pickupPool.Length);
            for (int offset = 0; offset < pickupPool.Length; offset++)
            {
                PickupDefinition candidate = pickupPool[(startIndex + offset) % pickupPool.Length];
                if (candidate != null)
                    return candidate;
            }

            return null;
        }

        private PickupDefinition FirstValidPickupDefinition()
        {
            for (int i = 0; i < pickupPool.Length; i++)
            {
                if (pickupPool[i] != null)
                    return pickupPool[i];
            }

            return null;
        }

        private void OnDrawGizmos()
        {
            if (!drawGizmo)
                return;

            Vector3 position = spawnPointOverride != null ? spawnPointOverride.position : transform.position;
            Quaternion rotation = spawnPointOverride != null ? spawnPointOverride.rotation : transform.rotation;

            Gizmos.color = gizmoColor;
            Gizmos.DrawWireSphere(position, Mathf.Max(0.05f, gizmoRadius));
            Gizmos.DrawLine(position, position + rotation * Vector3.up * 1.25f);
        }
    }
}
