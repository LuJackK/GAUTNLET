using UnityEngine;
using Fragsurf.TraceUtil;

namespace Fragsurf.Movement {
    // All persistent gameplay state belongs to MoveData. This controller may be
    // recreated between any two ticks without changing the result.
    public class SurfController {
        public Transform playerTransform;
        public Transform camera;
        public float cameraYPos;
        private ISurfControllable _surfer;
        public bool IsSliding => _surfer != null && _surfer.moveData.sliding;
        public Vector3 SlideDirection => _surfer != null ? _surfer.moveData.slideDirection : Vector3.forward;
        public bool jumping { get => _surfer != null && _surfer.moveData.jumping; set { if (_surfer != null) _surfer.moveData.jumping = value; } }
        public bool crouching { get => _surfer != null && _surfer.moveData.crouching; set { if (_surfer != null) _surfer.moveData.crouching = value; } }
        public float speed { get => _surfer != null ? _surfer.moveData.velocity.magnitude : 0f; set { } }

        public void ProcessMovement(ISurfControllable surfer, MovementConfig config, float dt) {
            _surfer = surfer;
            MoveData s = surfer.moveData;
            if (dt <= 0f) return;
            s.underwater = s.cameraUnderwater = s.climbingLadder = false;
            s.dashTimer = Mathf.Max(0f, s.dashTimer - dt);
            s.dashCooldownTimer = Mathf.Max(0f, s.dashCooldownTimer - dt);
            s.slideDelay = Mathf.Max(0f, s.slideDelay - dt);
            s.isDashing = s.isDashing && s.dashTimer > 0f;
            float regenTime = Mathf.Max(0f, dt - s.staminaRegenTimer);
            s.staminaRegenTimer = Mathf.Max(0f, s.staminaRegenTimer - dt);
            s.stamina = Mathf.MoveTowards(s.stamina, config.maxStamina, config.staminaRegenRate * regenTime);
            s.jumpTimer += dt;

            Vector3 normal = FindGround(surfer, config);
            Quaternion yaw = Quaternion.Euler(0f, s.viewAngles.y, 0f);
            Vector3 wish = Vector3.ClampMagnitude(yaw * new Vector3(s.horizontalAxis, 0f, s.verticalAxis), 1f);
            bool melee = s.moveType == MoveType.HeavyMelee;
            if (!melee && s.moveType == MoveType.Walk) {
                // Dash and jump compose in the same tick, before ground friction.
                TryDash(s, config, wish, yaw * Vector3.forward);
                bool groundJump = s.grounded && (config.autoBhop ? s.wishJump : s.wishJumpDown);
                if (groundJump) {
                    LeaveSlide(s);
                    s.velocity.y = config.jumpForce;
                    s.jumpCount = 1;
                    s.jumpTimer = 0f;
                    s.jumping = true;
                    s.grounded = false;
                    surfer.groundObject = null;
                    s.isDashing = false;
                    s.dashTimer = 0f;
                } else if (!s.grounded && s.wishJumpDown && Mathf.Max(1, s.jumpCount) < config.maxJumps &&
                           s.jumpTimer >= config.doubleJumpDelay && s.stamina >= config.doubleJumpCost) {
                    LeaveSlide(s);
                    s.velocity.y = config.doubleJumpForce;
                    s.jumpCount = Mathf.Max(1, s.jumpCount) + 1;
                    s.jumpTimer = 0f;
                    s.jumping = true;
                    s.isDashing = false;
                    s.dashTimer = 0f;
                    s.doubleJumpedThisFrame = true;
                    SpendStamina(s, config.doubleJumpCost, config);
                }

                if (s.grounded) {
                    Vector3 planar = Planar(s.velocity);
                    bool slope = Vector3.Angle(Vector3.up, normal) > 5f;
                    bool slide = s.slidingEnabled && s.slideRequested && s.slideDelay <= 0f &&
                                 (planar.magnitude >= config.minimumSlideSpeed || slope);
                    if (slide) {
                        s.sliding = s.wasSliding = true;
                        // Collision-adjusted velocity is the sole source of momentum.
                        // Slide entry never multiplies speed.
                        Vector3 downhill = Vector3.ProjectOnPlane(Vector3.down * config.gravity, normal);
                        planar += Planar(downhill) * config.slideSlopeAcceleration * dt;
                        planar = Steer(planar, wish, config.slideControlAcceleration * dt);
                        planar = Vector3.MoveTowards(planar, Vector3.zero, config.slideDrag * dt);
                    } else {
                        LeaveSlide(s);
                        float friction = s.crouching ? config.crouchFriction : config.friction;
                        float stopSpeed = s.crouching ? config.crouchDeceleration : config.deceleration;
                        if (s.isDashing) friction *= config.dashFrictionOverride;
                        SurfPhysics.Friction(ref planar, stopSpeed, friction, dt);
                        float targetSpeed = (s.crouching ? config.crouchSpeed : config.walkSpeed) * s.walkFactor;
                        float acceleration = s.crouching ? config.crouchAcceleration : config.acceleration;
                        Accelerate(ref planar, wish, targetSpeed, acceleration * targetSpeed * dt);
                    }
                    s.velocity = planar;
                    if (normal.y > 0.001f)
                        s.velocity.y = -(normal.x * planar.x + normal.z * planar.z) / normal.y;
                } else {
                    LeaveSlide(s);
                    Vector3 planar = Planar(s.velocity);
                    planar = Steer(planar, wish, config.airControlAcceleration * dt);
                    Accelerate(ref planar, wish, config.maxSpeed, config.airAcceleration * dt);
                    s.velocity.x = planar.x;
                    s.velocity.z = planar.z;
                }
            } else if (melee) {
                LeaveSlide(s);
                s.isDashing = false;
                s.dashTimer = 0f;
                Vector3 planar = Planar(s.velocity);
                if (s.grounded && s.meleeState != MoveData.MeleeState.Lunging) {
                    float drag = s.meleeState == MoveData.MeleeState.Charging
                        ? config.heavyMeleeChargeFriction : config.heavyMeleeRecoveryDrag;
                    SurfPhysics.Friction(ref planar, 0f, drag, dt);
                }
                if (s.meleeState != MoveData.MeleeState.Lunging)
                    planar = Steer(planar, wish, config.airControlAcceleration * dt);
                s.velocity.x = planar.x;
                s.velocity.z = planar.z;
            }

            // Charge hovers, including when entered during a fall. This policy is
            // derived entirely from reconciled state, not a live Rigidbody setting.
            bool hovering = melee && s.meleeState == MoveData.MeleeState.Charging;
            if (hovering)
                s.velocity.y = 0f;
            else if (!s.grounded && !s.isDashing)
                s.velocity.y -= config.gravity * s.gravityFactor * dt;
            Vector3 capped = Vector3.ClampMagnitude(Planar(s.velocity), config.maxVelocity);
            s.velocity.x = capped.x;
            s.velocity.z = capped.z;
            MoveAndSlide(surfer, config, dt);
            if (s.sliding) {
                s.slideSpeedCurrent = Planar(s.velocity).magnitude;
                s.slideDirection = Planar(s.velocity).normalized;
            }
            s.jumping = !s.grounded && s.velocity.y > 0f;
            s.groundedTemp = s.grounded;
            if (surfer is SurfCharacter character)
                character.SetMovementQueryDiagnostics($"grounded={s.grounded}, normal={normal}, sliding={s.sliding}, velocity={s.velocity}");
        }

