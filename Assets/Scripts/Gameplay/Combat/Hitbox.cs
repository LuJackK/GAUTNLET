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
        [SerializeField] private bool enableDebugLogging = true;

        public HitboxDefinition definition;
        public LayerMask targetLayer;
        public bool isActive = false;
        private Vector3 _queryLocalPosition;
        private Quaternion _queryLocalRotation;
        private Vector3 _queryLocalScale = Vector3.one;

        private void Awake() {
            _queryLocalPosition = transform.localPosition;
            _queryLocalRotation = transform.localRotation;
            _queryLocalScale = transform.lossyScale;
        }

        public void Activate() {
            isActive = true;
            Debug.Log($"[Hitbox] {gameObject.name} ACTIVATED. Target Layer: {targetLayer.value}");
        }

        public void Deactivate() {
            isActive = false;
        }

        public void ConfigureFromConfig(HitboxDefinition configDefinition, LayerMask configTargetLayer) {
            if (!useComponentDefinition)
                definition = configDefinition;

            if (!useComponentTargetLayer)
                targetLayer = configTargetLayer;
        }

        public Hurtbox CheckHit(Vector3 attackerOrigin, MoveData attackerState, Hurtbox ownerHurtbox = null) {
            BuildQueryPose(attackerOrigin, attackerState, out Vector3 center, out Quaternion rotation);

            Collider[] hits = GetOverlaps(center, rotation);
            LogQuery(attackerOrigin, attackerState, center, rotation, hits, ownerHurtbox);

            foreach (var hit in hits) {
                Hurtbox hurtbox = ResolveHurtbox(hit);
                if (enableDebugLogging) {
                    string hurtboxName = hurtbox != null ? hurtbox.name : "<none>";
                    string hitName = hit != null ? hit.name : "<null>";
                    Debug.Log($"[Hitbox] Inspecting overlap collider='{hitName}', resolvedHurtbox='{hurtboxName}', ownerMatch={(hurtbox == ownerHurtbox)}", this);
                }

                if (hurtbox != null && hurtbox != ownerHurtbox) {
                    Debug.Log($"[Hitbox] {gameObject.name} DETECTED Hurtbox: {hurtbox.name}");
                    return hurtbox;
                }
            }

            if (enableDebugLogging)
                Debug.Log($"[Hitbox] {gameObject.name} found no valid hurtbox target. overlapCount={hits.Length}", this);
            return null;
        }

        private static Hurtbox ResolveHurtbox(Collider hit) {
            if (hit == null)
                return null;

            Hurtbox hurtbox = hit.GetComponent<Hurtbox>();
            if (hurtbox != null)
                return hurtbox;

            hurtbox = hit.GetComponentInParent<Hurtbox>();
            if (hurtbox != null)
                return hurtbox;

            SurfCharacter surfCharacter = hit.GetComponentInParent<SurfCharacter>();
            if (surfCharacter != null && surfCharacter.playerHurtboxComponent != null)
                return surfCharacter.playerHurtboxComponent;

            return null;
        }

        private void LogQuery(Vector3 attackerOrigin, MoveData attackerState, Vector3 center, Quaternion rotation, Collider[] hits, Hurtbox ownerHurtbox) {
            if (!enableDebugLogging)
                return;

            string ownerName = ownerHurtbox != null ? ownerHurtbox.name : "<none>";
            string moveType = attackerState != null ? attackerState.moveType.ToString() : "<null>";
            string meleeState = attackerState != null ? attackerState.meleeState.ToString() : "<null>";
            string shapeSummary;

            if (definition.shape == ShapeType.Box)
                shapeSummary = $"box size={definition.size}";
            else if (definition.shape == ShapeType.Sphere)
                shapeSummary = $"sphere radius={definition.radius}";
            else if (definition.shape == ShapeType.Cone)
                shapeSummary = $"cone radius={definition.radius}, coneAngleDot={definition.coneAngle}";
            else
                shapeSummary = $"shape={definition.shape}";

            Debug.Log($"[Hitbox] Query '{gameObject.name}': origin={attackerOrigin}, center={center}, yaw={(attackerState != null ? attackerState.viewAngles.y : 0f):F2}, moveType={moveType}, meleeState={meleeState}, targetLayer={targetLayer.value}, ownerHurtbox={ownerName}, {shapeSummary}, overlapCount={hits.Length}", this);

            for (int i = 0; i < hits.Length; i++) {
                Collider hit = hits[i];
                if (hit == null) {
                    Debug.Log($"[Hitbox]   overlap[{i}] = <null>", this);
                    continue;
                }

                Debug.Log($"[Hitbox]   overlap[{i}] collider='{hit.name}', layer={hit.gameObject.layer}, root='{hit.transform.root.name}', path='{BuildPath(hit.transform)}'", this);
            }
        }

        private static string BuildPath(Transform current) {
            if (current == null)
                return "<null>";

            System.Text.StringBuilder sb = new System.Text.StringBuilder(current.name);
            while (current.parent != null) {
                current = current.parent;
                sb.Insert(0, current.name + "/");
            }

            return sb.ToString();
        }

        private void BuildQueryPose(Vector3 attackerOrigin, MoveData attackerState, out Vector3 center, out Quaternion rotation) {
            float yaw = (attackerState != null) ? attackerState.viewAngles.y : 0f;
            Quaternion attackerRotation = Quaternion.Euler(0f, yaw, 0f);
            Quaternion localRotation = attackerRotation * _queryLocalRotation;
            rotation = localRotation * Quaternion.Euler(definition.rotationOffset);

            Vector3 localCenter = Vector3.Scale(_queryLocalPosition + definition.offset, _queryLocalScale);
            center = attackerOrigin + (attackerRotation * localCenter);
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
            if (!HasDrawableDefinition())
                return;

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

        private bool HasDrawableDefinition() {
            if (definition.shape == ShapeType.Box)
                return definition.size.sqrMagnitude > 0f;

            if (definition.shape == ShapeType.Sphere || definition.shape == ShapeType.Cone)
                return definition.radius > 0f;

            return !string.IsNullOrWhiteSpace(definition.id);
        }
    }
}
