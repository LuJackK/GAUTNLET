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
| B-GND-03 | Tick order | decoded | friction then accelerate | not started |
| B-GND-04 | Crouch walking | configured | separate crouch speed/accel/friction | not started |
| B-GND-05 | Sprint | configured | hidden, unused | not started |
| B-AIR-01 | Gravity | confirmed | constant, suspended while dashing | not started |
| B-AIR-02 | Air accelerate | decoded | custom steer + accelerate | not started |
| B-AIR-03 | Air drag | decoded | none | not started |
| B-JMP-01 | Ground jump | confirmed | set vy, auto-bhop, beats slide | not started |
| B-JMP-02 | Air (double) jump | decoded | set vy after 0.2 s, costs stamina | not started |
| B-JMP-03 | Late jump (coyote) | decoded | 0.1 s grace | not started |
| B-DSH-01 | Ground dash | confirmed | velocity set to max(speed, 20 m/s) | not started |
| B-DSH-02 | Air dash | decoded | velocity set to max(speed, 25 m/s), once per air time | not started |
| B-DSH-03 | Down dash | decoded (partial) | none | not started |
| B-DSH-04 | Dash-jump | decoded | jump cancels dash | not started |
| B-SLD-01 | Slide activation | decoded | speed ≥ 9 m/s or on slope, after delay | not started |
| B-SLD-02 | Slide sustain and exit | decoded | ends when crouch released | not started |
| B-SLD-03 | Slide friction and grace | decoded | linear drag 3 m/s² | not started |
| B-SLD-04 | Slope sliding | decoded | gravity projected down the slope | not started |
| B-SLD-05 | Slide steering | decoded | heading steer | not started |
| B-SLD-06 | Sprint-slide boost | decoded (suspect) | none ("never multiplies speed") | not started |
| B-SLD-07 | Landing slide boost | decoded | none | not started |
| B-SLD-08 | Dash-slide | confirmed | none | not started |
| B-SLD-09 | Slide-jump | decoded | ordinary jump | not started |
| B-SLD-10 | Drop-slide (crouch while falling) | decoded | none | not started |
| B-SLD-11 | Slide cancel and lockout | configured | none | not started |
| B-STA-01 | Stamina pool and regen | configured (wiki) | 3 bars, 1/s after 1 s delay | not started |
| B-STA-02 | Stamina costs | decoded | dash 1, air dash 1, double jump 1 | not started |
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
  doubled while the vector at mv+0xf4 is set. That vector is the ground-dash displacement
  (B-DSH-01); friction is skipped entirely while its horizontal length exceeds 1 u. `pawnFriction`
  is `CBaseEntity::m_flFriction` (pawn +0x3fc, `0xdf87e0`): 1 normally, written by the slide (B-SLD-03).
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

### B-GND-03 Tick order
- **Deadlock** (decoded; ground order confirmed by capture). `ProcessMovement` (`0xb2f7b0`) uses
  `dt = frametime × time scale`, first resolves a moving parent (`0xb09060`), then by move type:
  walk (2) → `0xb40940`, noclip (7) → fly move. Walk move, in order:
  1. strafe-reversal tracking: rapid left/right input reversals within 0.3 s count up; a factor
     (services +0x2f0) is 0 during them and ramps back to 1 over 0.15 s (consumer not found yet);
  2. friction: air drag `0xb076c0` if airborne, else ground friction `0xb08540` (skipped by mv flag +0x10f);
  3. acceleration: `AirAccelerate` `0xb07140` if airborne, else `Accelerate` `0xb06ec0` (skipped by +0x110);
  4. dash displacement to velocity `0xb094a0`: velocity −= last dash velocity, += `disp / dt`;
  5. slip acceleration `0xb09ab0` (`citadel_movement_slip_accel` 200);
  6. gravity `0xb08740` (B-AIR-01);
  7. `0xb092e0` (pushes/base velocity, unread) and `0xb07ef0` (unread);
  8. stepping and collision move (`0xb09d80`, `0xb3b320`, `0xb09670`), ground categorisation (`0xb32800`).
  Ability movement callbacks (interface +0x4c0 slot 4: slide, dash) run from a dispatch site not
  yet located; tests T03/T04 show the one-tick timing.
