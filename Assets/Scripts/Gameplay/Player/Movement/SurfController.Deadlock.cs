using UnityEngine;

namespace Fragsurf.Movement {
    // Deadlock movement model. Rules follow Research/DeadlockMovement/BEHAVIOURS.md; IDs such as
    // B-GND-01 refer to its entries. Values come from MovementConfig.deadlock (metres and seconds).
    // Ground detection, collision and crouch stance are shared with the legacy model.
    public partial class SurfController {
        private const float DeadlockZeroSpeed = 0.1f * DeadlockMovementConfig.Unit;

        private void ProcessDeadlockMovement(ISurfControllable surfer, MovementConfig config, float dt) {
            MoveData s = surfer.moveData;
            DeadlockMovementConfig d = config.deadlock;

            s.dashTimer = Mathf.Max(0f, s.dashTimer - dt);
            s.dashCooldownTimer = Mathf.Max(0f, s.dashCooldownTimer - dt);
            s.slideDelay = Mathf.Max(0f, s.slideDelay - dt);
            s.dashSlideTimer = Mathf.Max(0f, s.dashSlideTimer - dt);
            s.slideGraceTimer = Mathf.Max(0f, s.slideGraceTimer - dt);
            s.isDashing = s.isDashing && s.dashTimer > 0f;
            if (!s.isDashing) s.airDashing = false;
            float regenTime = Mathf.Max(0f, dt - s.staminaRegenTimer);
            s.staminaRegenTimer = Mathf.Max(0f, s.staminaRegenTimer - dt);
            float regenRate = Mathf.Max(0f, d.staminaRegenRate + s.staminaRegenBonus);
            s.stamina = Mathf.MoveTowards(s.stamina, config.GetMaxStamina(s), regenRate * regenTime);
            s.jumpTimer += dt;

            bool wasGrounded = s.grounded;
            Vector3 normal = FindGround(surfer, config);
            // B-JMP-03: the late-jump window is refreshed by walkable ground.
            s.coyoteTimer = s.grounded ? Mathf.Max(0f, d.lateJumpWindow)
                : Mathf.Max(0f, Mathf.Min(s.coyoteTimer, Mathf.Max(0f, d.lateJumpWindow)) - dt);
            Quaternion yaw = Quaternion.Euler(0f, s.viewAngles.y, 0f);
            Vector3 wish = Vector3.ClampMagnitude(yaw * new Vector3(s.horizontalAxis, 0f, s.verticalAxis), 1f);
            float moveSpeed = Mathf.Max(0f, d.walkSpeed) * s.walkFactor;

            if (s.moveType == MoveType.Walk) {
                TryDeadlockDash(s, config, wish, yaw * Vector3.forward);
                DeadlockJumps(surfer, config, wish, moveSpeed);

                if (s.grounded) {
                    Vector3 planar = Planar(s.velocity);
                    float slopeAngle = Vector3.Angle(Vector3.up, normal);
                    Vector3 downhill = Planar(Vector3.ProjectOnPlane(Vector3.down, normal));
                    if (downhill.sqrMagnitude > 0.000001f) downhill.Normalize();
                    bool onSlope = slopeAngle >= d.slopeMinAngle && downhill.sqrMagnitude > 0f;
                    bool uphill = onSlope && Vector3.Dot(planar, downhill) < -0.001f;
                    if (s.airDashing) { s.isDashing = s.airDashing = false; s.dashTimer = 0f; }
                    if (UpdateDeadlockSlide(s, d, planar, onSlope && !uphill, landed: !wasGrounded))
                        DeadlockSlideEntrySpeed(s, d, ref planar);

                    if (s.isDashing && !s.airDashing) {
                        // B-DSH-01: a ground dash holds dash speed along its direction; no friction, no input.
                        planar = s.dashDirection * d.GroundDashSpeed;
                    } else {
                        if (s.sliding) {
                            float coefficient = DeadlockSlideFriction(s, d, onSlope, uphill, slopeAngle);
                            if (onSlope && !uphill) {
                                // B-SLD-04 (decoded): downhill acceleration up to a speed cap.
                                float t = Mathf.InverseLerp(d.slopeAngleRange.x, d.slopeAngleRange.y, slopeAngle);
                                float acceleration = Mathf.Lerp(d.slopeAcceleration.x, d.slopeAcceleration.y, t);
                                float cap = Mathf.Lerp(d.slopeMaxAccelerationSpeed.x, d.slopeMaxAccelerationSpeed.y, t);
                                float gain = Mathf.Min(Mathf.Max(cap - s.velocity.magnitude, 0f), acceleration);
                                planar += downhill * gain * dt;
                            }
                            DeadlockFriction(ref planar, d.friction * coefficient * s.surfaceFriction, d.stopSpeed, dt);
                        } else {
                            DeadlockFriction(ref planar, d.friction * s.surfaceFriction, d.stopSpeed, dt);
                        }
                        // B-GND-02: crouched (and sliding) wish speed is the crouch speed (B-GND-04).
                        float wishSpeed = (s.crouching || s.sliding ? d.crouchSpeed * s.walkFactor : moveSpeed);
                        DeadlockAccelerate(ref planar, wish, wishSpeed, d, s.surfaceFriction, dt);
                    }
                    s.velocity = planar;
                    if (normal.y > 0.001f)
                        s.velocity.y = -(normal.x * planar.x + normal.z * planar.z) / normal.y;
                } else {
                    LeaveSlide(s);
                    Vector3 planar = Planar(s.velocity);
                    if (s.isDashing && s.airDashing) {
                        DeadlockAirDash(s, d, ref planar, dt);
                    } else if (s.isDashing) {
                        // Ground dash carried off a ledge keeps its speed; gravity applies (not capture-tested).
                        planar = s.dashDirection * d.GroundDashSpeed;
                    } else {
                        // B-AIR-03 then B-AIR-02.
                        DeadlockFriction(ref planar, d.airDrag, d.stopSpeed, dt);
                        DeadlockAirAccelerate(ref planar, wish, moveSpeed, d, dt);
                    }
                    s.velocity.x = planar.x;
                    s.velocity.z = planar.z;
                }
            }

            // B-AIR-01: gravity is off during an air dash.
            if (!s.grounded && !(s.isDashing && s.airDashing))
                s.velocity.y -= d.gravity * s.gravityFactor * dt;
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
                character.SetMovementQueryDiagnostics($"model=deadlock, grounded={s.grounded}, normal={normal}, sliding={s.sliding}, velocity={s.velocity}");
        }

