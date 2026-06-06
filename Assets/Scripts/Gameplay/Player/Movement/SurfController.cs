using UnityEngine;
using Fragsurf.TraceUtil;
using System;
using System.Text;

namespace Fragsurf.Movement {
    public class SurfController {

        ///// Fields /////

        [HideInInspector] public Transform playerTransform;
        private ISurfControllable _surfer;
        private MovementConfig _config;
        private float _deltaTime;
        
        public Transform camera;
        public float cameraYPos = 0f;

        private float frictionMult = 1f;

        public bool IsSliding => _surfer != null ? _surfer.moveData.sliding : false;
        public Vector3 SlideDirection => _surfer != null ? _surfer.moveData.slideDirection : Vector3.forward;
        public bool jumping { get => _surfer != null ? _surfer.moveData.jumping : false; set { if (_surfer != null) _surfer.moveData.jumping = value; } }
        public bool crouching { get => _surfer != null ? _surfer.moveData.crouching : false; set { if (_surfer != null) _surfer.moveData.crouching = value; } }
        public float speed { get => _surfer != null ? _surfer.moveData.velocity.magnitude : 0f; set { } }

        ///// Methods /////

        Vector3 groundNormal = Vector3.up;
        private MovementQueryDiagnostics _queryDiagnostics;

        private struct MovementQueryDiagnostics {
            public bool groundTraceRan;
            public bool groundTraceHit;
            public Vector3 groundNormal;
            public float groundDistance;
            public bool groundedResult;
            public bool ladderCheckRan;
            public bool ladderCheckSkippedZeroVelocity;
            public bool ladderAnyHit;
            public bool ladderFound;
            public Vector3 ladderNormal;
            public float ladderHitDistance;
            public bool waterJumpTraceRan;
            public bool waterJumpTraceHit;
            public Vector3 waterJumpNormal;
            public float waterJumpDistance;
            public bool underwater;
            public bool cameraUnderwater;
        }

        private Quaternion GetLookYawRotation() {
            float yaw = (_surfer != null && _surfer.moveData != null)
                ? _surfer.moveData.viewAngles.y
                : 0f;
            return Quaternion.Euler(0f, yaw, 0f);
        }

        private Vector3 GetLookForward() {
            return GetLookYawRotation() * Vector3.forward;
        }

        private Vector3 GetLookRight() {
            return GetLookYawRotation() * Vector3.right;
        }

        /// <summary>
        /// 
        /// </summary>
        public void ProcessMovement (ISurfControllable surfer, MovementConfig config, float deltaTime) {
            
            // cache instead of passing around parameters
            _surfer = surfer;
            _config = config;
            _deltaTime = deltaTime;
            ResetMovementQueryDiagnostics();
            
            if (_surfer.moveData.laddersEnabled && !_surfer.moveData.climbingLadder) {

                // Look for ladders
                LadderCheck (new Vector3(1f, 0.95f, 1f), _surfer.moveData.velocity * Mathf.Clamp (_deltaTime * 2f, 0.025f, 0.25f));

            }
            
            if (_surfer.moveData.laddersEnabled && _surfer.moveData.climbingLadder) {
                
                LadderPhysics ();
                
            } else if (!_surfer.moveData.underwater) {

                if (_surfer.moveData.velocity.y <= 0f) {
                    _surfer.moveData.jumping = false;
                }

                // Stamina Regen
                if (_surfer.moveData.staminaRegenTimer > 0f) {
                    _surfer.moveData.staminaRegenTimer -= _deltaTime;
                } else {
                    _surfer.moveData.stamina = Mathf.MoveTowards(_surfer.moveData.stamina, _config.maxStamina, _config.staminaRegenRate * _deltaTime);
                }

                if (_surfer.groundObject == null) {
 
                    bool suspendGravity = (_surfer.moveData.isDashing && _surfer.moveData.dashTimer > 0f) || _surfer.moveData.meleeState == MoveData.MeleeState.Charging;
                    if (!suspendGravity) {
                        _surfer.moveData.velocity.y -= (_surfer.moveData.gravityFactor * _config.gravity * _deltaTime);
                        _surfer.moveData.velocity.y += _surfer.baseVelocity.y * _deltaTime;
                    }

                }

                // Jump Timer
                _surfer.moveData.jumpTimer += _deltaTime;
                
                // input velocity, check for ground
                CheckGrounded ();
                CalculateMovementVelocity ();
                
            } else {

                // Do underwater logic
                UnderwaterPhysics ();

            }

            float yVel = _surfer.moveData.velocity.y;
            _surfer.moveData.velocity.y = 0f;
            _surfer.moveData.velocity = Vector3.ClampMagnitude (_surfer.moveData.velocity, _config.maxVelocity);
            _surfer.moveData.velocity.y = yVel;
            
            if (_surfer.moveData.velocity.sqrMagnitude == 0f) {

                // Do collisions while standing still
                SurfPhysics.ResolveCollisions (_surfer.collider, ref _surfer.moveData.origin, ref _surfer.moveData.velocity, _surfer.moveData.rigidbodyPushForce, _deltaTime, 1f, _surfer.moveData.stepOffset, _surfer);

            } else {

                float maxDistPerFrame = 0.2f;
                Vector3 velocityThisFrame = _surfer.moveData.velocity * _deltaTime;
                float velocityDistLeft = velocityThisFrame.magnitude;
                float initialVel = velocityDistLeft;
                
                // Debug.Log($"[SurfController] Move Start. Vel: {_surfer.moveData.velocity}, DistLeft: {velocityDistLeft}, Origin: {_surfer.moveData.origin}");

                while (velocityDistLeft > 0f) {

                    float amountThisLoop = Mathf.Min (maxDistPerFrame, velocityDistLeft);
                    velocityDistLeft -= amountThisLoop;

                    // increment origin
                    Vector3 velThisLoop = velocityThisFrame * (amountThisLoop / initialVel);
                    _surfer.moveData.origin += velThisLoop;

                    // don't penetrate walls
                    SurfPhysics.ResolveCollisions (_surfer.collider, ref _surfer.moveData.origin, ref _surfer.moveData.velocity, _surfer.moveData.rigidbodyPushForce, _deltaTime, amountThisLoop / initialVel, _surfer.moveData.stepOffset, _surfer);
                }
                
                // Debug.Log($"[SurfController] Move End. Origin: {_surfer.moveData.origin}");

            }

            _surfer.moveData.groundedTemp = _surfer.moveData.grounded;
            StoreMovementQueryDiagnostics();

            _surfer = null;
            
        }