- **GAUNTLET:** dash, then jump, then slide or friction+accelerate, then gravity, then collision move.

### B-GND-04 Crouch walking / B-GND-05 Sprint
- **Deadlock:** hero stats `ECrouchSpeed` 4.75, `ESprintSpeed` 1.6 (base). How they enter
  wishspeed is not decoded yet.
- **Sprint (wiki):** added to move speed after **5 s out of combat**, until combat or zooming in.
  The first 2.5 m/s applies at once; anything beyond it ramps in at 0.6 m/s per second. Sprint bonuses
  stack additively. Sliding does not interrupt sprint. Base 7.2 + 1.6 = 8.8 m/s cannot start a flat
  slide (B-SLD-01).
- **GAUNTLET:** crouch speed 4, accel 8, friction 3, stop 4. Sprint is a hidden field, not simulated.

## Air

### B-AIR-01 Gravity
- **Deadlock** (`0xb08740`), confirmed: one full step per tick,
  `vz −= gravity (800) × pawn gravity scale × per-move factor (mv +0x160) × dt`. If
  `MODIFIER_VALUE_FALL_SPEED_MAX` is set, the step is limited so fall speed does not exceed it.
  Skipped while mv flags +0x10c, +0x10d, +0x111 or +0x112 are set. The **air dash sets +0x111**, so
  gravity is off during an air dash. The gravity convar handle is `0x3d482e0` (value 800 per capture;
  name not resolved).
- **Capture:** measured 800.003 u/s²; the first airborne vz = 300 − 800/64 fits one full step after
  the jump sets vz.
- **GAUNTLET:** constant 20 m/s² (Deadlock 20.32), not applied while dashing (ground or air).

### B-AIR-02 Air accelerate
- **Deadlock** (`0xb07140`): Source `AirAccelerate` with Deadlock's wish-speed cap.
  - `wishdir`, `wishspeed` come from `0xb0b640` (input, view, hero max move speed `0xb150f0`,
    modifiers). Wishspeed is 0 while a ground-dash vector is active.
  - **Cap:** `cap = lerp(citadel_aircontrol_speed_slow 80, citadel_aircontrol_speed_fast 50,
    remap(horizontal speed, 600, 800, 0, 1))`: 80 u/s (2.03 m/s) below 600 u/s of carried speed,
    easing to 50 u/s above 800. An `AIR_SPEED_MAX`-style override (`0xb15de0`) or an active ability
    limit replaces or lowers it. `wishspeed = min(wishspeed, airControlPercent × cap)`.
  - `addspeed = wishspeed − v·wishdir`; if positive,
    `v += wishdir × min(addspeed, airaccel × airAccelPercent × wishspeed × dt)` with
    `airaccel = sv_airaccelerate` 10 (or `citadel_fly_accelerate` 2 under mv flag +0x10d).
  - A ground dash carried into the air accelerates toward the dash velocity instead.
  - Modifier state 0x1f: horizontal velocity snaps to `wishdir × wishspeed` (instant control).
- Note: an earlier entry said air uses `Accelerate` × 0.25. That branch of `0xb06ec0` is never
  reached from walk movement, which calls `0xb07140` when airborne.
- **GAUNTLET:** heading steer (`airControlAcceleration`) plus accelerate to `maxSpeed` 6, or the
  Source-style capped mode at `sourceAirStrafeStrength` 1 with `sourceAirSpeedCap`. The capped mode is
  the right structure: cap 80 → 50 u/s (2.03 → 1.27 m/s) blended by speed 600–800 u/s, air
  accelerate 10.

