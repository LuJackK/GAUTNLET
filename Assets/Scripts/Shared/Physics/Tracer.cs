using UnityEngine;

namespace Fragsurf.TraceUtil {
    public class Tracer {
        // Cast geometry comes from serialized shape data, never renderer/broadphase
        // bounds. Distances describe travel of the unshrunk shape, in world metres.
        // PhysX box sweeps can miss an initially touching floor with a sub-millimetre
        // inset. One centimetre stays outside that contact tolerance.
        private const float Skin = 0.01f;

        public static Trace TraceCollider(Collider collider, Vector3 origin, Vector3 end, int layerMask, float colliderScale = 1f) {
            Vector3 scale = collider.transform.lossyScale;
            scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            if (collider is BoxCollider box) {
                Vector3 center = Vector3.Scale(box.center, scale);
                return TraceBox(origin + center, end + center, Vector3.Scale(box.size * 0.5f, scale),
                    collider.contactOffset, layerMask, colliderScale);
            }
            if (collider is CapsuleCollider capsule) {
                Movement.SurfPhysics.GetCapsulePoints(capsule, origin, out Vector3 p1, out Vector3 p2);
                float radius = capsule.radius * Mathf.Max(scale.x, scale.z);
                return TraceCapsule(p1, p2, radius, origin, end, collider.contactOffset, layerMask, colliderScale);
            }
            throw new System.NotImplementedException("Trace missing for collider: " + collider.GetType());
        }

        public static Trace TraceCapsule(Vector3 point1, Vector3 point2, float radius, Vector3 start,
            Vector3 destination, float contactOffset, int layerMask, float colliderScale = 1f) {
            Vector3 displacement = destination - start;
            float distance = displacement.magnitude;
            Trace result = new Trace { startPos = start, endPos = destination, fraction = 1f };
            if (distance <= 0f) return result;
            Vector3 center = (point1 + point2) * 0.5f;
            point1 = center + (point1 - center) * colliderScale;
            point2 = center + (point2 - center) * colliderScale;
            if (Physics.CapsuleCast(point1, point2, Mathf.Max(Skin, radius * colliderScale - Skin),
                displacement / distance, out RaycastHit hit, distance + Skin, layerMask, QueryTriggerInteraction.Ignore))
                FillHit(ref result, hit, distance);
            return result;
        }

        public static Trace TraceBox(Vector3 start, Vector3 destination, Vector3 extents, float contactOffset,
            int layerMask, float colliderScale = 1f) {
            Vector3 displacement = destination - start;
            float distance = displacement.magnitude;
            Trace result = new Trace { startPos = start, endPos = destination, fraction = 1f };
            if (distance <= 0f) return result;
            extents = Vector3.Max(Vector3.one * Skin, extents * colliderScale - Vector3.one * Skin);
            if (Physics.BoxCast(start, extents, displacement / distance, out RaycastHit hit,
                Quaternion.identity, distance + Skin, layerMask, QueryTriggerInteraction.Ignore))
                FillHit(ref result, hit, distance);
            return result;
        }

        private static void FillHit(ref Trace result, RaycastHit hit, float distance) {
            result.hitCollider = hit.collider;
            result.hitPoint = hit.point;
            result.planeNormal = hit.normal;
            result.distance = Mathf.Clamp(hit.distance - Skin, 0f, distance);
            result.fraction = result.distance / distance;
        }
    }
}