        /// <summary>
        /// 
        /// </summary>
        private void CalculateMovementVelocity () {
            switch (_surfer.moveType) {

                case MoveType.HeavyMelee:
                    if (_surfer.moveData.meleeState == MoveData.MeleeState.Charging) {
                         // Apply specific friction to allow sliding
                         ApplyFriction(1.0f, true, _surfer.moveData.grounded, _config.heavyMeleeChargeFriction);
                    }
                    else if (_surfer.moveData.meleeState == MoveData.MeleeState.Lunging) {
                         // No friction, just momentum
                         // ResolveCollisions is handled at end of ProcessMovement
                    }
                    else if (_surfer.moveData.meleeState == MoveData.MeleeState.Recovery) {
                         // High Drag on Ground, less in Air
                         if (_surfer.moveData.grounded)
                             ApplyFriction(1.0f, true, true, _config.heavyMeleeRecoveryDrag);
                         else {
                             // Simple air dampen
                             _surfer.moveData.velocity = Vector3.MoveTowards(_surfer.moveData.velocity, Vector3.zero, _deltaTime * 10f);
                         }
                    }
                    break;

                case MoveType.Walk:

                if (_surfer.groundObject == null) {

                    /*
                    // AIR MOVEMENT
                    */

                    _surfer.moveData.wasSliding = false;

                    // AIR DASHING
                    if (_surfer.moveData.dashCooldownTimer > 0f)
                        _surfer.moveData.dashCooldownTimer -= _deltaTime;

                    if (_surfer.moveData.isDashing && _surfer.moveData.dashTimer > 0f) {
                        _surfer.moveData.dashTimer -= _deltaTime;
                        if (_surfer.moveData.dashTimer <= 0f)
                            _surfer.moveData.isDashing = false;
                    }

                    if (_surfer.moveData.wishDash && _surfer.moveData.canAirDash && _surfer.moveData.stamina >= _config.airDashCost && _surfer.moveData.dashCooldownTimer <= 0f) {
                        
                        // Consume stamina
                        _surfer.moveData.stamina -= _config.airDashCost;
                        _surfer.moveData.staminaRegenTimer = _config.staminaRegenDelay;
                        
                        // Dash logic
                        _surfer.moveData.isDashing = true;
                        _surfer.moveData.dashStartedThisFrame = true;
                        _surfer.moveData.dashTimer = _config.airDashDuration;
                        _surfer.moveData.currentDashDuration = _config.airDashDuration;
                        _surfer.moveData.dashCooldownTimer = _config.dashCooldown;
                        _surfer.moveData.canAirDash = false; // Limit to one per air instance
                        
                        // Set velocity (snappy redirect)
                        Vector3 lookForward = GetLookForward();
                        Vector3 lookRight = GetLookRight();
                        Vector3 dashDir = (_surfer.moveData.verticalAxis * lookForward + _surfer.moveData.horizontalAxis * lookRight).normalized;
                        if (dashDir.sqrMagnitude == 0) dashDir = lookForward;
                        
                        _surfer.moveData.velocity = dashDir * _config.airDashVelocity;
                    }

                    // apply movement from input
                    _surfer.moveData.velocity += AirInputMovement ();

                    // Air Jump
                    if (_surfer.moveData.wishJumpDown
                        && _surfer.moveData.jumpCount > 0
                        && _surfer.moveData.jumpCount < _config.maxJumps
                        && _surfer.moveData.jumpTimer >= _config.doubleJumpDelay
                        && _surfer.moveData.stamina >= _config.doubleJumpCost) {
                        DoubleJump ();
                        _surfer.moveData.doubleJumpedThisFrame = true;
                    }

                    // let the magic happen
                    SurfPhysics.Reflect (ref _surfer.moveData.velocity, _surfer.collider, _surfer.moveData.origin, _deltaTime);



                } else {

                    /*
                    //  GROUND MOVEMENT
                    */

                    _surfer.moveData.sliding = false;

                    // Check if on a slope
                    bool onSlope = Vector3.Angle (Vector3.up, groundNormal) > 5f;

                    // Sliding
                    if (!_surfer.moveData.wasSliding) {

                        if (onSlope && _surfer.moveData.velocity.sqrMagnitude < 0.1f) {
                             // Kickstart from slope
                             _surfer.moveData.slideDirection = new Vector3(groundNormal.x, 0f, groundNormal.z).normalized;
                             _surfer.moveData.slideSpeedCurrent = 0f; // Start from zero
                        } else {
                             _surfer.moveData.slideDirection = new Vector3 (_surfer.moveData.velocity.x, 0f, _surfer.moveData.velocity.z).normalized;
                             _surfer.moveData.slideSpeedCurrent = new Vector3 (_surfer.moveData.velocity.x, 0f, _surfer.moveData.velocity.z).magnitude;
                        }

                    }

                    if ((_surfer.moveData.velocity.magnitude > _config.minimumSlideSpeed || onSlope ) && _surfer.moveData.slidingEnabled && _surfer.moveData.slideRequested && _surfer.moveData.slideDelay <= 0f) {

                        if (!_surfer.moveData.wasSliding) {
                            float dashBonus = _surfer.moveData.isDashing ? 1.5f : 1f; // Bonus speed if starting slide during dash
                            _surfer.moveData.slideSpeedCurrent = Mathf.Clamp (_surfer.moveData.slideSpeedCurrent * _config.slideSpeedMultiplier * dashBonus, _config.minimumSlideSpeed, _config.maximumSlideSpeed * dashBonus);
                            if (_surfer.moveData.slideSpeedCurrent == 0f && onSlope) _surfer.moveData.slideSpeedCurrent = 1f; // Small nudge to prevent clamp issues if logic relies on >0
                        }

                        _surfer.moveData.sliding = true;
                        _surfer.moveData.wasSliding = true;
                        SlideMovement ();
                        return;

                    } else {

                        if (_surfer.moveData.slideDelay > 0f)
                            _surfer.moveData.slideDelay -= _deltaTime;

                        if (_surfer.moveData.wasSliding)
                            _surfer.moveData.slideDelay = _config.slideDelay;

                        _surfer.moveData.wasSliding = false;

                    }
                    
                    float fric = crouching ? _config.crouchFriction : _config.friction;
                    float accel = crouching ? _config.crouchAcceleration : _config.acceleration;
                    float decel = crouching ? _config.crouchDeceleration : _config.deceleration;
                    
                    // Get movement directions
                    Vector3 lookRight = GetLookRight();
                    Vector3 forward = Vector3.Cross (groundNormal, -lookRight);
                    Vector3 right = Vector3.Cross (groundNormal, forward);

                    float speed = _config.walkSpeed;
                    if (crouching)
                        speed = _config.crouchSpeed;

                    Vector3 _wishDir;

                    // Jump and friction
                    if (_surfer.moveData.wishJump) {

                        ApplyFriction (0.0f, true, true);
                        Jump ();
                        return;

                    } else {

                        ApplyFriction (1.0f * frictionMult, true, true);

                    }

                    float forwardMove = _surfer.moveData.verticalAxis;
                    float rightMove = _surfer.moveData.horizontalAxis;

                    _wishDir = forwardMove * forward + rightMove * right;
                    _wishDir.Normalize ();
                    Vector3 moveDirNorm = _wishDir;

                    Vector3 forwardVelocity = Vector3.Cross (groundNormal, Quaternion.AngleAxis (-90, Vector3.up) * new Vector3 (_surfer.moveData.velocity.x, 0f, _surfer.moveData.velocity.z));

                    // Set the target speed of the player
                    float _wishSpeed = _wishDir.magnitude;
                    _wishSpeed *= speed;

                    // Accelerate
                    float yVel = _surfer.moveData.velocity.y;
                    Accelerate (_wishDir, _wishSpeed, accel * Mathf.Min (frictionMult, 1f), false);

                    float maxVelocityMagnitude = _config.maxVelocity;
                    _surfer.moveData.velocity = Vector3.ClampMagnitude (new Vector3 (_surfer.moveData.velocity.x, 0f, _surfer.moveData.velocity.z), maxVelocityMagnitude);
                    _surfer.moveData.velocity.y = yVel;

                    // Calculate how much slopes should affect movement
                    float yVelocityNew = forwardVelocity.normalized.y * new Vector3 (_surfer.moveData.velocity.x, 0f, _surfer.moveData.velocity.z).magnitude;

                    // Apply the Y-movement from slopes
                    _surfer.moveData.velocity.y = yVelocityNew * (_wishDir.y < 0f ? 1.2f : 1.0f);
                    float removableYVelocity = _surfer.moveData.velocity.y - yVelocityNew;

                    // DASHING
                    if (_surfer.moveData.dashCooldownTimer > 0f)
                        _surfer.moveData.dashCooldownTimer -= _deltaTime;

                    if (_surfer.moveData.dashTimer > 0f) {
                        _surfer.moveData.dashTimer -= _deltaTime;
                        if (_surfer.moveData.dashTimer <= 0f)
                            _surfer.moveData.isDashing = false;
                    }

                    if (_surfer.moveData.wishDash && _surfer.moveData.stamina >= _config.dashCost && _surfer.moveData.dashCooldownTimer <= 0f) {
                        
                        // Consume stamina
                        _surfer.moveData.stamina -= _config.dashCost;
                        _surfer.moveData.staminaRegenTimer = _config.staminaRegenDelay;
                        
                        // Dash logic
                        _surfer.moveData.isDashing = true;
                        _surfer.moveData.dashStartedThisFrame = true;
                        _surfer.moveData.dashTimer = _config.dashDuration;
                        _surfer.moveData.currentDashDuration = _config.dashDuration;
                        _surfer.moveData.dashCooldownTimer = _config.dashCooldown;
                        
                        // Apply impulse
                        Vector3 dashDir = (forwardMove * forward + rightMove * right).normalized;
                        if (dashDir.sqrMagnitude == 0) dashDir = forward; // Dash forward if no input
                        
                        _surfer.moveData.velocity += dashDir * _config.dashImpulse;
                    }

                }

                break;

            } // END OF SWITCH STATEMENT
        }