### B-AIR-03 Air drag
- **Deadlock** (`0xb076c0`). Skipped in pawn state bit 14.
  - **Horizontal**, when |v.xy| ≥ 0.1: split velocity into components along and across the pawn's
    facing. `drag = max(citadel_air_drag_min 0.2, air-drag modifier) × slow multiplier`
    (`citadel_enable_slows_affect_air_drag`, penalty scale 0.35). `newspeed = max(0, speed −
    max(speed, sv_stopspeed 100) × drag × dt)`, the same law as ground friction. Each component is
    scaled to it when moving faster than the air speed override, or when there is no input on that
    axis and an override is active. With no override (normal case), both components always drag.
    So carried air speed decays at about 0.2/s (≈ 18% per second) above 100 u/s.
  - **Vertical**, only under mv flag +0x10d or pawn state bit 10: the same law on |vz| with
    `MODIFIER_VALUE_AIR_DRAG` (the slide supplies this, B-SLD-09) plus another value.
  - **Wall slide** (pawn state bit 8, falling): vz eased by a remap up to
    `citadel_wall_slide_terminal_velocity` 600 (deferred).
- **GAUNTLET:** none.
- **Check:** horizontal speed through the airborne part of the pilot's running jumps should fall by
  about (1 − 0.2/64) per tick.

## Jumping

Decoded 2026-10-20 from `CCitadel_Ability_Jump`, build 26059233. `0x2a1790` is the jump
dispatcher: it checks overhead clearance (`citadel_jump_overhead_clearance` 73), records slide-jump
state, picks a type with `0x25b970`, builds the new velocity, and applies it with `0x25d4f0`.
The applier *sets* the pawn velocity (`entity.set-abs-velocity`), records the jump time and counts
executed air jumps. Jump-ability values are from the vdata `citadel_ability_jump` block.

| Type | Builder | Chosen when (in this order) |
| --- | --- | --- |
| 3 dash-jump | `0x25c9f0` | pawn dashing state (bit 0x200); or the late dash-jump check `0x2524d0` passes |
| 2 wall jump | `0x25d6a0` | near a wall and wall-jump fatigue allows (deferred, B-WAL-01) |
| 1 air jump | `0x25c4b0` | airborne and outside the late-jump grace (B-JMP-03) |
| 0 ground jump | `0x25d120` | otherwise (grounded, or within the late-jump grace) |

### B-JMP-01 Ground jump
- **Deadlock** (`0x25d120`), confirmed: keeps the current velocity, scaled by
  `m_flSlowedSlideJumpFactor` when slide-jumping while slowed. Sets
  `vz = VerticalSpeed (300) × jump-height modifier (0x88, default 1)`. Horizontal velocity is not
  changed.
- **Capture:** the first airborne vz was 287.485 = 300 − 800/64 (one gravity tick). The rise of
  53.8 u matches 300²/(2·800) = 56.25 u minus discretisation and takeoff offset.
- **GAUNTLET:** `vy = jumpForce` 6.5 m/s (Deadlock 300 u/s = 7.62 m/s). Auto-bhop on. Jump beats
  slide and cancels dash.

### B-JMP-02 Air (double) jump
- **Deadlock** (`0x25c4b0`): costs **1 stamina** unless the consecutive air-jump count is still
  below the free-air-jump modifier (`MODIFIER_VALUE_FREE_AIR_JUMPS`). Starts from the current
  velocity. If there is movement input, it can add horizontal speed along the input direction up to
  max(move speed, another speed term), with the gain per jump capped by `m_flMaxSpeedDelta`. This
  lets an air jump redirect momentum. Then **sets** `vz = VerticalSpeed (300) × jump-height modifier`.
  The count of air jumps per airtime comes from `MODIFIER_VALUE_AIR_JUMPS` (wiki: one by default,
  two with Vitality or Stamina Mastery).
- **Open:** where `AirJumpVerticalSpeedPercent` 75 applies (not on this path; it has an
  `EAirMoveDistanceScale` scale function). The value of `m_flMaxSpeedDelta` is not in vdata.
