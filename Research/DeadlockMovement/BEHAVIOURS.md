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

**Port status** (Unity: `MovementConfig.movementModel = Deadlock`, `SurfController.Deadlock.cs`, tests in
`Assets/Tests/ReplayHarness/DeadlockMovementTests.cs`): `not started` · `specified` (Unity design written) · `implemented` ·
`validated` (Unity trace matches a Deadlock capture within the agreed tolerance).

## Index

| ID | Behaviour | Deadlock | GAUNTLET now | Port |
| --- | --- | --- | --- | --- |
| B-GND-01 | Ground friction | confirmed | same law, different values | implemented |
| B-GND-02 | Ground accelerate | confirmed | Source accelerate, no min-speed clamp | implemented |
| B-GND-03 | Tick order | decoded | friction then accelerate | implemented |
| B-GND-04 | Crouch walking | confirmed (crouch speed) | separate crouch speed/accel/friction | implemented |
| B-GND-05 | Sprint | configured | hidden, unused | not started |
| B-AIR-01 | Gravity | confirmed | constant, suspended while dashing | implemented |
| B-AIR-02 | Air accelerate | decoded | custom steer + accelerate | implemented |
| B-AIR-03 | Air drag | confirmed | none | implemented |
| B-JMP-01 | Ground jump | confirmed | set vy, auto-bhop, beats slide | implemented |
| B-JMP-02 | Air (double) jump | confirmed | set vy after 0.2 s, costs stamina | implemented |
| B-JMP-03 | Late jump (coyote) | confirmed | 0.1 s grace | implemented |
| B-DSH-01 | Ground dash | confirmed | velocity set to max(speed, 20 m/s) | implemented |
| B-DSH-02 | Air dash | confirmed | velocity set to max(speed, 25 m/s), once per air time | implemented |
| B-DSH-03 | Down dash | decoded (partial) | none | not started |
| B-DSH-04 | Dash-jump | confirmed (miss cap decoded) | jump cancels dash | implemented |
| B-SLD-01 | Slide activation | decoded | speed ≥ 9 m/s or on slope, after delay | implemented |
| B-SLD-02 | Slide sustain and exit | confirmed | ends when crouch released | implemented |
| B-SLD-03 | Slide friction and grace | confirmed | linear drag 3 m/s² | implemented |
| B-SLD-04 | Slope sliding | confirmed (27.8–30.7°) | gravity projected down the slope | implemented |
| B-SLD-05 | Slide steering | decoded | heading steer | implemented |
| B-SLD-06 | Sprint-slide boost | decoded (suspect) | none ("never multiplies speed") | not started |
| B-SLD-07 | Landing slide boost | decoded | none | not started |
| B-SLD-08 | Dash-slide | confirmed | none | implemented |
| B-SLD-09 | Slide-jump | decoded | ordinary jump | not started |
| B-SLD-10 | Drop-slide (crouch while falling) | decoded | none | not started |
| B-SLD-11 | Slide cancel and lockout | configured | none | not started |
| B-STA-01 | Stamina pool and regen | configured (wiki) | 3 bars, 1/s after 1 s delay | implemented |
| B-STA-02 | Stamina costs | confirmed | dash 1, air dash 1, double jump 1 | implemented (dash-jump 1, wall jumps 0 then 0.5) |
| B-WAL-01 | Wall jump | confirmed | none | implemented |
| B-WAL-02 | Wall slide | observed (no effect) | none | not needed |
| B-MNT-01 | Mantle | confirmed (path measured) | none | implemented |
| B-MNT-02 | Mantle slide | confirmed (+400) | none | implemented |


## Capture checks, 2026-10-20

`analyze_decoded_checks.py` on `human-20261018-02` (build 26020302; decoding on 26059233), report
`decoded-checks-01.json`. No parameter was fitted.

