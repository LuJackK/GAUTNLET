using UnityEngine;

namespace Fragsurf.Movement {
    // Query-only collision helpers. The world is read, never advanced or pushed
    // during prediction/replay. Moving world objects need their own rollback history.
    public class SurfPhysics {
        public static int groundLayerMask = LayerMask.GetMask("Default", "Ground", "Player clip");
        public const float HU2M = 52.4934383202f;
        public const float SurfSlope = 0.7f;
        private static readonly Collider[] Overlaps = new Collider[128];

        public static void ResolveCollisions(Collider collider, ref Vector3 origin, ref Vector3 velocity,
            float rigidbodyPushForce, float deltaTime, float velocityMultiplier = 1f,
            float stepOffset = 0f, ISurfControllable surfer = null) {
            // Old optional arguments remain for source compatibility. Stepping is
            // performed by the swept motor, and untracked rigidbody impulses are retired.
            for (int iteration = 0; iteration < 8; iteration++) {
                Collider[] hits = Overlaps;
                int count = CollectOverlaps(collider, origin, ref hits);
                float deepest = 0f;
                Vector3 normal = Vector3.zero;
                for (int i = 0; i < count; i++) {
                    Collider other = hits[i];
                    if (other == null || other == collider) continue;
                    if (!Physics.ComputePenetration(collider, origin, Quaternion.identity,
                        other, other.transform.position, other.transform.rotation,
                        out Vector3 direction, out float distance)) continue;
                    // Deepest-first avoids depending on the physics query's order.
                    if (distance > deepest || (distance == deepest && CompareNormal(direction, normal) > 0)) {
                        deepest = distance;
                        normal = direction;
                    }
                }
                if (deepest <= 0.000001f) break;
                origin += normal * (deepest + 0.0001f);
                float into = Vector3.Dot(velocity, normal);
                if (into < 0f) velocity -= normal * into;
            }
        }

        private static int CompareNormal(Vector3 a, Vector3 b) {
            int x = a.x.CompareTo(b.x);
            if (x != 0) return x;
            int y = a.y.CompareTo(b.y);
            return y != 0 ? y : a.z.CompareTo(b.z);
        }

        private static Vector3 Scale(Collider collider) {
            Vector3 s = collider.transform.lossyScale;
            return new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
        }

        private static int CollectOverlaps(Collider collider, Vector3 origin, ref Collider[] hits) {
            Vector3 scale = Scale(collider);
            int count;
            if (collider is CapsuleCollider capsule) {
                GetCapsulePoints(capsule, origin, out Vector3 a, out Vector3 b);
                float radius = capsule.radius * Mathf.Max(scale.x, scale.z);
                count = Physics.OverlapCapsuleNonAlloc(a, b, radius, hits, groundLayerMask, QueryTriggerInteraction.Ignore);
                if (count == hits.Length) {
                    hits = Physics.OverlapCapsule(a, b, radius, groundLayerMask, QueryTriggerInteraction.Ignore);
                    count = hits.Length;
                }
            } else if (collider is BoxCollider box) {
                Vector3 center = origin + Vector3.Scale(box.center, scale);
                Vector3 extents = Vector3.Scale(box.size * 0.5f, scale);
                count = Physics.OverlapBoxNonAlloc(center, extents, hits, Quaternion.identity, groundLayerMask, QueryTriggerInteraction.Ignore);
                if (count == hits.Length) {
                    hits = Physics.OverlapBox(center, extents, Quaternion.identity, groundLayerMask, QueryTriggerInteraction.Ignore);
                    count = hits.Length;
                }
            } else {
                throw new System.NotSupportedException("Motor requires a box or vertical capsule.");
            }
            return count;
        }

        public static void Friction(ref Vector3 velocity, float stopSpeed, float friction, float deltaTime) {
            float speed = velocity.magnitude;
            if (speed <= 0.000001f) return;
            float drop = Mathf.Max(speed, stopSpeed) * Mathf.Max(0f, friction) * deltaTime;
            velocity *= Mathf.Max(0f, speed - drop) / speed;
        }

        public static void GetCapsulePoints(CapsuleCollider capsule, Vector3 origin, out Vector3 p1, out Vector3 p2) {
            Vector3 scale = Scale(capsule);
            float radius = capsule.radius * Mathf.Max(scale.x, scale.z);
            float halfSegment = Mathf.Max(0f, capsule.height * scale.y * 0.5f - radius);
            Vector3 center = origin + Vector3.Scale(capsule.center, scale);
            p1 = center + Vector3.up * halfSegment;
            p2 = center - Vector3.up * halfSegment;
        }
    }
}
