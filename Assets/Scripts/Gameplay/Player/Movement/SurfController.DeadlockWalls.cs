using UnityEngine;
using Fragsurf.TraceUtil;

namespace Fragsurf.Movement {
    // Deadlock wall jump (B-WAL-01) and mantle (B-MNT-01, B-MNT-02). Formulas were checked against
    // the 2026-10-26 captures (Research/DeadlockMovement/BEHAVIOURS.md, "Walls and mantle").
    public partial class SurfController {
        // Fixed world directions keep wall detection independent of view and query order.
        private static readonly Vector3[] WallProbeDirections = {
            new Vector3(1f, 0f, 0f), new Vector3(0.70710678f, 0f, 0.70710678f),
            new Vector3(0f, 0f, 1f), new Vector3(-0.70710678f, 0f, 0.70710678f),
            new Vector3(-1f, 0f, 0f), new Vector3(-0.70710678f, 0f, -0.70710678f),
            new Vector3(0f, 0f, -1f), new Vector3(0.70710678f, 0f, -0.70710678f),
        };

        // Wall detection: walls within a speed-dependent distance are weighted 1 at contact to 0 at
        // the distance; the flattened sum is the wall normal, confirmed by a trace toward it. With no
        // wall this tick, the last one is kept while within half the distance of where it was seen.
        private static void UpdateDeadlockWall(ISurfControllable surfer, DeadlockMovementConfig d) {
            MoveData s = surfer.moveData;
            if (s.grounded) {
                s.wallNormal = Vector3.zero;
                s.consecutiveWallJumps = 0;
                return;
            }
            Vector4 bySpeed = d.wallDetectionBySpeed;
            float distance = Mathf.Lerp(bySpeed.z, bySpeed.w, Mathf.InverseLerp(bySpeed.x, bySpeed.y, Planar(s.velocity).magnitude));
            if (distance > 0f && SurfPhysics.Blocked(surfer.collider, s.origin, distance)) {
                Vector3 sum = Vector3.zero;
                foreach (Vector3 direction in WallProbeDirections) {
                    Trace trace = Tracer.TraceCollider(surfer.collider, s.origin, s.origin + direction * distance, SurfPhysics.groundLayerMask);
                    if (trace.hitCollider == null || Mathf.Abs(trace.planeNormal.y) > d.wallMaxNormalY) continue;
                    sum += trace.planeNormal * (1f - trace.distance / distance);
                }
                Vector3 flat = Planar(sum);
                if (flat.sqrMagnitude > 0.000001f) {
                    Vector3 normal = flat.normalized;
                    Trace confirm = Tracer.TraceCollider(surfer.collider, s.origin,
                        s.origin - normal * (distance + d.wallDetectionSkin), SurfPhysics.groundLayerMask);
                    if (confirm.hitCollider != null && Mathf.Abs(confirm.planeNormal.y) <= d.wallMaxNormalY) {
                        s.wallNormal = normal;
                        s.wallSeenOrigin = s.origin;
                        return;
                    }
                }
            }
            if (s.wallNormal != Vector3.zero && Planar(s.origin - s.wallSeenOrigin).magnitude > distance * 0.5f)
                s.wallNormal = Vector3.zero;
        }

        // B-WAL-01. facing is the raw input vector (forward + side, not normalised: a diagonal has
        // length sqrt 2, which the capture fit needs).
        private static bool TryDeadlockWallJump(ISurfControllable surfer, MovementConfig config, Vector3 facing) {
            MoveData s = surfer.moveData;
            DeadlockMovementConfig d = config.deadlock;
            if (s.grounded || s.wallNormal == Vector3.zero) return false;
            float cost = s.consecutiveWallJumps < d.wallJumpFreeCount ? d.wallJumpCosts.x : d.wallJumpCosts.y;
            if (s.stamina <= cost) return false;

            Vector3 direction = s.wallNormal;
            Vector3 tangent = new Vector3(-direction.z, 0f, direction.x);
            float into = Vector3.Dot(s.velocity, direction);
            if (into < 0f) s.velocity -= direction * into;
            s.velocity += direction * Mathf.Lerp(d.wallJumpOutSpeed.x, d.wallJumpOutSpeed.y,
                Mathf.Clamp01((Vector3.Dot(facing, direction) + 1f) * 0.5f));

            float vertical = s.velocity.y;
            s.velocity.y = (vertical < d.wallJumpKeepBelowSpeed ? vertical * d.wallJumpVerticalKeep : 0f) +
                           DeadlockWallJumpPower(s.wallJumpFatigueTime, d) * d.wallJumpUpSpeed;

            float along = Vector3.Dot(s.velocity, tangent);
            float target = Mathf.Lerp(-d.wallJumpLateralSpeed, d.wallJumpLateralSpeed,
                Mathf.Clamp01((Vector3.Dot(facing, tangent) + 1f) * 0.5f));
            // Pushing against the current along-wall motion is not faded.
            float falloff = DeadlockLateralFalloff(target * along >= 0f ? Mathf.Abs(along) : 0f, d);
            s.velocity += tangent * (target * falloff);

            LeaveSlide(s);
            s.isDashing = s.airDashing = false;
            s.dashTimer = 0f;
            s.wallJumpFatigueTime = 0f;
            s.consecutiveWallJumps++;
            s.wallNormal = Vector3.zero;
            s.jumpCount = Mathf.Max(1, s.jumpCount);
            s.coyoteTimer = 0f;
            s.jumpTimer = 0f;
            s.jumping = true;
            if (cost > 0f) SpendStamina(s, cost, config);
            return true;
        }