        private void UnderwaterPhysics () {
            _queryDiagnostics.underwater = _surfer.moveData.underwater;
            _queryDiagnostics.cameraUnderwater = _surfer.moveData.cameraUnderwater;

            _surfer.moveData.velocity = Vector3.Lerp (_surfer.moveData.velocity, Vector3.zero, _config.underwaterVelocityDampening * _deltaTime);

            // Gravity
            if (!CheckGrounded ())
                _surfer.moveData.velocity.y -= _config.underwaterGravity * _deltaTime;

            // Swimming upwards
            if (_surfer.moveData.wishJump)
                _surfer.moveData.velocity.y += _config.swimUpSpeed * _deltaTime;

            float fric = _config.underwaterFriction;
            float accel = _config.underwaterAcceleration;
            float decel = _config.underwaterDeceleration;

            ApplyFriction (1f, true, false);

            // Get movement directions
            Vector3 lookForward = GetLookForward();
            Vector3 lookRight = GetLookRight();
            Vector3 forward = Vector3.Cross (groundNormal, -lookRight);
            Vector3 right = Vector3.Cross (groundNormal, forward);

            float speed = _config.underwaterSwimSpeed;

            Vector3 _wishDir;

            float forwardMove = _surfer.moveData.verticalAxis;
            float rightMove = _surfer.moveData.horizontalAxis;

            _wishDir = forwardMove * forward + rightMove * right;
            _wishDir.Normalize ();
            Vector3 moveDirNorm = _wishDir;

            Vector3 forwardVelocity = Vector3.Cross (groundNormal, Quaternion.AngleAxis (-90, Vector3.up) * new Vector3 (_surfer.moveData.velocity.x, 0f, _surfer.moveData.velocity.z));

            // Set the target speed of the player
            float _wishSpeed = _wishDir.magnitude;
            _wishSpeed *= speed;

            // Accelerate
            float yVel = _surfer.moveData.velocity.y;
            Accelerate (_wishDir, _wishSpeed, accel, false);

            float maxVelocityMagnitude = _config.maxVelocity;
            _surfer.moveData.velocity = Vector3.ClampMagnitude (new Vector3 (_surfer.moveData.velocity.x, 0f, _surfer.moveData.velocity.z), maxVelocityMagnitude);
            _surfer.moveData.velocity.y = yVel;

            float yVelStored = _surfer.moveData.velocity.y;
            _surfer.moveData.velocity.y = 0f;

            // Calculate how much slopes should affect movement
            float yVelocityNew = forwardVelocity.normalized.y * new Vector3 (_surfer.moveData.velocity.x, 0f, _surfer.moveData.velocity.z).magnitude;

            // Apply the Y-movement from slopes
            _surfer.moveData.velocity.y = Mathf.Min (Mathf.Max (0f, yVelocityNew) + yVelStored, speed);

            // Jumping out of water
            bool movingForwards = Vector3.Dot(_surfer.moveData.velocity, lookForward) > 0f;
            Vector3 traceOrigin = _surfer.moveData.origin;
            Trace waterJumpTrace = TraceBounds (traceOrigin, traceOrigin + lookForward * 0.1f, SurfPhysics.groundLayerMask);
            _queryDiagnostics.waterJumpTraceRan = true;
            _queryDiagnostics.waterJumpTraceHit = waterJumpTrace.hitCollider != null;
            _queryDiagnostics.waterJumpNormal = waterJumpTrace.planeNormal;
            _queryDiagnostics.waterJumpDistance = waterJumpTrace.distance;
            if (waterJumpTrace.hitCollider != null && Vector3.Angle (Vector3.up, waterJumpTrace.planeNormal) >= _config.slopeLimit && _surfer.moveData.wishJump && !_surfer.moveData.cameraUnderwater && movingForwards)
                _surfer.moveData.velocity.y = Mathf.Max (_surfer.moveData.velocity.y, _config.jumpForce);

        }
        
