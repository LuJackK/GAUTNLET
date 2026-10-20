# Deadlock → GAUNTLET movement mapping

Started 2026-10-20. Living document: one row per value, filled as each is
decoded or measured. Behaviours (rules, triggers, interactions) are tracked in [BEHAVIOURS.md](BEHAVIOURS.md). Rules come from [the static analysis](STATIC_ANALYSIS.md); measured
values from [the human pilot](capture/HUMAN_PILOT_ANALYSIS.md); configured values from the
`build-25841406-initial` vdata export (older build; re-extract on 26059233 before final values).

## Decisions

1. **Literal mapping.** Deadlock's rules, values and world scale are the baseline. GAUNTLET's
   current tuning and map proportions are not; levels will be rebuilt to Deadlock scale.
2. **Scale: 1 Source unit = 0.0254 m, decoded.** The server converts metre-valued stats with a
   shared helper (`0x3c58a0`, divides by 0.0254). Convert lengths, speeds and accelerations by
   ×0.0254. Times, stamina counts and friction coefficients are unchanged (friction is 1/s:
   `drop = speed × friction × dt`).
3. **Reference hero: open.** Values differ per hero. Proposal: base-hero values in
   `MovementConfig`, per-hero profiles later. The captures are Mina + Superior Stamina.
4. **Tick rate: open.** Deadlock simulates at 64 Hz (observed). No GAUNTLET scene sets FishNet's
   `_tickRate`, so it is presumably FishNet's default **30 Hz**. Rules in seconds carry over;
   per-tick discretisation does not: walk-to-95% is 23 ticks (0.359 s) at 64 Hz and
   11 ticks (0.367 s) at 30 Hz. Short windows (dash-slide 0.3 s, one-tick boosts) lose
   resolution at 30 Hz. Recommendation: 64 Hz if the netcode budget allows, else 60.

Status: **decoded** (read in code, matches capture where checked), **vdata** (configured,
rule not yet read), **measured**, **pending** (not yet known).

## Ground movement

| Behaviour / value | Deadlock | Metres | GAUNTLET now | Change | Status |
| --- | --- | --- | --- | --- | --- |
| Order | friction, then accelerate | — | same | none | decoded |
| Friction law | `max(speed, stop) × friction × dt`, horizontal, zero below 0.1 u/s | 0.00254 m/s | same law (`SurfPhysics.Friction`) | add zero-below-threshold | decoded |
| Friction coefficient | `sv_friction` 4 × surface × 2 undecoded multipliers | 4 | `friction` 6 | → 4 | decoded (multipliers pending) |
| Stop speed | `sv_stopspeed` 100 | 2.54 m/s | `deceleration` 10 | → 2.54 | decoded |
| Accelerate amount | `max(wish, 400) × accel × surface × dt` | min 10.16 m/s | `acceleration × wish × dt` | **code: add min-accel-speed clamp** (new field 10.16) | decoded |
| Ground accel | hero stat `EMoveAcceleration` 4 | 4 | `acceleration` 14 | → 4 | decoded |
| Walk speed | hero `EMaxMoveSpeed`: base 7.2 m/s (vdata), Mina measured 318.909 u/s | 7.2 / 8.10 | `walkSpeed` 7 | → per reference hero | vdata + measured |
| Sprint | `ESprintSpeed` 1.6 (base) | — | `sprintSpeed` 12 (hidden) | pending rule | vdata |
| Crouch speed | `ECrouchSpeed` 4.75 (base) | 4.75? | `crouchSpeed` 4 | pending units/rule | vdata |

## Air and jumping