        private static Vector3 Planar(Vector3 v) => new Vector3(v.x, 0f, v.z);

        private static Vector3 Steer(Vector3 velocity, Vector3 wish, float amount) {
            float speed = velocity.magnitude;
            if (speed < 0.001f || wish.sqrMagnitude < 0.0001f) return velocity;
            float heading = Mathf.Atan2(velocity.x, velocity.z) * Mathf.Rad2Deg;
            float target = Mathf.Atan2(wish.x, wish.z) * Mathf.Rad2Deg;
            float angle = Mathf.MoveTowardsAngle(heading, target,
                amount * wish.magnitude / Mathf.Max(speed, 1f) * Mathf.Rad2Deg) * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(angle) * speed, 0f, Mathf.Cos(angle) * speed);
        }

        private static void Accelerate(ref Vector3 velocity, Vector3 wish, float speed, float amount) {
            float magnitude = wish.magnitude;
            if (magnitude < 0.0001f) return;
            Vector3 direction = wish / magnitude;
            float missing = speed * magnitude - Vector3.Dot(velocity, direction);
            if (missing > 0f) velocity += direction * Mathf.Min(missing, amount * magnitude);
        }

        private static void SpendStamina(MoveData s, float cost, MovementConfig config) {
            s.stamina = Mathf.Max(0f, s.stamina - cost);
            s.staminaRegenTimer = config.staminaRegenDelay;
        }