        private void LadderCheck (Vector3 colliderScale, Vector3 direction) {
            _queryDiagnostics.ladderCheckRan = true;
            _queryDiagnostics.ladderCheckSkippedZeroVelocity = false;
            _queryDiagnostics.ladderAnyHit = false;
            _queryDiagnostics.ladderFound = false;

            if (_surfer.moveData.velocity.sqrMagnitude <= 0f) {
                _queryDiagnostics.ladderCheckSkippedZeroVelocity = true;
                return;
            }
            
            bool foundLadder = false;

            RaycastHit [] hits = Physics.BoxCastAll (_surfer.moveData.origin, Vector3.Scale (_surfer.collider.bounds.size * 0.5f, colliderScale), Vector3.Scale (direction, new Vector3 (1f, 0f, 1f)), Quaternion.identity, direction.magnitude, SurfPhysics.groundLayerMask, QueryTriggerInteraction.Collide);
            _queryDiagnostics.ladderAnyHit = hits != null && hits.Length > 0;
            foreach (RaycastHit hit in hits) {

                Ladder ladder = hit.transform.GetComponentInParent<Ladder> ();
                if (ladder != null) {

                    bool allowClimb = true;
                    float ladderAngle = Vector3.Angle (Vector3.up, hit.normal);
                    if (_surfer.moveData.angledLaddersEnabled) {

                        if (hit.normal.y < 0f)
                            allowClimb = false;
                        else {
                            
                            if (ladderAngle <= _surfer.moveData.slopeLimit)
                                allowClimb = false;

                        }

                    } else if (hit.normal.y != 0f)
                        allowClimb = false;

                    if (allowClimb) {
                        foundLadder = true;
                        _queryDiagnostics.ladderFound = true;
                        _queryDiagnostics.ladderNormal = hit.normal;
                        _queryDiagnostics.ladderHitDistance = hit.distance;
                        if (_surfer.moveData.climbingLadder == false) {

                            _surfer.moveData.climbingLadder = true;
                            _surfer.moveData.ladderNormal = hit.normal;
                            _surfer.moveData.ladderDirection = -hit.normal * direction.magnitude * 2f;

                            if (_surfer.moveData.angledLaddersEnabled) {

                                Vector3 sideDir = hit.normal;
                                sideDir.y = 0f;
                                sideDir = Quaternion.AngleAxis (-90f, Vector3.up) * sideDir;

                                _surfer.moveData.ladderClimbDir = Quaternion.AngleAxis (90f, sideDir) * hit.normal;
                                _surfer.moveData.ladderClimbDir *= 1f/ _surfer.moveData.ladderClimbDir.y; // Make sure Y is always 1

                            } else
                                _surfer.moveData.ladderClimbDir = Vector3.up;
                            
                        }
                        
                    }

                }

            }

            if (!foundLadder) {
                
                _surfer.moveData.ladderNormal = Vector3.zero;
                _surfer.moveData.ladderVelocity = Vector3.zero;
                _surfer.moveData.climbingLadder = false;
                _surfer.moveData.ladderClimbDir = Vector3.up;

            }

        }