        // B-DSH-01 / B-DSH-02: dash direction is the input relative to view yaw, or forward.
        private static void TryDeadlockDash(MoveData s, MovementConfig config, Vector3 wish, Vector3 forward) {
            DeadlockMovementConfig d = config.deadlock;
            if (!s.wishDash || s.dashCooldownTimer > 0f || (!s.grounded && !s.canAirDash)) return;
            float cost = s.grounded ? d.groundDashCost : d.airDashCost;
            if (s.stamina < cost) return;
            Vector3 direction = wish.sqrMagnitude > 0.0001f ? wish.normalized : forward;
            s.dashDirection = Planar(direction).normalized;
            s.airDashing = !s.grounded;
            if (s.grounded) {
                // A ground dash discards existing velocity and moves at dash speed.
                LeaveSlide(s);
                s.velocity = s.dashDirection * d.GroundDashSpeed;
            } else {
                s.canAirDash = false;
            }
            s.isDashing = s.dashStartedThisFrame = true;
            s.dashTimer = s.currentDashDuration = s.grounded ? d.groundDashDuration : d.airDashDuration;
            s.dashCooldownTimer = config.dashCooldown;
            SpendStamina(s, cost, config);
        }

        private void DeadlockJumps(ISurfControllable surfer, MovementConfig config, Vector3 wish, float moveSpeed) {
            MoveData s = surfer.moveData;
            DeadlockMovementConfig d = config.deadlock;
            bool groundJump = s.grounded && (config.autoBhop ? s.wishJump : s.wishJumpDown);
            // B-JMP-03: a late jump needs a fresh press and only applies while not rising.
            bool lateJump = !s.grounded && s.coyoteTimer > 0f && s.jumpCount == 0 && s.wishJumpDown &&
                            s.velocity.y <= 0f;
            if (groundJump || lateJump) {
                // B-JMP-01: set vertical speed, keep horizontal velocity.
                LeaveSlide(s);
                s.velocity.y = d.jumpSpeed;
                s.jumpCount = 1;
                s.coyoteTimer = 0f;
                s.jumpTimer = 0f;
                s.jumping = true;
                s.grounded = false;
                surfer.groundObject = null;
                s.isDashing = s.airDashing = false;
                s.dashTimer = 0f;
                return;
            }
            if (!s.grounded && s.wishJumpDown && Mathf.Max(1, s.jumpCount) < config.maxJumps &&
                s.jumpTimer >= config.doubleJumpDelay && s.stamina >= d.airJumpCost) {
                // B-JMP-02: set vertical speed; add horizontal speed along input, capped.
                LeaveSlide(s);
                float magnitude = wish.magnitude;
                if (magnitude > 0.0001f) {
                    Vector3 direction = wish / magnitude;
                    float along = Vector3.Dot(Planar(s.velocity), direction);
                    float gain = Mathf.Min(moveSpeed - along, d.airJumpMaxSpeedDelta);
                    if (gain > 0f) s.velocity += direction * gain;
                }
                s.velocity.y = d.airJumpSpeed;
                s.jumpCount = Mathf.Max(1, s.jumpCount) + 1;
                s.coyoteTimer = 0f;
                s.jumpTimer = 0f;
                s.jumping = true;
                s.isDashing = s.airDashing = false;
                s.dashTimer = 0f;
                s.doubleJumpedThisFrame = true;
                SpendStamina(s, d.airJumpCost, config);
            }
        }