        private static void TryDash(MoveData s, MovementConfig config, Vector3 wish, Vector3 forward) {
            if (!s.wishDash || s.dashCooldownTimer > 0f || (!s.grounded && !s.canAirDash)) return;
            float cost = s.grounded ? config.dashCost : config.airDashCost;
            if (s.stamina < cost) return;
            Vector3 direction = wish.sqrMagnitude > 0.0001f ? wish.normalized : forward;
            float speed = Mathf.Max(Planar(s.velocity).magnitude, s.grounded ? config.dashImpulse : config.airDashVelocity);
            s.velocity = direction * speed;
            s.isDashing = s.dashStartedThisFrame = true;
            s.dashTimer = s.currentDashDuration = s.grounded ? config.dashDuration : config.airDashDuration;
            s.dashCooldownTimer = config.dashCooldown;
            if (!s.grounded) s.canAirDash = false;
            SpendStamina(s, cost, config);
        }

        private static void LeaveSlide(MoveData s) {
            s.sliding = s.wasSliding = false;
            s.slideSpeedCurrent = 0f;
            s.slideDirection = Vector3.zero;
        }

        private static Vector3 FindGround(ISurfControllable surfer, MovementConfig config) {
            MoveData s = surfer.moveData;
            bool wasGrounded = s.grounded;
            surfer.groundObject = null;
            s.grounded = false;
            s.surfaceFriction = 1f;
            if ((s.jumping || !wasGrounded || s.meleeState == MoveData.MeleeState.Lunging) && s.velocity.y > 0.01f) return Vector3.up;
            float distance = config.useGroundSnapping && wasGrounded ? config.groundSnapDistance : 0.035f;
            Trace trace = Tracer.TraceCollider(surfer.collider, s.origin, s.origin + Vector3.down * distance, SurfPhysics.groundLayerMask);
            if (trace.hitCollider == null || Vector3.Angle(Vector3.up, trace.planeNormal) > config.slopeLimit)
                return Vector3.up;
            s.grounded = true;
            s.jumping = false;
            s.canAirDash = true;
            s.jumpCount = 0;
            s.velocity.y = 0f;
            surfer.groundObject = trace.hitCollider.gameObject;
            if (config.useGroundSnapping) s.origin.y -= Mathf.Max(0f, trace.distance - 0.002f);
            return trace.planeNormal;
        }