        private void LadderPhysics () {
            
            _surfer.moveData.ladderVelocity = _surfer.moveData.ladderClimbDir * _surfer.moveData.verticalAxis * 6f;

            _surfer.moveData.velocity = Vector3.Lerp (_surfer.moveData.velocity, _surfer.moveData.ladderVelocity, _deltaTime * 10f);

            LadderCheck (Vector3.one, _surfer.moveData.ladderDirection);
            
            Trace floorTrace = TraceToFloor ();
            if (_surfer.moveData.verticalAxis < 0f && floorTrace.hitCollider != null && Vector3.Angle (Vector3.up, floorTrace.planeNormal) <= _surfer.moveData.slopeLimit) {

                _surfer.moveData.velocity = _surfer.moveData.ladderNormal * 0.5f;
                _surfer.moveData.ladderVelocity = Vector3.zero;
                _surfer.moveData.climbingLadder = false;

            }

            if (_surfer.moveData.wishJump) {

                _surfer.moveData.velocity = _surfer.moveData.ladderNormal * 4f;
                _surfer.moveData.ladderVelocity = Vector3.zero;
                _surfer.moveData.climbingLadder = false;
                
            }
            
        }
        
        private void Accelerate (Vector3 wishDir, float wishSpeed, float acceleration, bool yMovement) {

            // Initialise variables
            float _addSpeed;
            float _accelerationSpeed;
            float _currentSpeed;
            
            // again, no idea
            _currentSpeed = Vector3.Dot (_surfer.moveData.velocity, wishDir);
            _addSpeed = wishSpeed - _currentSpeed;

            // If you're not actually increasing your speed, stop here.
            if (_addSpeed <= 0)
                return;

            // won't bother trying to understand any of this, really
            _accelerationSpeed = Mathf.Min (acceleration * _deltaTime * wishSpeed, _addSpeed);

            // Add the velocity.
            _surfer.moveData.velocity.x += _accelerationSpeed * wishDir.x;
            if (yMovement) { _surfer.moveData.velocity.y += _accelerationSpeed * wishDir.y; }
            _surfer.moveData.velocity.z += _accelerationSpeed * wishDir.z;

        }