        // Power = Bias(remap(time since the last wall jump, 0..recharge, min..1), bias), with Valve's
        // Schlick bias x / ((1/b - 2)(1 - x) + 1).
        private static float DeadlockWallJumpPower(float sinceLast, DeadlockMovementConfig d) {
            float recharge = Mathf.Max(0.0001f, d.wallJumpFatigueTime);
            float x = Mathf.Lerp(d.wallJumpPowerMinAndBias.x, 1f, Mathf.Clamp01(sinceLast / recharge));
            float bias = Mathf.Clamp(d.wallJumpPowerMinAndBias.y, 0.0001f, 1f);
            return x / ((1f / bias - 2f) * (1f - x) + 1f);
        }

        // m_WallJumpLateralSpeedFalloffVsAlongSpeed: 1 up to the first speed, a Hermite segment to 0
        // at the second.
        private static float DeadlockLateralFalloff(float speed, DeadlockMovementConfig d) {
            Vector2 span = d.wallJumpLateralFalloffSpeeds;
            if (speed <= span.x) return 1f;
            if (speed >= span.y) return 0f;
            float u = (speed - span.x) / (span.y - span.x);
            float u2 = u * u, u3 = u2 * u;
            float h00 = 2f * u3 - 3f * u2 + 1f, h10 = u3 - 2f * u2 + u, h11 = u3 - u2;
            return Mathf.Clamp01(h00 + h10 * d.wallJumpLateralFalloffTangents.x + h11 * d.wallJumpLateralFalloffTangents.y);
        }

        // B-MNT-01: pushing into a wall with a ledge on top starts a mantle. Pushing counts while
        // speed toward the wall is below the blocked speed; the ledge is checked once pushed long enough.
        private static bool TryStartDeadlockMantle(ISurfControllable surfer, DeadlockMovementConfig d, Vector3 wish, float dt) {
            MoveData s = surfer.moveData;
            Vector3 input = Planar(wish);
            if (input.sqrMagnitude < 0.01f) { s.mantlePushTimer = 0f; return false; }
            Trace wall = Tracer.TraceCollider(surfer.collider, s.origin, s.origin + input.normalized * d.mantleProbeDepth, SurfPhysics.groundLayerMask);
            Vector3 wallFlat = wall.hitCollider != null ? Planar(wall.planeNormal) : Vector3.zero;
            if (wallFlat.sqrMagnitude < 0.000001f || Mathf.Abs(wall.planeNormal.y) > d.wallMaxNormalY) { s.mantlePushTimer = 0f; return false; }
            Vector3 direction = -wallFlat.normalized;
            if (Vector3.Dot(input.normalized, direction) < 0.5f ||
                Vector3.Dot(Planar(s.velocity), direction) > d.mantleBlockedMaxSpeed) {
                s.mantlePushTimer = 0f;
                return false;
            }
            s.mantlePushTimer += dt;
            if (s.mantlePushTimer < d.mantlePushTime) return false;

            // Drop the hull onto the ledge from the highest mantle height above the feet.
            float maxHeight = s.grounded ? d.mantleMaxGroundHeight : MaxMantleHeight(d);
            float across = wall.distance + d.mantleHorizontalDistance;
            Vector3 high = s.origin + direction * across + Vector3.up * (maxHeight + d.mantleClearance);
            if (SurfPhysics.Blocked(surfer.collider, high, 0f)) return false;
            Trace top = Tracer.TraceCollider(surfer.collider, high,
                high + Vector3.down * (maxHeight + d.mantleClearance - d.mantleMinHeight), SurfPhysics.groundLayerMask);
            if (top.hitCollider == null || Vector3.Angle(Vector3.up, top.planeNormal) > s.slopeLimit) return false;
            float height = maxHeight + d.mantleClearance - top.distance;
            if (height < d.mantleMinHeight) return false;
            // Room to rise beside the wall, then to move onto the ledge.
            Vector3 raised = s.origin + Vector3.up * (height + d.mantleClearance);
            if (Tracer.TraceCollider(surfer.collider, s.origin, raised, SurfPhysics.groundLayerMask).hitCollider != null) return false;
            if (Tracer.TraceCollider(surfer.collider, raised, raised + direction * across, SurfPhysics.groundLayerMask).hitCollider != null) return false;

            int type = -1;
            for (int i = 0; i < d.mantleTypes.Length; i++) {
                Vector4 candidate = d.mantleTypes[i];
                if (height < candidate.y || height > candidate.z) continue;
                if (type < 0 || Mathf.Abs(height - candidate.x) < Mathf.Abs(height - d.mantleTypes[type].x)) type = i;
            }
            if (type < 0) return false;

            LeaveSlide(s);
            s.isDashing = s.airDashing = false;
            s.dashTimer = 0f;
            s.mantling = true;
            s.mantleTime = 0f;
            s.mantlePushTimer = 0f;
            s.mantleStart = s.origin;
            s.mantleDirection = direction;
            s.mantleDistance = across;
            s.mantleHeight = d.mantleTypes[type].x;
            s.mantleOffset = height - d.mantleTypes[type].x;
            s.mantleVerticalTime = Mathf.Max(0.0001f, d.mantleTypes[type].w);
            s.mantleJumpBuffered = false;
            s.velocity = Vector3.zero;
            s.grounded = false;
            surfer.groundObject = null;
            return true;
        }