        private static void MoveAndSlide(ISurfControllable surfer, MovementConfig config, float dt) {
            MoveData s = surfer.moveData;
            // Replay never applies impulses to live rigidbodies.
            SurfPhysics.ResolveCollisions(surfer.collider, ref s.origin, ref s.velocity, 0f, dt);
            float remaining = dt;
            for (int bump = 0; bump < 5 && remaining > 0.000001f; bump++) {
                Vector3 displacement = s.velocity * remaining;
                float distance = displacement.magnitude;
                if (distance < 0.000001f) break;
                Trace trace = Tracer.TraceCollider(surfer.collider, s.origin, s.origin + displacement, SurfPhysics.groundLayerMask);
                if (trace.hitCollider == null) { s.origin += displacement; break; }
                if (s.grounded && s.useStepOffset && s.stepOffset > 0f &&
                    Vector3.Angle(Vector3.up, trace.planeNormal) > config.slopeLimit &&
                    TryStep(surfer, config, displacement)) break;
                float travelled = Mathf.Clamp(trace.distance - 0.002f, 0f, distance);
                s.origin += displacement * (travelled / distance);
                remaining *= 1f - travelled / distance;
                float into = Vector3.Dot(s.velocity, trace.planeNormal);
                if (into >= -0.00001f) break;
                s.velocity -= trace.planeNormal * into;
            }
        }

        private static bool TryStep(ISurfControllable surfer, MovementConfig config, Vector3 displacement) {
            MoveData s = surfer.moveData;
            Vector3 horizontal = Planar(displacement);
            if (horizontal.sqrMagnitude < 0.000001f) return false;
            Vector3 raised = s.origin + Vector3.up * s.stepOffset;
            if (Tracer.TraceCollider(surfer.collider, s.origin, raised, SurfPhysics.groundLayerMask).hitCollider != null)
                return false;
            Vector3 across = raised + horizontal;
            if (Tracer.TraceCollider(surfer.collider, raised, across, SurfPhysics.groundLayerMask).hitCollider != null)
                return false;
            Trace floor = Tracer.TraceCollider(surfer.collider, across,
                across - Vector3.up * (s.stepOffset + 0.035f), SurfPhysics.groundLayerMask);
            if (floor.hitCollider == null || Vector3.Angle(Vector3.up, floor.planeNormal) > config.slopeLimit)
                return false;
            Vector3 landing = across - Vector3.up * Mathf.Max(0f, floor.distance - 0.002f);
            if (landing.y <= s.origin.y + 0.001f || landing.y > s.origin.y + s.stepOffset + 0.003f)
                return false;
            s.origin = landing;
            s.velocity.y = 0f;
            return true;
        }

        public void Crouch(ISurfControllable surfer, MovementConfig config, float dt) {
            _surfer = surfer;
            MoveData s = surfer.moveData;
            float standing = s.defaultHeight;
            float ducked = standing * Mathf.Clamp(s.crouchingHeight, 0.01f, 1f);
            if (surfer.collider is CapsuleCollider cap) ducked = Mathf.Max(ducked, cap.radius * 2f);
            float difference = standing - ducked;
            bool wants = s.slideRequested;
            SetHeight(surfer.collider, s.crouching ? ducked : standing);
            if (wants != s.crouching) {
                bool clear = true;
                if (!wants && difference > 0f) {
                    Trace trace = Tracer.TraceCollider(surfer.collider, s.origin,
                        s.origin + Vector3.up * difference, SurfPhysics.groundLayerMask);
                    clear = trace.hitCollider == null;
                }
                if (clear) {
                    // Keep feet fixed, both grounded and airborne.
                    s.origin.y += (wants ? -1f : 1f) * difference * 0.5f;
                    s.crouching = wants;
                    SetHeight(surfer.collider, wants ? ducked : standing);
                }
            }
            s.uncrouchDown = false;
            s.crouchLerp = Mathf.MoveTowards(s.crouchLerp, s.crouching ? 1f : 0f, s.crouchingSpeed * dt);
            s.renderCrouchLerp = s.crouchLerp;
        }

        private static void SetHeight(Collider collider, float height) {
            if (collider is BoxCollider box) box.size = new Vector3(box.size.x, height, box.size.z);
            else if (collider is CapsuleCollider capsule) capsule.height = height;
        }
    }
}