        // B-SLD-01 / B-SLD-02: start and stop, before this tick's friction. Returns true when a
        // slide starts out of a ground dash (a dash-slide).
        private static bool UpdateDeadlockSlide(MoveData s, DeadlockMovementConfig d, Vector3 planar,
                                                bool downhillSlope, bool landed) {
            bool wants = s.slidingEnabled && s.slideRequested && s.slideDelay <= 0f;
            float speed = planar.magnitude;
            if (s.sliding && (!wants || (!downhillSlope && speed < d.slideSustainSpeed))) {
                LeaveSlide(s);
                s.dashSlideTimer = s.slideGraceTimer = 0f;
                return false;
            }
            if (s.sliding || !wants) return false;
            bool groundDashing = s.isDashing && !s.airDashing;
            if (!downhillSlope && speed < d.slideActivationSpeed &&
                !(groundDashing && speed >= d.slideDashActivationSpeed)) return false;
            s.sliding = s.wasSliding = true;
            s.slideGraceTimer = landed ? d.slideLandedGraceTime : d.slideGraceTime;
            s.dashSlideTimer = 0f;
            return groundDashing;
        }

        // B-SLD-08: sliding out of a ground dash sets speed to the dash-slide speed (or the
        // fail speed when crouching too early), then the dash ends.
        private static void DeadlockSlideEntrySpeed(MoveData s, DeadlockMovementConfig d, ref Vector3 planar) {
            float elapsed = s.currentDashDuration - s.dashTimer;
            float target = elapsed >= d.dashSlideStartTime ? d.dashSlideSpeed : d.dashSlideFailSpeed;
            Vector3 direction = planar.sqrMagnitude > 0.000001f ? planar.normalized : s.dashDirection;
            planar = direction * target;
            s.dashSlideTimer = d.dashSlideFrictionTime;
            s.isDashing = s.airDashing = false;
            s.dashTimer = 0f;
        }