- **GAUNTLET:** needs 0.2 s since the last jump and 1 stamina; sets vy to 8 m/s; no horizontal redirect.

### B-JMP-03 Late jump (coyote time)
- **Deadlock** (`0x2b8cb0`): a jump counts as a ground jump within `m_flLateJumpGraceWindow`
  **0.15 s** of the last on-ground time, but only while vertical velocity is negative (walking off a
  ledge, not after jumping). Also within 0.15 s of leaving a zipline. It is free.
- **GAUNTLET:** 0.1 s after leaving walkable ground; free. **Gap:** value 0.15, and require falling.

## Dash

Decoded 2026-10-20 from `CCitadel_Ability_Dash`, build 26059233. Per-hero values come from hero
stats (wiki dash buckets: Mina and bucket 1 are 10 m in 0.62 s ground and 8 m in 0.43 s air; the
base hero is 10 m / 0.68 s and 8 m / 0.47 s).

| RVA | Role |
| --- | --- |
| `0x2a16a0` | cast: grounded → set `m_GroundDashExecuteTime` (optionally delayed) and start; airborne → air dash (`0x25bae0`, unread) |
| `0x293f40` | movement callback (interface +0x4c0, slot 4): air-dash steering, ground-dash displacement |
| `0x267400` | ground duration = stat `EGroundDashDuration` (0x5c) / time scale |
| `0x2673d0` | ground distance = stat `EGroundDashDistanceInMeters` (0x5b) / 0.0254 |
| `0x263150` | air distance = stat `EAirDashDistanceInMeters` (0x5d) / 0.0254; air duration is stat 0x5e |
| `0x267450` | evaluates the hero's `m_groundDashPositionCurve` |
| `0xb558a0` | dash scale from slows and modifiers, clamped to `citadel_player_ground_dash_min/max_percent` 0.5–2; 1 by default |
| `0x2a3e40` | dash end: stop, fire modifier event 0x3c |

### B-DSH-01 Ground dash
- **Deadlock** (`0x293f40`): a **position curve, not an impulse**. Each tick, with elapsed time `t`
  (clamped to duration `T`) and the previous tick's `t_prev`:
  `move = D × (C(t/T) − C(t_prev/T)) × dashDir(yaw) × dashScale`, written to `CMoveData +0xf4`.
  `D` is the distance in units and `C` the hero position curve. That vector is the "+0xf4"
  term in ground friction (B-GND-01): friction is skipped while it is set and larger than 1 u, so
  the dash is not braked. `0xb094a0` turns it into velocity each tick: subtract the previous
  tick's dash velocity, add `displacement / dt`. So the dash velocity is layered on the velocity
  the player already had (test T02 from running). Wish speed is 0 while the vector is active, so input
  does not accelerate during a ground dash.
- **Capture:** Mina's 10 m / 0.62 s = 393.70 u / 0.62 s = **635.00 u/s**; measured flat dash peak
  **635.005 u/s**. So the curve's slope is ≈ 1 over the dash (constant speed). After the dash,
  speed decays by ground friction. The 0.719 s spent above 400 u/s fits 0.62 s plus about 8 ticks of
  friction from 635 down to 400.
- **Open:** the exact curve shape; whether pre-dash velocity is cleared at cast (the wiki says ground
  dashes "set velocity to a fixed amount", which conflicts with the layering above); whether the dash
  velocity is removed at the end or left to decay (the post-dash decay in the capture suggests it is left).
- **GAUNTLET:** velocity set to `max(speed, 20 m/s)` for 0.3 s; gravity off; friction ×0.5.
  Deadlock: 16.1 m/s (bucket 1) or 14.7 m/s (base) for 0.62–0.68 s along the dash yaw.

