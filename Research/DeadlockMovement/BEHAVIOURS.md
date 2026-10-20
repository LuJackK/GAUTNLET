# Deadlock movement behaviours

Started 2026-10-20 on branch `deadlock-movement-mapping`. One entry per behaviour: what
triggers it, what it does, its timing and interactions, the evidence, GAUNTLET's current
equivalent and the gap. Numeric values live in [the value mapping](GAUNTLET_MAPPING.md);
decoding method and RVAs in [the static analysis](STATIC_ANALYSIS.md).

Reference IDs (`B-SLD-03`) in notes, commits and tests. Never reuse an ID; mark a dropped
behaviour as such.

**Deadlock status:** `decoded` (rule read in code) · `confirmed` (decoded and a capture
agrees) · `located` (code found, not read) · `configured` (only vdata/convar values known)
· `observed` (seen in captures or play, no rule) · `unknown`.

**Port status:** `not started` · `specified` (Unity design written) · `implemented` ·
`validated` (Unity trace matches a Deadlock capture within the agreed tolerance).

## Index

| ID | Behaviour | Deadlock | GAUNTLET now | Port |
| --- | --- | --- | --- | --- |
| B-GND-01 | Ground friction | confirmed* | same law, different values | not started |
| B-GND-02 | Ground accelerate | confirmed | Source accelerate, no min-speed clamp | not started |
| B-GND-03 | Ground tick order | confirmed | friction then accelerate | not started |
| B-GND-04 | Crouch walking | configured | separate crouch speed/accel/friction | not started |
| B-GND-05 | Sprint | configured | hidden, unused | not started |
| B-AIR-01 | Gravity | confirmed | constant, suspended while dashing | not started |
| B-AIR-02 | Air accelerate | decoded (partial) | custom steer + accelerate | not started |
| B-AIR-03 | Air drag | located | none | not started |
| B-JMP-01 | Ground jump | observed | set vy, auto-bhop, beats slide | not started |
| B-JMP-02 | Air (double) jump | configured | set vy after 0.2 s, costs stamina | not started |
| B-JMP-03 | Coyote jump | unknown | 0.1 s grace | not started |
| B-DSH-01 | Ground dash | configured | velocity set to max(speed, 20 m/s) | not started |
| B-DSH-02 | Air dash | configured | velocity set to max(speed, 25 m/s), once per air time | not started |
| B-DSH-03 | Down dash | configured | none | not started |
| B-DSH-04 | Dash-jump | configured | jump cancels dash | not started |
| B-SLD-01 | Slide activation | decoded | speed ≥ 9 m/s or on slope, after delay | not started |
| B-SLD-02 | Slide sustain and exit | decoded | ends when crouch released | not started |
| B-SLD-03 | Slide friction and grace | decoded | linear drag 3 m/s² | not started |
| B-SLD-04 | Slope sliding | decoded | gravity projected down the slope | not started |
| B-SLD-05 | Slide steering | decoded (partial) | heading steer | not started |
| B-SLD-06 | Sprint-slide boost | decoded (suspect) | none ("never multiplies speed") | not started |
| B-SLD-07 | Landing slide boost | decoded | none | not started |
| B-SLD-08 | Dash-slide | confirmed | none | not started |
| B-SLD-09 | Slide-jump | located | ordinary jump | not started |
| B-SLD-10 | Drop-slide (crouch while falling) | decoded | none | not started |
| B-SLD-11 | Slide cancel and lockout | configured | none | not started |
| B-STA-01 | Stamina pool and regen | configured | 3 bars, 1/s after 1 s delay | not started |
| B-STA-02 | Stamina costs | configured | dash 1, air dash 1, double jump 1 | not started |
| B-WAL-01 | Wall jump | configured | none | deferred |
| B-WAL-02 | Wall slide | configured | none | deferred |
| B-MNT-01 | Mantle | configured | none | deferred |

\* Release-to-stop is 1–2 ticks faster in the capture than in the model; see B-GND-01.

## Ground

### B-GND-01 Ground friction
- **Deadlock** (`0xb08540`, decoded): applies when grounded (or mv flag +0x10e). Uses
  horizontal speed only. Below 0.1 u/s, horizontal velocity is zeroed. Otherwise
  `speed −= max(speed, sv_stopspeed) × friction × dt`, floored at 0, direction kept.