| Behaviour / value | Deadlock | Metres | GAUNTLET now | Change | Status |
| --- | --- | --- | --- | --- | --- |
| Gravity | `sv_gravity` 800 | 20.32 m/s² | `gravity` 20 | → 20.32 | decoded + measured |
| Air accelerate | `wish × sv_airaccelerate(10) × 0.25 × dt` | — | Steer + `airAcceleration` 12 | pending air-move read (`0xb07140`) before changing | decoded (partial) |
| Air drag | `citadel_air_drag_min` 0.2, slows scale air drag 0.35 | — | none | pending (`0xb076c0`) | pending |
| Jump impulse | first airborne vz 287.485 (one tick after takeoff); rise 53.81 u | ≈7.3 m/s; 1.37 m rise | `jumpForce` 6.5 | pending decode of impulse | measured |
| Double / air jump | `citadel_air_jumps_enabled`, `AirJumpVerticalSpeedPercent` scale | — | `doubleJumpForce` 8, delay 0.2 | pending | vdata |
| Dash-jump | vertical 425, miss max speed 250 | 10.8 m/s, 6.35 m/s | none | pending | vdata |
| Coyote time | unknown | — | `coyoteTime` 0.1 | pending: may not exist in Deadlock | pending |

## Dash

| Behaviour / value | Deadlock | Metres | GAUNTLET now | Change | Status |
| --- | --- | --- | --- | --- | --- |
| Model | distance over duration along `m_groundDashPositionCurve` (to verify) | — | velocity set to max(speed, impulse) | **code: likely curve-driven** | vdata |
| Ground dash | 10 m over 0.68 s (base); measured peak 635 u/s | 16.1 m/s peak | `dashImpulse` 20, 0.3 s | pending | vdata + measured |
| Air dash | 8 m over 0.47 s (base) | — | `airDashVelocity` 25, 0.2 s | pending | vdata |
| Ground dash speed range | min/max percent 0.5 / 2 | — | none | pending | decoded (convar only) |
| Stamina cost | ground 1, down-dash 0.5 | — | 1 / 1 | → 1 / 0.5 | decoded (convar) |

## Slide

| Behaviour / value | Deadlock | Metres | GAUNTLET now | Change | Status |
| --- | --- | --- | --- | --- | --- |
| Model | friction with grace periods, slope accel, boosts, steering | — | linear `slideDrag` 3 | **code: replace** | vdata (rule pending) |
| Activation speed | 350 | 8.89 m/s | `minimumSlideSpeed` 9 | → 8.89 | vdata |
| Sustain speed | 170 | 4.32 m/s | none | add | vdata |
| Flat friction / grace | 0.8; grace 0.05 for 0.6 s (0.75 s after landing) | — | none | add | vdata |
| Sprint boost on entry | 300 | 7.62 m/s | none ("never multiplies speed") | add | vdata |
| Landing boost | 100–600 by landing speed | 2.54–15.2 m/s | none | add | vdata |
| Dash-slide | start within 0.3 s → 650 u/s, friction 0.175 for 1 s | 16.5 m/s | none | add | vdata |
| Turn speed | 90 | deg/s? | `slideControlAcceleration` 10 | pending units | vdata |
| Slide delay | `citadel_slide_delay` 0.15 | — | `slideDelay` 0.5 | → 0.15 (meaning pending) | decoded (convar) |

## Stamina

| Behaviour / value | Deadlock | GAUNTLET now | Change | Status |
| --- | --- | --- | --- | --- |
| Max stamina | `EStamina` 3 | `maxStamina` 3 | none | vdata |
| Regen | `EStaminaRegenPerSecond` 0.222 (≈4.5 s per bar) | 1 per second | → 0.222 | vdata (rule pending) |
| Regen delay / grounded rules | unknown | `staminaRegenDelay` 1.0 | pending | pending |

## GAUNTLET-only behaviour to decide on

These exist in GAUNTLET with no known Deadlock equivalent. Keep, remove, or wait for decoding:
`airControlAcceleration` steering, `autoBhop`, `coyoteTime`, `maxVelocity` 50 m/s cap,
`dashFrictionOverride`, heavy melee and parry (GAUNTLET's own; out of scope).
