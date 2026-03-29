using UnityEngine;

namespace GAUNTLET.Networking
{
    /// <summary>
    /// Marker for valid player spawn locations.
    /// </summary>
    public class SpawnPoint : MonoBehaviour
    {
        [SerializeField] private bool drawGizmo = true;
        [SerializeField] private Color gizmoColor = Color.green;

        private void OnDrawGizmos()
        {
            if (!drawGizmo)
                return;

            Gizmos.color = gizmoColor;
            Gizmos.DrawSphere(transform.position, 0.25f);
            Gizmos.DrawLine(transform.position, transform.position + transform.forward * 1.25f);
        }
    }
}