- `friction = sv_friction (4) × surfaceFriction × pawnFriction × FRICTION_PERCENTAGE × GROUND_FRICTION_PERCENTAGE`,
  doubled while a vector at mv+0xf4 is set (unidentified, possibly base velocity). `pawnFriction`
  is the pawn coefficient at +0x3fc (`0xdf87e0`): 1 normally, written by the slide (B-SLD-03).
  The two percentages are modifier values 3 and 142 (`0xaf8870`), default 1.
- **Open:** capture release time 0.500–0.516 s against a model 0.531 s with all multipliers at 1.
  Check surfaceFriction and the mv+0xf4 vector during those trials.
- **GAUNTLET:** `SurfPhysics.Friction`, same law. Different values. No zero-below-threshold.
  Crouch has its own friction, and dashing scales friction by 0.5.
- **Gap:** values and threshold. Is GAUNTLET's dash-friction rule in Deadlock? Pending B-DSH-01.
- **Port note:** model slide deceleration as a friction coefficient on this same function, not as
  separate drag.
- **Check:** release-to-stop tick count against `human-20261018-02` forward trials.

### B-GND-02 Ground accelerate
- **Deadlock** (`0xb06ec0`, `0xb139f0`; confirmed): `addspeed = wishspeed − v·wishdir`, and
  nothing happens if it is not positive. Grounded:
  `accelspeed = max(wishspeed, citadel_min_accel_speed) × EMoveAcceleration × groundAccelModifier × surfaceFriction × dt`.
  Then `v += min(accelspeed, addspeed) × wishdir`.
- **Effect:** at walking speeds the acceleration is constant (based on 400 u/s, not on
  wishspeed), so slow heroes and slow states accelerate as hard as fast ones.
- **GAUNTLET:** `SurfController.Accelerate` with `acceleration × targetSpeed × dt`.
- **Gap:** min-accel-speed clamp; values. Wishspeed derivation (`0xb0b640`) not read yet.
- **Check:** walk from rest to 95%: 23 ticks at 64 Hz (passes against capture).

### B-GND-03 Ground tick order
- **Deadlock** (confirmed by capture): friction, then accelerate. Accelerate-then-friction never
  reaches top speed.
- **Open:** where jump, dash, slide and gravity sit within `ProcessMovement` (`0xb2f7b0`).
- **GAUNTLET:** dash, then jump, then slide or friction+accelerate, then gravity, then collision move.

### B-GND-04 Crouch walking / B-GND-05 Sprint
- **Deadlock:** hero stats `ECrouchSpeed` 4.75, `ESprintSpeed` 1.6 (base). How they enter
  wishspeed is unknown.
- **GAUNTLET:** crouch speed 4, accel 8, friction 3, stop 4. Sprint is a hidden field, not simulated.

## Air

### B-AIR-01 Gravity
- **Deadlock:** `sv_gravity` 800 u/s² (confirmed: measured 800.003). `MODIFIER_VALUE_GRAVITY_SCALE` exists.
- **Open:** whether dashes suspend gravity; integration order (the position fit's linear term
  differs from its velocity intercept).
- **GAUNTLET:** constant 20 m/s², not applied while dashing.

### B-AIR-02 Air accelerate
- **Deadlock** (`0xb06ec0`, partial): airborne `accelspeed = wishspeed × sv_airaccelerate × 0.25 × dt`,
  same projection cap as on the ground. Whether wishspeed is capped in air (Source caps it at 30)
  is in the unread air-move function `0xb07140`.
- **GAUNTLET:** heading steer (`airControlAcceleration`) plus accelerate to `maxSpeed` 6, or a
  Source-style capped mode at `sourceAirStrafeStrength` 1.
- **Gap:** pending `0xb07140`. Do not change GAUNTLET air control before it is read.

### B-AIR-03 Air drag
- **Deadlock** (`0xb076c0`, located): reads `citadel_air_drag_min` 0.2 and `sv_stopspeed`.
  Related: slows affect air drag (`citadel_enable_slows_affect_air_drag`, penalty scale 0.35),
  and the slide-jump air drag in B-SLD-09.