| Behaviour | Prediction | Measured |
| --- | --- | --- |
| B-GND-02 | +25.0 u/s per tick from rest (400 × 4 / 64) | 25.0 u/s per tick |
| B-GND-01 | release to < 1 u/s: 33 / 34 / 33 ticks | 33 / 34 / 33 ticks; per-tick drop ratio 1.0000 (0.998–1.002). The earlier "1–2 tick gap" came from the pilot's tick counting, not the model |
| B-DSH-01 | 635.0 u/s for 0.62 s, 10 m | 635.005 u/s for 39 full ticks (0.609 s), 9.83 m plus the partial last tick ≈ 10 m; afterwards ground friction to ±0.02 u/s per tick |
| B-AIR-03 | air drag 0.2 | 0.1999–0.2002 over 4 running jumps |
| B-SLD-08 | target 650 after the dash-slide timer, else 450 | crouch ≤ 0.297 s after the dash: speed set to exactly 450.0 (3 trials); ≥ 0.3125 s: exactly 650.0 (13 trials). The timer is 0.3 s after the dash starts. The speed is set before the hooked movement step (it arrives in the step's incoming velocity) |
| B-SLD-03 | slide coefficient 0.175 for 1 s, then 0.8 | 0.172 for the first second, then 0.797, in all 16 trials (within 2%; possibly the slide's friction smoothing, check with the recorder's `friction` field) |
| B-GND-04 | crouch wishspeed `ECrouchSpeed` 4.75 m/s = 187.0 u/s | holding forward while sliding, speed settles at 187.0 u/s: acceleration (25 u/s per tick) beats slide friction at that speed, so a slide held with forward never drops to the 170 sustain stop |
| B-SLD-03 (values) | friction coefficients | recorded `friction` is exactly 11/64 = 0.171875 and 51/64 = 0.796875: the game stores it in 1/64 steps, which explains the 2% (runs 20261020-01 and -04) |

Live session 2026-10-20: `human-tests-20261020-01` (build 26059233; Mina, item-free from 945.97 s) and
`human-tests-20261020-04` (**build 26090082**; Mina, no items). On 26090082 a gate trial reproduced the
dash and dash-slide tick for tick; the schema diff shows no movement-specific field added or removed and
no movement convar default changed. Ticks are listed in each run's `activity-notes.jsonl`.

| Behaviour | Prediction | Measured |
| --- | --- | --- |
| B-JMP-03 | late jump within 0.15 s (9.6 ticks) of leaving ground while falling | walk-offs pressed at ≤ 9 ticks: free ground jump (vz 287.5 after the step); ≥ 10 ticks: air jump costing 1 stamina (12 reps) |
| B-JMP-02 | air jump sets vz; adds horizontal speed along input capped by `m_flMaxSpeedDelta` | vz set to **525.0** = `VerticalSpeed` 300 × (1 + `AirJumpVerticalSpeedPercent` 75%), every rep; horizontal **+200.0 u/s exactly along the input direction** in all 7 sideways reps, whatever the prior momentum, so `m_flMaxSpeedDelta` = 200 |
| B-DSH-01 | ground dash at 635 u/s | the cast tick **zeroes the existing velocity** (step input velocity 0), then 0.2 u/s on the first tick and 635.0 from the second, along the input direction relative to view yaw. From a 319 u/s run: still 635. The wiki's "set velocity to a fixed amount" is right |
| B-DSH-02 | air dash | **not an impulse**: Source air acceleration toward the dash direction with `wish = 732.4 × lerp(1, m_flAirDashEndVelocityScale 0.2, progress)` (732.4 = 8 m / 0.43 s) and `accel = sv_airaccelerate 10 × (1 + m_flAirDashAccPct −40%)`: first-tick gain 68.66 predicted, 68.6 measured; the cap reached was 626.0 at tick 5 from a run and 562.1 at tick 8 from standing, which extrapolates to 732.5 at tick 0. Prior momentum is kept and projected (a backward dash from forward momentum slows through 0 first). `vz` is pinned at 100/64 = 1.5625 (`m_flDuringDrag` = 1); gravity is off for 28 ticks (0.44 s) |
| B-DSH-04 | dash-jump | velocity **set** to vz **425** and horizontal **704.0** along the dash, which is `m_flDashJumpDistanceInMeters` 19 m ÷ the flat flight time 2 × 425 / 800 s = 704.03 u/s (a 19 m dash-jump on flat ground). Works at 0.31–0.33 s into the dash; no dash-jump at 0.14–0.17 s or at 0.56–0.58 s. The exact window end (0.5 or 0.55 s) is open: the user dropped it, since dash-jumps are low priority |
| B-SLD-08 (new build) | 0.3 s timer | crouch ≤ 16 ticks (0.25 s) → 450; ≥ 21 ticks (0.328 s) → 650 (10 reps) |
| B-SLD-02 | stop below 170 u/s with no forward input | dash-slide friction lasts exactly 64 ticks (1.0 s), then 0.8; the slide ends (friction back to 1) on the tick after speed falls below 170 (at 167.6), in 3 of 3 reps |

Live session 2026-10-21: `human-tests-20261021-01` (**build 26097978**; Mina, no items, dl_midtown). The
gate reproduced the dash and dash-slide tick for tick. Per-tick checks ran from scratch scripts against
the step trace and the frame `friction` field. Slope angles come from the trace, not the mesh.

| Behaviour | Prediction | Measured |
| --- | --- | --- |
| B-AIR-02 | air accelerate with cap 80 → 50 (blended over 600–800 u/s), `sv_airaccelerate` 10, after air drag | per-tick velocity residual median 0.010, max 0.021 u/s over 173 low-speed air ticks; 0.014 median over 373 dash-jump ticks at 470–700 u/s, so the speed blend is right. The cap uses speed after drag (both readings fit equally) |
| B-AIR-02 (new) | none | **after a dash-jump, air acceleration is about 1.3× (14.1 vs 10.9 u/s per tick) until about 37–38 ticks (≈ 0.6 s) after the ground dash cast**, then normal. It only shows with sideways input; along the dash, `addspeed` ≤ 0. The ability's `dashing` flag is already off by then. Source field not identified |
| B-DSH-04 (new) | — | a dash-jump costs **1 more stamina** (frame stamina 1.07 → 0.07 on the jump tick, 6 of 6) |
| B-DSH-02 | — | an air dash does not fire if the movement key goes down on the same tick as the dash press (0 of 5, plus 1 in the early trial; no stamina spent). A key held at least one tick earlier always fires. The diagonal-only press fired 5 of 5 |
| B-SLD-03 | slope friction 0.15; flat grace 0.05 for 0.6 s; then 0.8; stop below 170 | recorded 10/64 on the ramp (0.15 rounded to the nearest 1/64), 3/64 = 0.05 from the first tick under 8° for **39 ticks** (0.6 s = 38.4), then 51/64, and the slide ends the tick after speed drops below 170 (165.7). Drops per tick fit `max(v,100) × 4 × c / 64` to 0.02 u/s. Six slides |
| B-SLD-04 | `lerp(500, 850, (deg − 5)/40)` along the downhill direction | **+10.99 u/s per tick, constant**, added between hooked steps (it shows up in the next step's incoming velocity). That is 703 u/s², i.e. a 28.2° surface; the trajectory gives 27–28° (the session's 24.6° came from the walk path). Exact check waits for the ramp's collision normal |
| B-SLD-01 | slope start needs the slope check | crouch-only landings at 0 u/s on the ramp gave no slide (2 of 2); W + crouch landings started one at 80 + 11 u/s. A walk (319 u/s) on flat never starts a slide (2 of 2), consistent with the 350 threshold |
| B-SLD-07 | landing boost ≈ 350 for a fall at 590 u/s on 28° | **no boost** in 3 of 3 slide landings (entry 91 = 80 + slope step). Likely cause: the slide starts through activation on the landing tick, so the landing callback's "not sliding" test fails. Open, low priority |

Stamina regen here was about 0.24 per second (0.0037 per tick).

Open from the live session: some air-dash presses did not execute despite stamina and a movement key
(run -04 ticks 40352, 40385, 49848; ground 61383). `m_flSpeedAdjust` is never visible in after-frame state
(set and consumed within one tick).

Live session 2026-10-26: `human-tests-20261026-01` and `-02` (**build 26224525**; Mina, no items,
dl_midtown; the rapid-regen cheat did not take effect, regen 0.24/s). The gate reproduced the dash and
dash-slide tick for tick in both runs. Checks are per wall jump or mantle against the step trace (the
step's incoming velocity already contains the wall jump or mantle change, like the dash-slide) and the
new frame fields. Scratch scripts: `wj5.py`, `fat.py`, `mt.py` in the session scratchpad.

| Behaviour | Prediction | Measured |
| --- | --- | --- |
| B-WAL-01 out speed | `lerp(225, 310, (facing·n + 1) / 2)` with facing the camera | **facing is the movement input, not the camera**: no input gives 267.5 whatever the view (W01, 12 reps). The input vector is **not normalised** (forward + side, Deadlock side +1 = left): a diagonal has length √2, so it can saturate both the out-speed and lateral lerps at once. With that, 82 of 104 wall jumps fit to < 0.5 u/s; most others fit within a 0.1–4° view-yaw change inside the tick (sub-tick input); 4 remain (8–25 u/s, all with turning) |
| B-WAL-01 vertical | `(vz < 100 ? 0.3 vz : 0) + power × 525` | exact (< 0.01 u/s) in all 104 |
| B-WAL-01 lateral | `+t × lerp(−100, 100, (facing·t + 1)/2) × falloff` | exact with single-axis input; the falloff above 250 u/s along the wall is untested (one rep) |
| B-WAL-01 fatigue | `Bias(remap(t, 0, 1.25, 0.2, 1), 0.2)` | exact to 4 decimals in 19 chained wall jumps with **Schlick's bias** `x / ((1/b − 2)(1 − x) + 1)` (0.5676 at 1.0 s, 0.3797 at 0.797 s, 0.2414 at 0.5625 s, 1 from 1.25 s). The clock is the time since the previous wall jump |
| B-WAL-01 stamina | first free, then 0.5; needs stamina above the cost | first per airtime free (all), then 0.5 each (2 → 1.5 → 1.0, 0.51 → 0.01) |
| B-WAL-01 order | wall jump before air jump | near a wall a press is a wall jump. Wall jumps do **not** use up the air jump (chains wall, wall, air, wall), but they do restart the jump timer (`m_flJumpTime`) |
| B-WAL-01 lateral input suppression | input along the wall ignored for 0.95 s | **not visible**: sideways air acceleration (13 u/s per tick) continues from the first tick after a wall jump |
| B-WAL-01 detection | planes within `m_flCollidedWallMaxDist`, remembered within half of it | not measured directly. vdata `m_flRemapSpeedToWallJumpVelocityDist [300, 600, 10, 30]` is the likely distance (10–30 u by speed). The C07 "edge boost" (482–561 u/s off a corner) comes from the weighted normal rotating 40–45° near the corner, so input along the wall gains out speed |
| B-WAL-02 | terminal velocity 600 while the state is set | `wall_sliding` is set on any airborne wall contact (also rising and mantling), but gravity stays −12.5 u/s per tick and falls pass −600 along walls. No wall slide in practice |
| B-MNT-01 types | by ledge height | the type is the one whose **height is nearest** the ledge (a 124.8 u ledge is type 128, 58.1 u is type 64); `m_flVertOffset` = ledge height − type height. Seen: 45.5 u (type 32), 58–66 u (64), 98–110 u (96), 125–156 u (128) |
| B-MNT-01 path | velocity = path step ÷ dt, gravity off | start tick: velocity 0, no movement. Vertical part for the type's time rounded to ticks (**13, 19, 26, 32 ticks**): rise = type height × t/T plus the offset eased in quadratically over the first half (fits to 0.1 u). The first moving tick moves two ticks' worth. Then **13 ticks** (0.2 s) across, constant speed = distance ÷ 0.2 s (36 u in front: 177.2 u/s), with one tick of vertical overshoot taken back on the first horizontal tick. Exit on the next tick: 46 ticks from start for type 128, 33 for type 64, 27 for type 32 |
| B-MNT-01 trigger | push 0.06 s at < 60 u/s | grounded mantles only up to about 66 u (134 u limit); taller ledges mantle on the tick after a jump (156 u from an airborne start) |
| B-MNT-01 exit | no forward input: velocity 0; else capped at max speed | forward held: 177 u/s, then ordinary ground movement (191 after the exit tick). Released: 0 on 32/64 u mantles; **166 on the 128 u ledge** M-B (probably the "open space" exception), open |
| B-MNT-02 | `m_flSlideExitBoostOnGround` (unread) | **+400 u/s** (177 → 577, 98.5 → 498.4), on the exit tick with crouch and forward held; that tick has ordinary ground friction (577 → 541); the slide starts on the next tick with the normal 0.6 s grace (39 ticks at 3/64, then 51/64). One type-128 rep got +270 (crouch pressed late, 43 ticks before the end), open |
| B-MNT-01 jump | `m_flMantleRefundWindow` 0.1 s? | a jump pressed 3–8 ticks before the exit fires as a free ground jump on the tick after it; 17–22 ticks before is dropped (8 mantles; 9–16 untested) |
| B-SLD-04 | `lerp(500, 850, (deg − 5)/40)` per second | with the recorded ground normal: residual median 0.01 u/s per tick on 27.8°, 28.3° and 30.7° downhill slides (137 ticks). A 19.3° ramp is 1.0 u/s per tick low (110 ticks), open |


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
- **Scope (user, 2026-10-20):** sprint is only a base move-speed increase; no further decoding needed.

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
- **GAUNTLET:** implemented in `DeadlockJumps`: inside the window (0.3–0.55 s) velocity is set to 704 along the
  dash and vz 425 for 1 stamina, with 1.3× air acceleration until 0.6 s after the dash. A ground or late jump during
  the dash outside the window keeps at most 250 u/s and locks the dash-jump out (decoded, untested).

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
- **GAUNTLET:** legacy: heading steer at `slideControlAcceleration`. Deadlock model: `DeadlockSlideSteer`, 90°/s
  with the slope term taken as the slope angle (decoded, untested).

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

## Walls and mantle

Decoded 2026-10-23 on build 26059233 (RVAs below). The schema on 26147187 has the same fields.
Values come from the 25841406 vdata export and the 26147187 convars; the 26147187 vdata could not
be exported (the pinned Source 2 Viewer rejects a new KV3 type). Checked against the 2026-10-26 captures
(build 26224525, "Live session 2026-10-26" above); corrections from that check are marked **(capture)**.

| RVA | Role |
| --- | --- |
| `0x2a1790` | jump execute: picks the jump type (`0x25b970`), runs it, records `m_LastJumpType` |
| `0x25b970` | jump type: dash-jump if dashing (pawn state bit 9); wall jump if a wall is detected and stamina allows; air jump if airborne; else ground |
| `0x26c200` | wall jump possible: airborne and `m_vCurrentWallNormal` valid (not the FLT_MAX sentinel) |
| `0x2aed00` | wall detection: collects nearby planes, weights them, keeps a short memory of the last wall |
| `0x25d6a0` | wall jump: direction, out speed, up speed, lateral speed, fatigue, stamina |
| `0x26afb0` | wall-jump power (fatigue) |
| `0x2a6020` | jump think: landing resets, fatigue recovery during other moves |
| `0x2a1b00` | mantle start: ledge search (`0x257d20`), start/target positions, mantle type, zero velocity |
| `0x295070` | mantle movement callback: moves along the path, gravity off, exit handling and mantle-slide boost |

### B-WAL-01 Wall jump
- **Trigger:** a jump press while airborne with a valid wall normal. It is checked before the air jump,
  so near a wall a jump press is a wall jump, not a double jump. A dash-jump (while dashing) still wins.
- **Stamina:** the first `citadel_wall_jump_num_free_wall_jumps` (1) consecutive wall jumps cost
  `citadel_initial_wall_jump_stamina_cost` (0); after that `citadel_wall_jump_stamina_cost` (0.5). A wall
  jump needs current stamina strictly above its cost. `m_nConsecutiveWallJumps` counts them (reset on landing).
- **Wall detection** (`0x2aed00`): planes within `m_flCollidedWallMaxDist` of the hull whose normal is at
  most 45° from horizontal (|n.z| ≤ 0.707) count as walls. Each is weighted 1 at contact, falling to 0 at
  the maximum distance. The weighted sum is flattened to horizontal and normalised. A trace of
  `citadel_wall_detection_skin` (8 u) toward the wall confirms it; one surface type rejects it. With no
  wall this tick, the last valid normal is kept while the player is within half the detection distance
  of where it was seen (a short wall "coyote" time).
- **Direction:** `dir = normalize(c × n̂ + (1 − c) × n̂_horizontal)`, with n̂ the wall normal and
  `c = citadel_corner_boost_strength` 0.6. Velocity into the wall is removed first (`v −= dir × min(0, v·dir)`).
- **Out speed:** `out = lerp(m_flWallJumpMinOutSpeed 225, MaxOutSpeed 310, clamp((facing·dir + 1) × 0.5, 0, 1))`.
  **(capture)** `facing` is the movement input (forward + side, not normalised), not the camera: input away
  from the wall gives 310, no input 267.5, into the wall 225. `v.xy += dir.xy × out`.
- **Vertical:** `vz = out × dir.z + (vz < 100 ? 0.3 × vz : 0) + power × m_flWallJumpUpSpeed 525`. Falling
  speed is kept at 30%; upward speed of 100 or more is dropped.
- **Lateral:** along the wall's tangent `t = (−dir.y, dir.x, 0)`, target `= lerp(−100, 100, clamp((facing·t + 1) × 0.5, 0, 1))`
  (`m_flWallJumpMaxLateralSpeed` 100). It is scaled by the curve `m_WallJumpLateralSpeedFalloffVsAlongSpeed`
  (1 at 250 u/s along the wall, 0 at 550), evaluated at |v·t|, or at −|v·t| when the target opposes the
  current along-wall motion. Then `v += t × target × falloff`.
- **Fatigue:** `power = Bias(remap_clamped(now − m_flWallJumpFatigueStartTime, 0, m_flWallJumpFullPowerRechargeTime 1.25, m_flWallJumpPowerMin 0.2, 1), m_flWallJumpPowerBias 0.2)`.
  Each wall jump restarts the fatigue clock: an immediate second wall jump has power ≈ 0.06 (up speed ≈ 31),
  full power returns after 1.25 s. `citadel_wall_jump_fatigue_recovery` (1) scales recovery during heavy
  melee or air dash.
- **After:** lateral input is suppressed until `now + m_flWallJumpLateralInputSuppressTime` 0.95 s
  (**(capture)** no effect seen on air acceleration); the current and last valid normals are cleared;
  `m_vWallJumpNormalUsed` and `m_flLastWallJumpFatigueStrength` are stored. The jump timer restarts; the air
  jump stays available.
- **GAUNTLET:** `SurfController.DeadlockWalls.cs` (`UpdateDeadlockWall`, `TryDeadlockWallJump`). Detection casts
  the hull a speed-dependent 10–30 u in 8 fixed horizontal directions, weights hits by distance, confirms with
  a trace toward the wall and keeps the last wall within half the distance. Out speed, vertical, lateral
  (Hermite falloff from vdata), Schlick-bias fatigue and stamina as above. No lateral input suppression.
  Values under Deadlock > Wall jump.

### B-WAL-02 Wall slide
- **Deadlock** (`0xb076c0`, B-AIR-03), located only: while pawn state bit 8 (likely
  `MODIFIER_STATE_WALLSLIDE`) is set and falling, vz is eased toward `citadel_wall_slide_terminal_velocity` 600.
  What sets the state is not read. `citadel_wall_ride_enabled` is false ("obsolete"), so wall running is
  off in this build. **(capture)** The state is set on any airborne wall contact, but falls are not slowed:
  no wall slide in practice.
- **GAUNTLET:** none, not needed.

### B-MNT-01 Mantle
- **Ledge search:** `citadel_mantle_probe_depth` 32 u ahead, up to `citadel_mantle_max_height` 134 above.
  Auto-mantle skips ledges whose drop just past the lip (`citadel_mantle_auto_step_check_inset` 4 u) is within
  step height (`sv_stepsize` 18), so stairs are walked, not mantled. `citadel_mantle_horizontal_movement_distance`
  16 u is how far onto the ledge it moves you. `m_flAutoMantlePushTime` 0.06 s of pushing into a ledge
  and `m_flAutoMantleBlockedMaxSpeed` 60 gate the auto-mantle.
- **Types** (by ledge height, `MantleType_t`): 32 (≤ 64 u), 64 (32–96 u, 0.3 s), 96 (64–128 u, 0.4 s up,
  0.2 s across), 128 (96–160 u, 0.5 s). Unset times are class defaults (unread).
- **Start** (`0x2a1b00`): stores start position, target = start + ledge offset, facing, type index and
  start time; sets velocity to zero; tags "mantle".
- **Movement** (`0x295070`): each tick for the type's vertical time (rounded to ticks), velocity is set to
  the path step ÷ dt and the step goes into the CMoveData move vector (`+0xf4`, the same slot as the ground
  dash). mv flag `+0x112` is set, so gravity is off. Pulling away cancels only if
  `citadel_mantle_cancelling_allowed` (false).
- **Exit:** with no forward input, velocity is zeroed unless the probe finds open space. Horizontal speed is
  then capped at the hero's max move speed.
- **(capture)** Type = nearest type height to the ledge; ground mantles up to 134 u, airborne up to 160 u.
  Path: start tick still, then vertical for 13/19/26/32 ticks (type height linear plus the offset eased
  quadratically over the first half), then 13 ticks across at distance ÷ 0.2 s (36 u: 177 u/s), exit on
  the next tick. A jump pressed up to 8 ticks before the exit fires on the tick after it.
- **GAUNTLET:** `TryStartDeadlockMantle`, `StepDeadlockMantle`, `FinishDeadlockMantle`. Pushing into a wall
  within the probe depth at under 60 u/s for 0.06 s starts a ledge search: the hull is dropped from the
  highest mantle height onto the ledge 36 u past the wall, then rise and cross are checked for room. The
  path follows the capture; exit with forward keeps 177 u/s, without it stops (the 166 u/s type-128
  case is not modelled). Gravity off; the mantle cancels dash and slide.

### B-MNT-02 Mantle slide
- **Deadlock** (`0x295070`, exit): if crouch (button 4, or the slide button) is held, speed is forward and
  mantling is not blocked, the mantle type's `m_flSlideExitBoostOnGround` is added along the horizontal
  velocity, and the slide takes over. The boost values are class defaults, not in the vdata export, so
  they must be measured. **(capture)** +400 u/s on the exit tick (which has ordinary ground friction); the
  slide starts the next tick with the normal grace.
- **GAUNTLET:** `FinishDeadlockMantle` adds `mantleSlideBoost` and defers the slide one tick.

### Dash-jump air control (B-AIR-02 addendum)
The 1.3× air acceleration measured on 21 October matches `CAbilityJumpVData::m_flDashJumpExtraAirControlTime`
and `…Percent` (class defaults, not in vdata): about +30% until about 0.6 s after the dash.