### B-DSH-02 Air dash
Three phases: cast (`0x25bae0`), per tick (`0x293f40`) and end (`0x2b24e0`).
- **Cast:** records cast time and start position, spends `citadel_player_dash_stamina_cost` 1
  and counts consecutive air dashes. The impulse is
  `dir(m_flDashAngle yaw, pitch 0) × airDistance / airDuration` (stats 0x5d / 0.0254 and 0x5e),
  applied through modifier event 0x4c. That is 8 m / 0.43 s = 18.6 m/s (bucket 1) and
  8 / 0.47 = 17.0 m/s (bucket 2), matching the wiki table. The wiki says the impulse combines with
  prior momentum ("can stack with or conflict"); how exactly is in the event-0x4c handler, unread.
  Sets the `"air_dash"` tag and a forward/back/left/right animation from the input angle.
- **Per tick:** progress = time / air duration. The forward/side move input (CMoveData
  +0x2c/+0x30) is overridden so input points along the dash direction relative to the view.
  `vz = max(vz, 100) × dt × m_flDuringDrag`, which suppresses vertical motion (the value of
  `m_flDuringDrag` is not in vdata, so it is a default). Ends when the horizontal distance from the
  start exceeds the air distance, or progress reaches 1.
- **End:** clears the cast state, sets `m_flAirDashDragStartTime`, and starts a post-dash drag for
  `m_flPostDragDuration` 0.1 s. Related values whose rule is still unread: `m_flPostDrag` 4.0,
  `m_flAirSpeedForMinDrag/MaxDrag` 12/14 (m/s), `m_flAirDashEndVelocityScale` 0.2,
  `m_flAirDashAccPct` −40 (air acceleration while dashing?), `m_flGroundDashAirbornDrag` 0.7
  (ground dash after leaving the ground).
- **Limits:** one per airtime by default (`m_nConsecutiveAirDashes`; wiki: two with Vitality or
  Stamina Mastery). Any direction.
- **GAUNTLET:** velocity set to `max(speed, 25 m/s)` for 0.2 s; one per airtime; gravity off.
- **Gap:** an impulse at distance/duration speed, input steering instead of fixed velocity, vertical
  damping instead of gravity off, and post-dash drag.

### B-DSH-03 Down dash
- **Deadlock** (`0x25bae0` with `m_bDownAirDash`): airborne only, triggered by a double crouch tap
  within 28 ticks (0.44 s) (wiki). Costs 0.5 (`citadel_player_dash_down_stamina_cost`), counted
  separately (`m_nConsecutiveDownDashes`; one per airtime by default). Applies a downward velocity of
  `m_flDownwardAirDashSpeed` and ends at once. That speed is not in vdata (default value unread).
- **GAUNTLET:** none.

### B-DSH-04 Dash-jump
- **Deadlock** (`0x25c9f0`, `0x25b970`): jumping while the dash state is active gives a dash-jump.
  It costs **1 stamina** (2 with the dash). It clears `m_bCanDashJump` and **sets** velocity:
  horizontal = dash direction (or current horizontal direction) × the field at JumpVData +0x16f4 ×
  dash scale, and `vz = m_flDashJumpVerticalSpeed` **425**. +0x16f4 has no schema name; it sits
  after `m_flDashJumpDistanceInMeters` 19 m, so it is likely that distance cached in units
  (≈ 748 u/s, 19 m/s), to verify.
- **Window:** opens `m_flDashJumpStartTime` **0.3 s** after a *grounded* dash starts. Per the wiki
  it lasts 0.2 s (0.3–0.5 s) and the stamina bar flashes light blue; vdata `m_flDashJumpEndTime` is
  0.55, so the true end is 0.5 or 0.55 (test T07). Only a ground dash opens it, but you can
  still dash-jump after leaving the ground. Mistiming locks dash-jump out for the rest of that dash
  (wiki). A miss is capped at `m_flDashJumpMissMaxSpeed` 250. Extra air control after a dash-jump:
  `m_flDashJumpExtraAirControlTime` and `m_flDashJumpExtraAirControlPercent`.