- **GAUNTLET:** none.

## Jumping

### B-JMP-01 Ground jump
- **Deadlock:** observed rise 53.81 u (1.37 m) over a 0.71875 s airborne span. First airborne vz is
  287.485, one tick after takeoff, so it is not the impulse. Ability class
  `CCitadel_Ability_Jump` overrides 17 vtable slots; slot 258 (`0x264150`) and slot 269
  (`0x2b58e0`) are jump-specific candidates.
- **GAUNTLET:** `vy = jumpForce` (6.5 m/s). Auto-bhop on. Jump beats slide and cancels dash.

### B-JMP-02 Air (double) jump
- **Deadlock:** `citadel_air_jumps_enabled`; the vertical speed scales by
  `AirJumpVerticalSpeedPercent`; costs stamina (value pending).
- **GAUNTLET:** needs 0.2 s since the last jump and enough stamina; sets vy to 8 m/s.

### B-JMP-03 Coyote jump
- **Deadlock:** unknown whether it exists. Watch for a grace timer in the jump ability.
- **GAUNTLET:** 0.1 s after leaving walkable ground; free.

## Dash

### B-DSH-01 Ground dash / B-DSH-02 Air dash / B-DSH-03 Down dash
- **Deadlock:** hero stats: ground 10 m in 0.68 s, air 8 m in 0.47 s (base). Ground uses
  `m_groundDashPositionCurve`. Ground-dash speed is limited by min/max percent 0.5 and 2
  (`citadel_player_ground_dash_*`). Stamina 1, down-dash 0.5. Measured flat peak
  635.005 u/s. `CCitadel_Ability_Dash` overrides 20 slots, including 270, 279, 306,
  330 and 362, which are not shared with slide.
- **GAUNTLET:** velocity set to `max(speed, impulse)` along wish or forward; 0.3 s ground and
  0.2 s air duration; gravity off; friction ×0.5; cooldown 0.5 s; one air dash per air time.
- **Gap:** likely a position-curve model instead of an impulse. Momentum composition and
  dash-end velocity are unknown.

### B-DSH-04 Dash-jump
- **Deadlock:** `m_flDashJumpVerticalSpeed` 425; `m_flDashJumpMissMaxSpeed` 250 (speed cap when
  the timing is missed); start/end window fields exist.
- **GAUNTLET:** jumping cancels the dash and keeps horizontal velocity.

## Slide

Decoded 2026-10-20 from `CCitadel_Ability_Slide`, build 26059233. Values are base
`CAbilitySlideVData` (vdata build 25841406). "Speed" means horizontal speed.

| RVA | Role |
| --- | --- |
| `0x2b7bb0` | per-tick input, start and stop; runs the friction update last |
| `0x2b0740` | SetSliding(on/off): entry boosts, dash-slide, friction reset |
| `0x2b6cd0` | slide friction update (writes the pawn friction coefficient) |
| `0x295d70` | movement callback (interface at +0x4c0, slot 4): pending boost, slope acceleration, steering |
| `0x2a35c0` | landing callback: drop-slide and landing boost |
| `0x252350` | slope check: 1 = standing on a downhill slope ≥ 8°, 2 = forward probe finds one, 0 = no |
| `0x265660` | slow scaling: `clamp(slowFactor / (2 − slowResistance), 0.8, 1)`; 1 when not slowed |
| `0x2ab920` | registers modifier providers: AIR_DRAG (185), MOVEMENT_SPEED_MAX_PERCENT (70) |

The six non-boilerplate vtable overrides (`0x2b7a50`, `0x2b6480`, `0x2b1810`, `0x2a4e80`,
`0x2a72b0`, `0x2a35b0`) turned out to be cleanup, the `"sliding"` animation tag and stop-slide
calls. The logic is in the functions above.

**How speed changes reach velocity.** Slide entry never sets velocity directly. It stores a
pending speed in `m_flSpeedAdjust`; the next movement callback adds it once along the horizontal
velocity direction (or the facing direction when stationary), then clears it. Deceleration comes
from ground friction (B-GND-01): the slide writes the pawn friction coefficient (pawn +0x3fc),
which ground friction multiplies with `sv_friction` 4 under the stop-speed rule. Stopping the
slide resets the coefficient to 1.