        private static float MaxMantleHeight(DeadlockMovementConfig d) {
            float max = 0f;
            foreach (Vector4 type in d.mantleTypes) max = Mathf.Max(max, type.z);
            return max;
        }

        private static float MantleDuration(MoveData s, DeadlockMovementConfig d) => s.mantleVerticalTime + d.mantleHorizontalTime;

        // Measured path: the type height rises linearly over the vertical time, the rest of the ledge
        // height (the offset) eases in quadratically over its first half; then the horizontal part.
        private static Vector3 MantlePathPoint(MoveData s, DeadlockMovementConfig d, float time) {
            float vertical = Mathf.Clamp01(time / s.mantleVerticalTime);
            float early = 1f - Mathf.Clamp01(time / (s.mantleVerticalTime * 0.5f));
            float rise = (s.mantleHeight + d.mantleClearance) * vertical + s.mantleOffset * (1f - early * early);
            float across = d.mantleHorizontalTime > 0f ? Mathf.Clamp01((time - s.mantleVerticalTime) / d.mantleHorizontalTime) : 1f;
            return s.mantleStart + Vector3.up * rise + s.mantleDirection * (s.mantleDistance * across);
        }

        // The start tick holds still (velocity zeroed); each later tick moves to the next path point.
        private static void StepDeadlockMantle(MoveData s, DeadlockMovementConfig d, bool started, float dt) {
            if (started) { s.velocity = Vector3.zero; return; }
            float next = Mathf.Min(s.mantleTime + dt, MantleDuration(s, d));
            s.velocity = (MantlePathPoint(s, d, next) - s.origin) / dt;
            s.mantleTime = next;
            if (s.wishJumpDown && MantleDuration(s, d) - next <= d.mantleJumpBuffer) s.mantleJumpBuffered = true;
        }

        // B-MNT-01 exit / B-MNT-02. With forward input the horizontal path speed is kept (capped at
        // move speed); without it velocity is zeroed. Crouch with forward adds the mantle-slide boost;
        // the slide itself starts the next tick, so this tick has ordinary ground friction (measured).
        private static void FinishDeadlockMantle(MoveData s, DeadlockMovementConfig d, Vector3 wish, float moveSpeed) {
            s.mantling = false;
            Vector3 direction = s.mantleDirection;
            Vector3 planar = Vector3.zero;
            if (Vector3.Dot(Planar(wish), direction) > 0.01f) {
                float speed = d.mantleHorizontalTime > 0f ? s.mantleDistance / d.mantleHorizontalTime : 0f;
                planar = direction * Mathf.Min(speed, moveSpeed);
                if (s.slidingEnabled && s.slideRequested) {
                    planar += direction * d.mantleSlideBoost;
                    s.slideDelay = Mathf.Max(s.slideDelay, 0.000001f);
                }
            }
            s.velocity = planar;
        }
    }
}