        // B-SLD-03: friction coefficient while sliding.
        private static float DeadlockSlideFriction(MoveData s, DeadlockMovementConfig d, bool onSlope, bool uphill, float angle) {
            if (onSlope) {
                float t = Mathf.InverseLerp(d.slopeAngleRange.x, d.slopeAngleRange.y, angle);
                // The flat-ground grace restarts after a slope.
                s.slideGraceTimer = Mathf.Max(s.slideGraceTimer, d.slideGraceTime);
                return uphill ? Mathf.Lerp(d.slopeUphillFriction.x, d.slopeUphillFriction.y, t)
                              : Mathf.Lerp(d.slopeFriction.x, d.slopeFriction.y, t);
            }
            if (s.dashSlideTimer > 0f) return d.dashSlideFriction;
            if (s.slideGraceTimer > 0f) return d.slideGraceFriction;
            return d.slideFriction;
        }

        // B-GND-01 / B-AIR-03: Source friction law on horizontal speed; zero below 0.1 u/s.
        private static void DeadlockFriction(ref Vector3 planar, float coefficient, float stopSpeed, float dt) {
            float speed = planar.magnitude;
            if (speed < DeadlockZeroSpeed) { planar = Vector3.zero; return; }
            float drop = Mathf.Max(speed, stopSpeed) * Mathf.Max(0f, coefficient) * dt;
            planar *= Mathf.Max(0f, speed - drop) / speed;
        }

        // B-GND-02: acceleration uses at least citadel_min_accel_speed as wish speed.
        private static void DeadlockAccelerate(ref Vector3 planar, Vector3 wish, float wishSpeed,
                                               DeadlockMovementConfig d, float surfaceFriction, float dt) {
            float magnitude = wish.magnitude;
            if (magnitude < 0.0001f) return;
            Vector3 direction = wish / magnitude;
            wishSpeed *= magnitude;
            float add = wishSpeed - Vector3.Dot(planar, direction);
            if (add <= 0f) return;
            float accelSpeed = Mathf.Max(wishSpeed, d.minAccelSpeed) * d.acceleration * surfaceFriction * dt;
            planar += direction * Mathf.Min(accelSpeed, add);
        }

        // B-AIR-02 (decoded): Source air acceleration with a wish-speed cap that falls with carried speed.
        private static void DeadlockAirAccelerate(ref Vector3 planar, Vector3 wish, float moveSpeed,
                                                  DeadlockMovementConfig d, float dt) {
            float magnitude = wish.magnitude;
            if (magnitude < 0.0001f) return;
            Vector3 direction = wish / magnitude;
            float blend = Mathf.InverseLerp(d.airControlBlendSpeeds.x, d.airControlBlendSpeeds.y, planar.magnitude);
            float cap = Mathf.Lerp(d.airControlSpeedSlow, d.airControlSpeedFast, blend);
            float wishSpeed = Mathf.Min(moveSpeed * magnitude, cap);
            float add = wishSpeed - Vector3.Dot(planar, direction);
            if (add <= 0f) return;
            planar += direction * Mathf.Min(add, d.airAccelerate * wishSpeed * dt);
        }

        // B-DSH-02: air acceleration toward the dash direction with a target speed that shrinks over
        // the dash. Prior momentum is kept. Vertical speed is held near zero; gravity is off.
        private static void DeadlockAirDash(MoveData s, DeadlockMovementConfig d, ref Vector3 planar, float dt) {
            float progress = s.currentDashDuration > 0f ? 1f - s.dashTimer / s.currentDashDuration : 1f;
            float wishSpeed = d.AirDashSpeed * Mathf.Lerp(1f, d.airDashEndVelocityScale, progress);
            // Air drag only acts above the dash's target speed.
            if (planar.magnitude > wishSpeed) DeadlockFriction(ref planar, d.airDrag, d.stopSpeed, dt);
            float add = wishSpeed - Vector3.Dot(planar, s.dashDirection);
            if (add > 0f)
                planar += s.dashDirection * Mathf.Min(add, d.airAccelerate * d.airDashAccelerationScale * wishSpeed * dt);
            s.velocity.y = Mathf.Max(s.velocity.y, d.airDashVerticalHold) * dt;
        }
    }
}