### B-SLD-01 Activation
- **Deadlock** (`0x2b7bb0`): wanting to slide needs crouch held (or the sticky/toggle slide button)
  and no ability block. Holding crouch from before does not restart a slide unless a forced-restart
  tick allows it. Crouch press time and "airborne when crouch pressed" are recorded.
- A slide starts when wanted, not already sliding, and any of:
  1. the slope check passes (downhill ≥ 8° underfoot, or ahead via the forward probe);
  2. grounded and speed ≥ `m_flMinActivationSpeed` 350 (8.89 m/s);
  3. grounded, dashing (pawn state bit 0x200) and speed ≥ `m_flDashMinActivationSpeed` 250.
- Landing on a slide-able slope starts one via B-SLD-10.
- `citadel_slide_delay` 0.15 is not read on this path. Its role is open.
- **GAUNTLET:** crouch held, `slideDelay` 0.5 s, speed ≥ 9 m/s or slope > 5°.
- **Gap:** dash threshold, forward probe, no restart while crouch stays held.

### B-SLD-02 Sustain and exit
- **Deadlock** (`0x2b7bb0`): stops when crouch is released (unless sticky), when airborne, when
  blocked, or when speed < `m_flMinSustainSpeed` 170 with no pending boost, unless on a downhill
  slope it is facing (within `m_flSlopeFacingAngleToActivate`). On stop: friction coefficient back
  to 1, optional slow-getup animation, timers reset.
- **GAUNTLET:** no sustain threshold.

### B-SLD-03 Friction and grace
- **Deadlock** (`0x2b6cd0`), flat ground (slope < 8°). The coefficient is the first that applies:
  1. within `m_flDashSlideFrictionTime` 1.0 s of a dash-slide start: `m_flDashSlideFriction` 0.175;
  2. within `m_flFlatGroundFrictionGraceTime` 0.6 s of starting to slide on flat ground, or within
     `m_flLandedFlatGroundFrictionGraceTime` 0.75 s of the slide's ground start: grace 0.05;
  3. otherwise `m_flFrictionFlatGround` 0.8.
- Window lengths scale with `MOVEMENT_SLIDE_DISTANCE_SCALE` and a time scale. The coefficient is
  multiplied by `(2 − slowScaling) × max(0, 2 − slideDistanceScale)`, which is 1 by default.
  Pawn modifier state 200 forces 0.
- On slopes: `remap(slopeDeg, 5°, 45°, FrictionMinSlope 0.15, FrictionMaxSlope 0.15)`, or the
  uphill pair 1.0 → 1.5 when moving uphill. The flat timer resets on slopes.
- **Effective decay** with `sv_friction` 4 and stop speed 100: dash-slide 0.7/s, grace 0.2/s,
  flat 3.2/s, downhill 0.6/s, uphill 4–6/s, all above 100 u/s; below 100 u/s it is constant
  (4 × c × 100 u/s²).
- **GAUNTLET:** linear drag 3 m/s².
- **Check:** the pilot's single-exponential fit (k ≈ 0.90/s, RMSE ~38) mixes the 0.7/s and
  3.2/s regimes. Refit piecewise.

### B-SLD-04 Slope acceleration
- **Deadlock** (`0x295d70`): when slope ≥ 8° and not uphill, `t = clamp((deg − 5)/(45 − 5), 0, 1)`,
  `accel = lerp(500, 850, t)`, `cap = lerp(1200, 1500, t)`, then each tick
  `v += downhillDir_xy × min(max(cap − |v|, 0), accel) × dt`. For the first
  `m_flInitialSlideUseForwardProbeTime` 0.25 s of a slide started by the forward probe, the
  probe's slope is used instead of the slope underfoot.
- **GAUNTLET:** gravity projected on the ground normal × `slideSlopeAcceleration`.

