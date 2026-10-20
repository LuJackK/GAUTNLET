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
| B-SLD-01 | Slide activation | configured | speed ≥ 9 m/s or on slope, after delay | not started |
| B-SLD-02 | Slide sustain and exit | configured | ends when crouch released | not started |
| B-SLD-03 | Slide friction and grace | configured | linear drag 3 m/s² | not started |
| B-SLD-04 | Slope sliding | configured | gravity projected down the slope | not started |
| B-SLD-05 | Slide steering | configured | heading steer | not started |
| B-SLD-06 | Sprint-slide boost | configured | none ("never multiplies speed") | not started |
| B-SLD-07 | Landing slide boost | configured | none | not started |
| B-SLD-08 | Dash-slide | configured | none | not started |
| B-SLD-09 | Slide-jump | configured | ordinary jump | not started |
| B-SLD-10 | Drop-slide (crouch while falling) | configured | none | not started |
| B-SLD-11 | Slide cancel and lockout | configured | none | not started |
| B-STA-01 | Stamina pool and regen | configured | 3 bars, 1/s after 1 s delay | not started |
| B-STA-02 | Stamina costs | configured | dash 1, air dash 1, double jump 1 | not started |
| B-WAL-01 | Wall jump | configured | none | deferred |
| B-WAL-02 | Wall slide | configured | none | deferred |
| B-MNT-01 | Mantle | configured | none | deferred |

\* Release-to-stop is 1–2 ticks faster in the capture than in the model; see B-GND-01.

## Ground

### B-GND-01 Ground friction
- **Deadlock** (`0xb08540`, confirmed): applies when grounded (or mv flag +0x10e). Uses
  horizontal speed only. Below 0.1 u/s, horizontal velocity is zeroed. Otherwise
  `speed −= max(speed, stopSpeed) × friction × dt`, floored at 0, direction kept.
  `friction = sv_friction × surfaceFriction × hero multiplier × modifier multiplier`, doubled
  while a vector at mv+0xf4 is set. That vector is unidentified (possibly base or ground
  velocity).
- **Open:** both multipliers (`0xdf87e0`, `0xaf8870`) and the mv+0xf4 vector. Capture release
  time 0.500–0.516 s against a model 0.531 s.
- **GAUNTLET:** `SurfPhysics.Friction`, same law. Different values. No zero-below-threshold.
  Crouch has its own friction, and dashing scales friction by 0.5.
- **Gap:** values; threshold; multipliers once decoded. Is GAUNTLET's dash-friction rule in Deadlock? Pending B-DSH-01.
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

All slide behaviours anchor on `CCitadel_Ability_Slide`. It has six slide-specific vtable
overrides: slot 14 `0x2b7a50`, 269 `0x2b6480`, 345 `0x2b1810`, 350 `0x2a4e80`,
352 `0x2a72b0` and 354 `0x2a35b0`. `0x265660` reads `citadel_player_slide_min_percent`.
Values below are base `CAbilitySlideVData` from vdata.

### B-SLD-01 Activation
- **Deadlock:** flat ground needs speed ≥ 350 (`m_flMinActivationSpeed`). While dashing, ≥
  `m_flDashMinActivationSpeed`. Forward slope probe (`m_flSlideActivationProbeForwardOffset`)
  can start a slide early on a slope; facing angle limits apply. `citadel_slide_delay` 0.15;
  `m_flButtonPressWindow` 5.
- **GAUNTLET:** crouch held, `slideDelay` 0.5 s elapsed, speed ≥ 9 m/s or slope > 5°.

### B-SLD-02 Sustain and exit
- **Deadlock:** on flat ground, stop sliding below 170 (`m_flMinSustainSpeed`).
- **GAUNTLET:** no sustain threshold; the slide ends when crouch is released or the speed condition fails.

### B-SLD-03 Friction and grace
- **Deadlock:** flat friction 0.8. For 0.6 s after starting (0.75 s after landing) the grace
  friction 0.05 applies instead. Separate min/max-slope and uphill friction. The friction law
  is presumably shared with B-GND-01 (to verify).
- **GAUNTLET:** constant linear drag 3 m/s².

### B-SLD-04 Slope sliding
- **Deadlock:** slope acceleration between min/max slope angles (`m_flAccMinSlopeDeg`,
  `m_flAccMaxSlopeDeg`) with speed caps; sampled slope normals from probes.
- **GAUNTLET:** gravity projected onto the ground normal × `slideSlopeAcceleration`.

### B-SLD-05 Steering
- **Deadlock:** `m_flTurnSpeed` 90 with min/max angle difference.
- **GAUNTLET:** heading steer at `slideControlAcceleration`.

### B-SLD-06 Sprint-slide boost / B-SLD-07 Landing slide boost
- **Deadlock:** running forward into a slide adds 300. Landing into a slide adds a boost of 100–600,
  scaled by landing speed between the min and max trigger speeds, and biased by slope.
- **GAUNTLET:** none by design so far.

### B-SLD-08 Dash-slide
- **Deadlock:** sliding within 0.3 s of a dash starts at 650 (late timing gives
  `m_flDashSlideFailSpeed`), then friction 0.175 for 1 s. There is a success sound.
- **GAUNTLET:** none.

### B-SLD-09 Slide-jump
- **Deadlock:** jumping out of a slide applies air drag for a limited time, ramping by angle.
  A jump shortly after the slide ends still counts (`m_flLateSlideJumpWindow`).
- **GAUNTLET:** ordinary jump.

### B-SLD-10 Drop-slide
- **Deadlock:** crouching while falling can start a slide on landing. The activate press may
  be some time earlier. Lookahead up to 150 (`citadel_slide_one_button_falling_max_lookahead_distance`).
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
