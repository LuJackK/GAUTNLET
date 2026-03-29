using UnityEngine;
using System.Collections.Generic;
using Fragsurf.Movement;

namespace Fragsurf.Combat {

    public class Hitbox : MonoBehaviour {

        [Header("Definition Source")]
        [Tooltip("If true, uses this component's Definition values. If false, allows SurfCharacter to overwrite from MovementConfig.")]
        [SerializeField] private bool useComponentDefinition = true;
        [Tooltip("If true, uses this component's Target Layer. If false, allows SurfCharacter to overwrite from MovementConfig/enemyLayerMask.")]
        [SerializeField] private bool useComponentTargetLayer = true;

        public HitboxDefinition definition;
        public LayerMask targetLayer;
        public bool isActive = false;

        private List<Hurtbox> _hitObjects = new List<Hurtbox>();

        public void Activate() {
            isActive = true;
            _hitObjects.Clear();
            Debug.Log($"[Hitbox] {gameObject.name} ACTIVATED. Target Layer: {targetLayer.value}");
        }

        public void Deactivate() {
            isActive = false;
            _hitObjects.Clear();
        }

        public void ConfigureFromConfig(HitboxDefinition configDefinition, LayerMask configTargetLayer) {
            if (!useComponentDefinition)
                definition = configDefinition;

            if (!useComponentTargetLayer)
                targetLayer = configTargetLayer;
        }

        public Hurtbox CheckHit(Vector3 attackerOrigin, MoveData attackerState) {
            if (!isActive) return null;

            Vector3 center = transform.TransformPoint(definition.offset);
            Quaternion rotation = transform.rotation * Quaternion.Euler(definition.rotationOffset);

            Collider[] hits = GetOverlaps(center, rotation);

            foreach (var hit in hits) {
                Hurtbox hurtbox = hit.GetComponent<Hurtbox>();
                if (hurtbox == null) hurtbox = hit.GetComponentInParent<Hurtbox>();
                
                if (hurtbox != null && !_hitObjects.Contains(hurtbox)) {
                    Debug.Log($"[Hitbox] {gameObject.name} DETECTED Hurtbox: {hurtbox.name}");
                    _hitObjects.Add(hurtbox);
                    return hurtbox;
                }
            }
            return null;
        }

        private Collider[] GetOverlaps(Vector3 center, Quaternion rotation) {
            Collider[] hits;
            if (definition.shape == ShapeType.Box) {
                hits = Physics.OverlapBox(center, definition.size / 2f, rotation, targetLayer, QueryTriggerInteraction.Collide);
            } else if (definition.shape == ShapeType.Sphere) {
                hits = Physics.OverlapSphere(center, definition.radius, targetLayer, QueryTriggerInteraction.Collide);
            } else if (definition.shape == ShapeType.Cone) {
                hits = Physics.OverlapSphere(center, definition.radius, targetLayer, QueryTriggerInteraction.Collide);
                
                // Filter by angle relative to our rotated forward
                Vector3 rotatedForward = rotation * Vector3.forward;
                List<Collider> filteredHits = new List<Collider>();
                foreach (var hit in hits) {
                    Vector3 closestPoint = hit.ClosestPoint(center);
                    Vector3 dirToTarget = (closestPoint - center).normalized;
                    float dot = Vector3.Dot(rotatedForward, dirToTarget);
                    
                    if (dot >= definition.coneAngle) {
                        filteredHits.Add(hit);
                    }
                }
                hits = filteredHits.ToArray();
            } else {
                hits = new Collider[0];
            }
            return hits;
        }

        private void OnDrawGizmos() {
            if (definition.id == null) return;

            Gizmos.color = isActive ? Color.red : Color.white;
            Vector3 center = transform.TransformPoint(definition.offset);
            Quaternion combinedRotation = transform.rotation * Quaternion.Euler(definition.rotationOffset);
            
            Matrix4x4 oldRotation = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(center, combinedRotation, Vector3.one);

            if (definition.shape == ShapeType.Box) {
                if (isActive) Gizmos.DrawCube(Vector3.zero, definition.size);
                Gizmos.DrawWireCube(Vector3.zero, definition.size);
            } else if (definition.shape == ShapeType.Sphere) {
                if (isActive) Gizmos.DrawSphere(Vector3.zero, definition.radius);
                Gizmos.DrawWireSphere(Vector3.zero, definition.radius);
            } else if (definition.shape == ShapeType.Cone) {
                DrawWireCone(Vector3.zero, Vector3.forward, definition.radius, definition.coneAngle);
            }

            Gizmos.matrix = oldRotation;
        }

        private void DrawWireCone(Vector3 origin, Vector3 direction, float distance, float dotThreshold) {
            float angle = Mathf.Acos(Mathf.Clamp(dotThreshold, -1f, 1f)) * Mathf.Rad2Deg;
            
            // Draw rays for the cone edges
            int segments = 8;
            for (int i = 0; i < segments; i++) {
                float currentAngle = (i * 360f / segments) * Mathf.Deg2Rad;
                Vector3 offset = new Vector3(Mathf.Cos(currentAngle), Mathf.Sin(currentAngle), 0) * Mathf.Tan(angle * Mathf.Deg2Rad) * distance;
                Vector3 endPoint = (direction * distance) + (transform.rotation * offset); // This logic is slightly off because Gizmos.matrix is already TRS, but transform.rotation is absolute.
                // Simple version: just draw the circle at the end and some rays
                Vector3 relativeEnd = (direction * distance) + (Quaternion.AngleAxis(i * 360f / segments, direction) * (Vector3.up * Mathf.Tan(angle * Mathf.Deg2Rad) * distance));
                Gizmos.DrawLine(origin, relativeEnd);
                
                // Draw circle segment
                Vector3 nextRelativeEnd = (direction * distance) + (Quaternion.AngleAxis((i + 1) * 360f / segments, direction) * (Vector3.up * Mathf.Tan(angle * Mathf.Deg2Rad) * distance));
                Gizmos.DrawLine(relativeEnd, nextRelativeEnd);
            }
        }
    }
}