- **Open:** where the window and lockout are enforced (in the cast path, not the builder).
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
  `turn × dt × m_flTurnSpeed (90°/s) × scale` degrees, with
  `turn = clamp(AngleDiff(inputYaw, velocityYaw), −1, 1)` (`0x254fc70` wraps the difference to
  ±180°). So the slide turns toward the input direction at the full 90°/s until within 1° of it.
  On flat ground `scale` is a modifier (likely `MOVEMENT_SLIDE_TURN_SCALE`, 1 by default); on slopes it
  is `1 − clamp(|angle term| / 45°, 0, 1)`, so steering weakens on steep slopes. Rotation only, so
  speed is kept.
- **Open:** the slope angle term; `m_flTurnMin/MaxAngDiff` (5 and 2) not seen on this path.
- **GAUNTLET:** heading steer at `slideControlAcceleration`.

### B-SLD-06 Sprint-slide boost
- **Deadlock** (`0x2b0740`): on slide start, if at least 1 s has passed since the last slide change
  and the pawn has state bit 0x40 (likely sprinting), pending boost = `m_flSprintBoostSpeed` 300.
- **Suspect:** as decompiled, the slide-change time is updated just before this check, which
  would make the elapsed time 0 and the boost unreachable. Verify with a sprint-into-slide capture.
- **Context (user, wiki):** sprinting alone does not reach slide speed. Base walk 7.2 m/s plus base
  sprint 1.6 m/s is 8.8 m/s, below the 8.89 m/s (350 u/s) activation threshold (B-SLD-01). Sprint
  only starts after 5 s out of combat (B-GND-05). So a flat-ground slide from running needs a dash, a
  slope or extra speed; the 300 sprint boost (if reachable) applies only after a slide has started.

### B-SLD-07 Landing slide boost
- **Deadlock** (`0x2a35c0`): on landing with fall speed `f`, if wanting to slide, not sliding,
  the slope check passes, and crouch was pressed within `m_flButtonPressWindow` 5 s: start the
  slide. If `f ≥ 300`, pending boost =
  `remap(f, 300, 700, 100, 600) × Bias(remap(slopeDeg, 5°, 45°, 0.25, 1), 0.6)`.
  Flat landings get no boost. `Bias` (`0x2550650`) is Schlick's bias, not Source's power form:
  `Bias(x, b) = x / ((1/b − 2)(1 − x) + 1)`, with x and b clamped to [0, 1].
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
- **Deadlock** (`0x2632e0`): the slide supplies `MODIFIER_VALUE_AIR_DRAG` only after the slide has
  ended **and** from the **second** jump of a slide session (`m_nJumpsThisSlideSession > 1`):
  `drag = remap(time since slide ended, 0, m_flAirDragAfterJumpTime 0.5, m_flAirDragAfterJump 2.0, 0)`.
  Air drag uses `max(citadel_air_drag_min 0.2, this)` (B-AIR-03), so a chained slide-jump starts at
  10× normal air drag and eases back over 0.5 s. The first slide-jump of a session is not penalised.
  `m_flLateSlideJumpWindow` 0.25 (where it is read: not yet seen). How jumps per session are counted and
  reset is not read yet.
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
- **Deadlock:** base hero 3 bars; per hero 2–4 (wiki: Mina 2). Regen 0.222/s, about 4.5 s per bar
  (wiki per-hero cooldowns: Mina 4.2 s, others 3.8–6 s). Grounded/air regen rules and any delay
  are still unknown.
- **Costs** (decoded or convar, wiki agrees): ground or air dash 1; dash-jump +1 (2 total); air
  jump 1 unless free; down-dash 0.5; wall jump 0 for the first, then 0.5. A wall jump in combat
  reduces stamina regen by 25% for 5 s.
- **GAUNTLET:** 3 bars, 1/s after a 1 s delay since the last spend; costs 1/1/1.

## Deferred

B-WAL-01 wall jump (out speeds 225–310, up 525, lateral 100, fatigue recovery),
B-WAL-02 wall slide (terminal velocity 600), B-MNT-01 mantle (max height 134). Keep
`MoveData` room for wall contact and charges.