### B-SLD-05 Steering
- **Deadlock** (`0x295d70`): with movement input, velocity rotates about the vertical axis by
  `turn × dt × m_flTurnSpeed (90°/s) × scale`, with `turn` clamped to [−1, 1] (input relative to
  velocity). On flat ground `scale` is a modifier (likely `MOVEMENT_SLIDE_TURN_SCALE`); on slopes
  it is `1 − clamp(|angle term| / 45°, 0, 1)`. Rotation only, so speed is kept.
- **Open:** the exact `turn` function (`0x254fc70`) and the slope angle term;
  `m_flTurnMin/MaxAngDiff` not seen yet.
- **GAUNTLET:** heading steer at `slideControlAcceleration`.

### B-SLD-06 Sprint-slide boost
- **Deadlock** (`0x2b0740`): on slide start, if at least 1 s has passed since the last slide change
  and the pawn has state bit 0x40 (likely sprinting), pending boost = `m_flSprintBoostSpeed` 300.
- **Suspect:** as decompiled, the slide-change time is updated just before this check, which
  would make the elapsed time 0 and the boost unreachable. Verify with a sprint-into-slide capture.

### B-SLD-07 Landing slide boost
- **Deadlock** (`0x2a35c0`): on landing with fall speed `f`, if wanting to slide, not sliding,
  the slope check passes, and crouch was pressed within `m_flButtonPressWindow` 5 s: start the
  slide. If `f ≥ 300`, pending boost =
  `remap(f, 300, 700, 100, 600) × Bias(remap(slopeDeg, 5°, 45°, 0.25, 1), 0.6)`.
  Flat landings get no boost. `Bias` is assumed to be Source's `x^(log(b)/log(0.5))`
  (`0x2550650`, to verify).
- **GAUNTLET:** none.

### B-SLD-08 Dash-slide
- **Deadlock** (`0x2b0740`), confirmed: on slide start while dashing (state bit 0x200), the target is
  `m_flDashSlideSpeed` 650 if now is past the dash-slide timer (`m_flGroundDashSlideTime`),
  otherwise `m_flDashSlideFailSpeed` 450 (too early). Pending boost = `slowScaling × target − speed`,
  so speed becomes exactly the target. It starts the 1 s dash-slide friction window and the ground
  start time. The success sound plays only at full speed. When not dashing, the dash-slide time is
  cleared.
- **Capture:** one tick after entry, 650 × (1 − 0.7/64) = 642.9 and 450 × (1 − 0.7/64) = 445.1.
  The pilot measured peaks of "approximately 643" (13 trials) and "around 445" (3 trials).
- **Open:** how `m_flGroundDashSlideTime` is set (expected: dash start + `m_flDashSlideStartTime` 0.3).
- **GAUNTLET:** none.

### B-SLD-09 Slide-jump
- **Deadlock:** the slide supplies `MODIFIER_VALUE_AIR_DRAG` (provider `0x2632e0`, unread);
  `m_flAirDragAfterJumpTime` 0.5, `m_flLateSlideJumpWindow` 0.25.
- **GAUNTLET:** ordinary jump.

### B-SLD-10 Drop-slide
- **Deadlock** (`0x2a35c0`): the landing path in B-SLD-07. Crouch pressed up to 5 s before landing
  starts a slide on a slide-able slope. The lookahead convar (150) is unread.
- **GAUNTLET:** the slide starts on landing only if crouch is held and the speed condition holds.

### B-SLD-11 Cancel and lockout
- **Deadlock:** `m_flSlideCancelBlockerWindow` and `m_flSlideLockoutTime` (registered with the dash schema fields); parry
  cancel slide fields.
- **GAUNTLET:** none.

## Stamina

### B-STA-01 Pool and regen / B-STA-02 Costs
- **Deadlock:** 3 bars, regen 0.222/s (base). Grounded/air regen rules and any delay are
  unknown. Costs: dash 1, down-dash 0.5, wall jump 0.5 with 1 free. Air jump cost pending.
- **GAUNTLET:** 3 bars, 1/s after a 1 s delay since the last spend; costs 1/1/1.

## Deferred

B-WAL-01 wall jump (out speeds 225–310, up 525, lateral 100, fatigue recovery),
B-WAL-02 wall slide (terminal velocity 600), B-MNT-01 mantle (max height 134). Keep
`MoveData` room for wall contact and charges.