        private void ApplyFriction (float t, bool yAffected, bool grounded, float frictionOverride = -1f) {

            // Initialise variables
            Vector3 _vel = _surfer.moveData.velocity;
            float _speed;
            float _newSpeed;
            float _control;
            float _drop;

            // Set Y to 0, speed to the magnitude of movement and drop to 0. I think drop is the amount of speed that is lost, but I just stole this from the internet, idk.
            _vel.y = 0.0f;
            _speed = _vel.magnitude;
            _drop = 0.0f;

            float fric = crouching ? _config.crouchFriction : _config.friction;
            if (frictionOverride >= 0f) fric = frictionOverride;

            float accel = crouching ? _config.crouchAcceleration : _config.acceleration;
            float decel = crouching ? _config.crouchDeceleration : _config.deceleration;

            // Only apply friction if the player is grounded
            if (grounded) {
                
                _control = _speed < decel ? decel : _speed;

                // Apply friction
                float finalFriction = _surfer.moveData.isDashing ? fric * _config.dashFrictionOverride : fric;
                _drop = _control * finalFriction * _deltaTime * t;

            }

            // again, no idea, but comments look cool
            _newSpeed = Mathf.Max (_speed - _drop, 0f);
            if (_speed > 0.0f)
                _newSpeed /= _speed;

            // Set the end-velocity
            _surfer.moveData.velocity.x *= _newSpeed;
            if (yAffected == true) { _surfer.moveData.velocity.y *= _newSpeed; }
            _surfer.moveData.velocity.z *= _newSpeed;

        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private Vector3 AirInputMovement () {

            Vector3 wishVel, wishDir;
            float wishSpeed;

            GetWishValues (out wishVel, out wishDir, out wishSpeed);

            if (_config.clampAirSpeed && (wishSpeed != 0f && (wishSpeed > _config.maxSpeed))) {

                wishVel = wishVel * (_config.maxSpeed / wishSpeed);
                wishSpeed = _config.maxSpeed;

            }

            return SurfPhysics.AirAccelerate (_surfer.moveData.velocity, wishDir, wishSpeed, _config.airAcceleration, _config.airCap, _deltaTime);

        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="wishVel"></param>
        /// <param name="wishDir"></param>
        /// <param name="wishSpeed"></param>
        private void GetWishValues (out Vector3 wishVel, out Vector3 wishDir, out float wishSpeed) {

            wishVel = Vector3.zero;
            wishDir = Vector3.zero;
            wishSpeed = 0f;

            // IMPORTANT FOR NETWORK DETERMINISM:
            // Use moveData.viewAngles (input-driven/replicated) rather than live transform
            // orientation, which may differ between client render frames and server tick frames.
            Vector3 forward = GetLookForward();
            Vector3 right = GetLookRight();

            forward [1] = 0;
            right [1] = 0;
            forward.Normalize ();
            right.Normalize ();

            for (int i = 0; i < 3; i++)
                wishVel [i] = forward [i] * _surfer.moveData.forwardMove + right [i] * _surfer.moveData.sideMove;
            wishVel [1] = 0;

            wishSpeed = wishVel.magnitude;
            wishDir = wishVel.normalized;

        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="velocity"></param>
        /// <param name="jumpPower"></param>
        /// <summary>
        /// Applies the initial ground jump.
        /// </summary>
        /// <summary>
        /// Applies the initial ground jump.
        /// </summary>
        private void Jump () {
            
            if (!_config.autoBhop)
                _surfer.moveData.wishJump = false;
            
            _surfer.moveData.velocity.y += _config.jumpForce;
            _surfer.moveData.jumpCount = 1;
            _surfer.moveData.jumpTimer = 0f;
            _surfer.moveData.jumping = true;

        }

        /// <summary>
        /// Applies the double jump (mid-air).
        /// </summary>
        private void DoubleJump () {
            
            // Consume input
            _surfer.moveData.wishJumpDown = false;

            // Double Jump
            _surfer.moveData.stamina -= _config.doubleJumpCost;
            _surfer.moveData.staminaRegenTimer = _config.staminaRegenDelay;
            _surfer.moveData.jumpCount++;
            _surfer.moveData.jumpTimer = 0f;
            
            // Vertical Reset
            _surfer.moveData.velocity.y = _config.doubleJumpForce;
            _surfer.moveData.jumping = true;

        }

        /// <summary>
        /// 
        /// </summary>
        private bool CheckGrounded () {

            _surfer.moveData.surfaceFriction = 1f;
            var movingUp = _surfer.moveData.velocity.y > 0f;
            var trace = TraceToFloor ();
            _queryDiagnostics.groundTraceRan = true;
            _queryDiagnostics.groundTraceHit = trace.hitCollider != null;
            _queryDiagnostics.groundNormal = trace.planeNormal;
            _queryDiagnostics.groundDistance = trace.distance;

            float groundSteepness = Vector3.Angle (Vector3.up, trace.planeNormal);

            if (trace.hitCollider == null || groundSteepness > _config.slopeLimit || (jumping && _surfer.moveData.velocity.y > 0f)) {
                _queryDiagnostics.groundedResult = false;

                SetGround (null);

                if (movingUp && _surfer.moveType != MoveType.Noclip)
                    _surfer.moveData.surfaceFriction = _config.airFriction;
                
                return false;

            } else {

                // Snap to ground
                if (_config.useGroundSnapping && !jumping) {
                    // Only snap if we aren't moving up (jumping handled above, but double check velocity)
                    // And check if the distance is small but checking logic already implies we hit something within range
                    if (_surfer.moveData.velocity.y <= 0f) {
                        if (trace.distance > 0.01f) {
                            _surfer.moveData.origin += Vector3.down * trace.distance;
                        }
                    }
                }

                // Reset air resources
                _surfer.moveData.canAirDash = true;
                _surfer.moveData.jumpCount = 0;

                groundNormal = trace.planeNormal;
                SetGround (trace.hitCollider.gameObject);
                _queryDiagnostics.groundedResult = true;
                return true;

            }

        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="obj"></param>
        private void SetGround (GameObject obj) {

            if (obj != null) {

                _surfer.groundObject = obj;
                _surfer.moveData.velocity.y = 0;
                _surfer.moveData.grounded = true;

            } else {
                _surfer.groundObject = null;
                _surfer.moveData.grounded = false;
            }

        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="start"></param>
        /// <param name="end"></param>
        /// <param name="layerMask"></param>
        /// <returns></returns>
        private Trace TraceBounds (Vector3 start, Vector3 end, int layerMask) {

            return Tracer.TraceCollider (_surfer.collider, start, end, layerMask);

        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private Trace TraceToFloor () {

            var down = _surfer.moveData.origin;
            down.y -= _config.groundSnapDistance;

            return Tracer.TraceCollider (_surfer.collider, _surfer.moveData.origin, down, SurfPhysics.groundLayerMask);

        }

        private void ResetMovementQueryDiagnostics() {
            _queryDiagnostics = new MovementQueryDiagnostics {
                underwater = _surfer != null && _surfer.moveData != null && _surfer.moveData.underwater,
                cameraUnderwater = _surfer != null && _surfer.moveData != null && _surfer.moveData.cameraUnderwater
            };
        }

        private void StoreMovementQueryDiagnostics() {
            if (!(_surfer is SurfCharacter surfCharacter))
                return;

            _queryDiagnostics.underwater = _surfer.moveData.underwater;
            _queryDiagnostics.cameraUnderwater = _surfer.moveData.cameraUnderwater;
            surfCharacter.SetMovementQueryDiagnostics(BuildMovementQueryDiagnosticsSummary());
        }

        private string BuildMovementQueryDiagnosticsSummary() {
            StringBuilder sb = new StringBuilder(256);
            sb.AppendLine($"movement queries: underwater={_queryDiagnostics.underwater}, cameraUnderwater={_queryDiagnostics.cameraUnderwater}");
            sb.AppendLine(_queryDiagnostics.groundTraceRan
                ? $"ground trace: hit={_queryDiagnostics.groundTraceHit}, normal={FormatVector3(_queryDiagnostics.groundNormal)}, distance={_queryDiagnostics.groundDistance:F4}, groundedResult={_queryDiagnostics.groundedResult}"
                : "ground trace: not-run");
            sb.AppendLine(_queryDiagnostics.ladderCheckRan
                ? $"ladder trace: anyHit={_queryDiagnostics.ladderAnyHit}, ladderHit={_queryDiagnostics.ladderFound}, skippedZeroVelocity={_queryDiagnostics.ladderCheckSkippedZeroVelocity}, normal={FormatVector3(_queryDiagnostics.ladderNormal)}, distance={_queryDiagnostics.ladderHitDistance:F4}"
                : "ladder trace: not-run");
            sb.Append(_queryDiagnostics.waterJumpTraceRan
                ? $"water jump trace: hit={_queryDiagnostics.waterJumpTraceHit}, normal={FormatVector3(_queryDiagnostics.waterJumpNormal)}, distance={_queryDiagnostics.waterJumpDistance:F4}"
                : "water jump trace: not-run");
            return sb.ToString();
        }

        private static string FormatVector3(Vector3 value) {
            return $"({value.x:F4}, {value.y:F4}, {value.z:F4})";
        }

        public void Crouch (ISurfControllable surfer, MovementConfig config, float deltaTime) {

            _surfer = surfer;
            _config = config;
            _deltaTime = deltaTime;

            if (_surfer == null)
                return;

            if (_surfer.collider == null)
                return;

            bool grounded = _surfer.groundObject != null;
            bool wantsToCrouch = _surfer.moveData.crouching;

            float crouchingHeight = Mathf.Clamp (_surfer.moveData.crouchingHeight, 0.01f, 1f);
            float heightDifference = _surfer.moveData.defaultHeight - _surfer.moveData.defaultHeight * crouchingHeight;

            if (grounded)
                _surfer.moveData.uncrouchDown = false;

            // Crouching input
            if (grounded)
                _surfer.moveData.crouchLerp = Mathf.Lerp (_surfer.moveData.crouchLerp, wantsToCrouch ? 1f : 0f, _deltaTime * _surfer.moveData.crouchingSpeed);
            else if (!grounded && !wantsToCrouch && _surfer.moveData.crouchLerp < 0.95f)
                _surfer.moveData.crouchLerp = 0f;
            else if (!grounded && wantsToCrouch)
                _surfer.moveData.crouchLerp = 1f;

            // Collider and gameplay position changes stay in simulation.
            // Presentation-only child/camera offsets are applied in SurfCharacter.Update().
            if (_surfer.moveData.crouchLerp > 0.9f && !_surfer.moveData.crouching) {
                
                // Begin crouching
                _surfer.moveData.crouching = true;
                if (_surfer.collider.GetType () == typeof (BoxCollider)) {

                    // Box collider
                    BoxCollider boxCollider = (BoxCollider)_surfer.collider;
                    boxCollider.size = new Vector3 (boxCollider.size.x, _surfer.moveData.defaultHeight * crouchingHeight, boxCollider.size.z);

                } else if (_surfer.collider.GetType () == typeof (CapsuleCollider)) {

                    // Capsule collider
                    CapsuleCollider capsuleCollider = (CapsuleCollider)_surfer.collider;
                    capsuleCollider.height = _surfer.moveData.defaultHeight * crouchingHeight;

                }

                // Move gameplay origin and collider only.
                _surfer.moveData.origin += heightDifference / 2 * (grounded ? Vector3.down : Vector3.up);

                _surfer.moveData.uncrouchDown = !grounded;

            } else if (_surfer.moveData.crouching) {

                // Check if the player can uncrouch
                bool canUncrouch = true;
                if (_surfer.collider.GetType () == typeof (BoxCollider)) {

                    // Box collider
                    BoxCollider boxCollider = (BoxCollider)_surfer.collider;
                    Vector3 halfExtents = boxCollider.size * 0.5f;
                    Vector3 startPos = boxCollider.transform.position;
                    Vector3 endPos = boxCollider.transform.position + (_surfer.moveData.uncrouchDown ? Vector3.down : Vector3.up) * heightDifference;

                    Trace trace = Tracer.TraceBox (startPos, endPos, halfExtents, boxCollider.contactOffset, SurfPhysics.groundLayerMask);

                    if (trace.hitCollider != null)
                        canUncrouch = false;

                } else if (_surfer.collider.GetType () == typeof (CapsuleCollider)) {

                    // Capsule collider
                    CapsuleCollider capsuleCollider = (CapsuleCollider)_surfer.collider;
                    Vector3 point1 = capsuleCollider.center + Vector3.up * capsuleCollider.height * 0.5f;
                    Vector3 point2 = capsuleCollider.center + Vector3.down * capsuleCollider.height * 0.5f;
                    Vector3 startPos = capsuleCollider.transform.position;
                    Vector3 endPos = capsuleCollider.transform.position + (_surfer.moveData.uncrouchDown ? Vector3.down : Vector3.up) * heightDifference;

                    Trace trace = Tracer.TraceCapsule (point1, point2, capsuleCollider.radius, startPos, endPos, capsuleCollider.contactOffset, SurfPhysics.groundLayerMask);

                    if (trace.hitCollider != null)
                        canUncrouch = false;

                }

                // Uncrouch
                if (canUncrouch && _surfer.moveData.crouchLerp <= 0.9f) {

                    _surfer.moveData.crouching = false;
                    if (_surfer.collider.GetType () == typeof (BoxCollider)) {

                        // Box collider
                        BoxCollider boxCollider = (BoxCollider)_surfer.collider;
                        boxCollider.size = new Vector3 (boxCollider.size.x, _surfer.moveData.defaultHeight, boxCollider.size.z);

                    } else if (_surfer.collider.GetType () == typeof (CapsuleCollider)) {

                        // Capsule collider
                        CapsuleCollider capsuleCollider = (CapsuleCollider)_surfer.collider;
                        capsuleCollider.height = _surfer.moveData.defaultHeight;

                    }

                    // Move gameplay origin and collider only.
                    _surfer.moveData.origin += heightDifference / 2 * (_surfer.moveData.uncrouchDown ? Vector3.down : Vector3.up);

                }

                if (!canUncrouch)
                    _surfer.moveData.crouchLerp = 1f;

            }

            _surfer.moveData.renderCrouchLerp = _surfer.moveData.crouchLerp;

        }

        void SlideMovement () {
            
            // Gradually change direction
            _surfer.moveData.slideDirection += new Vector3 (groundNormal.x, 0f, groundNormal.z) * _surfer.moveData.slideSpeedCurrent * _deltaTime;

            // Steering
            Vector3 sideDir = Vector3.Cross (Vector3.up, _surfer.moveData.slideDirection);
            _surfer.moveData.slideDirection += sideDir * _surfer.moveData.horizontalAxis * _config.slideSteerForce * _deltaTime;

            _surfer.moveData.slideDirection = _surfer.moveData.slideDirection.normalized;

            // Set direction
            Vector3 slideForward = Vector3.Cross (groundNormal, Quaternion.AngleAxis (-90, Vector3.up) * _surfer.moveData.slideDirection);
            bool movingDownhill = slideForward.y < 0f;

            // Set the velocity
            // Only apply friction if NOT moving downhill (user request: frictionless downhill slide)
            if (!movingDownhill) {
                _surfer.moveData.slideSpeedCurrent -= _config.slideFriction * _deltaTime;
            }
            
            _surfer.moveData.slideSpeedCurrent = Mathf.Clamp (_surfer.moveData.slideSpeedCurrent, 0f, _config.maximumSlideSpeed);
            
            if (movingDownhill)
                _surfer.moveData.slideSpeedCurrent += _config.downhillSlideSpeedMultiplier * _deltaTime;
            else
                _surfer.moveData.slideSpeedCurrent -= (slideForward * _surfer.moveData.slideSpeedCurrent).y * _deltaTime * _config.downhillSlideSpeedMultiplier; // Accelerate downhill (-y = downward, - * - = +)

            _surfer.moveData.velocity = slideForward * _surfer.moveData.slideSpeedCurrent;
            
            // Jump
            if (_surfer.moveData.wishJump && _surfer.moveData.slideSpeedCurrent < _config.minimumSlideSpeed * _config.slideSpeedMultiplier) {

                Jump ();
                return;

            }

        }

    }
}
