# Test triage for the next testing session

Written 2026-10-20. Every test confirms or settles a behaviour in [BEHAVIOURS.md](BEHAVIOURS.md),
referenced by ID. Decoded rules are hypotheses until a capture agrees. Build **26059233**:
recheck the Steam build and `modlock-sigcheck` before starting; recapture on a new build.

**Priority:** **P0** blocks porting a core behaviour · **P1** settles an open detail of a behaviour we
will port · **P2** nice to have or deferred behaviour. **Kind:** *analysis* = existing captures, no game
needed · *capture* = new Deadlock recording · *unity* = GAUNTLET test after the port.

## Session setup

- **Hero:** one hero with **no items**, recorded with its dash bucket. Mina is bucket 1 with
  2 stamina bars; a 3-bar bucket-2 hero (e.g. the base-like Abrams/Warden profile) gives more
  stamina headroom. Earlier captures used Mina + Superior Stamina; do not mix the two.
- **Stamina:** keep it real for stamina tests. For non-stamina tests, cheats
  `citadel_rapid_stamina_regen 1` avoids waiting, but record that it was on.
- **Ground:** one marked flat strip, at least 30 m, for walk, dash and slide. For T11, two ramps
  with measured angles (one around 10–15°, one around 30°+), plus a ledge 2–4 m high for T06 and T09.
- **Input:** human input, with chat markers as now. Inputs in each trial should be clean (one action,
  then hands off), with 5 repetitions per condition.

## Recorder prerequisites (P0, before capturing)

**Status 2026-10-20: implemented and smoke-tested.** Built from the regenerated
`capture/modlock-observer.patch`; native tests, the synthetic lifecycle check and a live bot probe
(`local/captures/solo-smoke-20261020-01`, 1,290 frames, no losses) pass. In the probe, every ability
resolved its RTTI class, and all 26 slide, dash and jump fields populated. `friction` and
`surface_friction` read 1 and `dash_vector` stayed null, because the bot never slid or dashed.
**First trial of the session (T00):** one flat dash, then one dash-slide. Confirm that `dash_vector` is
non-null during the dash, that `friction` changes during the slide (0.175 → 0.8), and that
`m_bIsSliding`, `m_flSpeedAdjust`, `m_GroundDashExecuteTime` move as expected. See
[the recorder guide](capture/README.md#movement-detail-fields-20-october-upgrade).

Original requirement:

Add these to the observer from the decoded layouts (offsets from build 26059233 `types.json`; resolve
by schema name, not hard-coded offsets):

| Field | Source | Needed by |
| --- | --- | --- |
| slide active, pending boost, flat-start, dash-slide-start, ground-start times | `CCitadel_Ability_Slide` `m_bIsSliding`, `m_flSpeedAdjust`, `m_flSlidingOnFlatStartTime`, `m_flDashSlideStartTime`, `m_flOnGroundStartTime` | T03, T04, T10, T11 |
| pawn friction coefficient | `CBaseEntity::m_flFriction` (pawn +0x3fc, set by `0x1cf4de0`) | T01, T04 |
| dash displacement vector | `CMoveData` +0xf4..+0xfc (pre and post step) | T02, T01 |
| ground and air dash timing and counters | `CCitadel_Ability_Dash` `m_GroundDashExecuteTime`, `m_flAirDashCastTime`, `m_flAirDashDragStartTime`, `m_nConsecutiveAirDashes`, `m_bDownAirDash` | T02, T07, T08, T09 |
| jump type and counters | `CCitadel_Ability_Jump` `m_LastJumpType`, `m_nExecutedAirJumpCount`, `m_flLastOnGroundTime`, `m_bCanDashJump` | T05, T06, T07 |
| surface friction | movement services `m_flSurfaceFriction` | T01 |

`sliding` is currently always `null` in `frames.jsonl`, so this table is what makes slide tests decisive.

## Analysis results (2026-10-20)

T01, T02 (dash shape), T02b and T04 (piecewise regimes) **passed** on existing data, and T03's main
question (dash-slide timer = 0.3 s) is **answered** from the 16 pilot dash-slides. See
[BEHAVIOURS.md](BEHAVIOURS.md#capture-checks-2026-10-20). Remaining capture work for these:
T01 none; T02 only the from-a-run variant; T03 only a confirmation pair (0.28 s and 0.32 s) on build
26059233; T04 a no-forward-input slide to see the 170 sustain stop, and the recorder's `friction`
field for the 0.172 vs 0.175 difference.

## Live session results (2026-10-20)

Runs `human-tests-20261020-01` (build 26059233) and `-04` (build 26090082, after a mid-session update;
a gate trial reproduced dash and dash-slide exactly). P0 done: T00 passed (all recorder fields work;
`m_flSpeedAdjust` is consumed within a tick, so frames show 0). **T06, T05, T08, T02 (run variant), T03
pair and T04 confirmed** the decoded rules; T07 was partial by user decision (dash-jump is low priority;
the window end, 0.5 or 0.55 s, is open). New exact values: air jump vz 525 and +200 horizontal
(`m_flMaxSpeedDelta`); dash-jump horizontal 704 (19 m flight); the air dash as decaying-wish air
acceleration; the ground dash zeroes prior velocity. See
[BEHAVIOURS.md](BEHAVIOURS.md#capture-checks-2026-10-20). Still open: why some air-dash presses did
not execute (run -04 ticks 40352, 40385, 49848, 61383). P1 tests not run. Use
`python trace_ticks.py <capture> <tick>[:before:after]` for per-tick tables.

## Tests

| ID | Pri | Kind | Behaviour | What to do | Pass / what it settles |
| --- | --- | --- | --- | --- | --- |
| T01 | P0 | analysis, then capture | B-GND-01 | Re-time release-to-stop in `human-20261018-02` from the exact release tick (input-sample phase). If the gap remains, capture 5 walk-release trials with surface friction logged | Model 34 ticks vs capture 32–33: find the missing tick(s) or the extra multiplier |
| T02 | P0 | analysis, then capture | B-DSH-01 | Per-tick horizontal speed and distance of the 3 flat dashes in `human-20261018-02`; then capture dashes from a run (forward, sideways, backwards relative to the run) | Constant 635 u/s for 0.62 s (curve slope ≈ 1), 10 m total; then decay by friction 4 / stop 100. From a run: is pre-dash velocity kept and added (decoded layering) or replaced (wiki)? |
| T02b | P0 | analysis | B-AIR-03 | Horizontal speed through the airborne part of the pilot's 4 running jumps and the dash-crouch-jump trials | Decay by (1 − 0.2/64) per tick above 100 u/s, with no input |
| T03 | P0 | capture | B-SLD-08 | Ground dash, then crouch at about 0.1, 0.2, 0.25, 0.3, 0.35, 0.5 s into the dash | Entry speed 450 before the dash-slide timer and 650 after; locates `m_flGroundDashSlideTime` (expected 0.3 s) |
| T04 | P0 | analysis, then capture | B-SLD-03, B-SLD-02 | Refit existing dash-crouch trials piecewise; then hold crouch after a dash-slide until the slide stops | Decay 0.7/s for 1 s, then 3.2/s; stops below 170 u/s on flat; friction coefficient in the recorder matches |
| T05 | P0 | capture | B-JMP-02 | Standing double jump (no input), then jump forward and air-jump sideways | Air jump sets vz = 300 (not 225, the 75%); horizontal redirect gain per jump (gives `m_flMaxSpeedDelta`); 1 stamina per air jump |
| T06 | P0 | capture | B-JMP-03 | Walk off a ledge and press jump about 0.05, 0.1, 0.15, 0.2 s after leaving; also jump-then-jump on flat | Ground jump with no stamina within 0.15 s while falling; air jump with stamina after |
| T07 | P0 | capture | B-DSH-04 | Ground dash, then jump at about 0.2, 0.3, 0.4, 0.5, 0.55, 0.6 s into it; repeat with a second press after a miss | Dash-jump vz 425 inside the window; horizontal speed (≈ 748 u/s?) settles JumpVData +0x16f4; window end 0.5 or 0.55; miss cap 250; lockout after a mistimed press |
| T08 | P0 | capture | B-DSH-02 | From a standing jump at apex: air dash forward, sideways, backwards; also from a running jump (prior momentum along and against) | Impulse 18.6 m/s (bucket 1) or 17.0 m/s; how it combines with prior velocity; vz during the dash; end speed (scale 0.2?) and 0.1 s post-drag; 8 m distance cap |
| T09 | P1 | capture | B-DSH-03 | From a jump, double-tap crouch within 0.44 s | Downward speed (`m_flDownwardAirDashSpeed` default), 0.5 stamina, one per airtime |
| T10 | P1 | capture | B-SLD-06, B-GND-05 | Stay out of combat 5+ s so sprint is active, run, then dash-slide and slope-slide | Whether the +300 sprint boost happens (suspected unreachable); sprint speed ramp (first 2.5 m/s at once, then 0.6 m/s per s) |
| T11 | P1 | capture | B-SLD-04, B-SLD-07, B-SLD-10 | On the measured ramps: slide down from a standstill; drop onto the ramp from heights giving fall speeds of about 300, 500, 700 u/s with crouch pressed in the air; repeat landing on flat | Slope acceleration lerp(500, 850) with cap lerp(1200, 1500) by angle; landing boost remap(fall, 300–700 → 100–600) × slope bias; no boost on flat |
| T12 | P1 | capture | B-SLD-05 | Flat dash-slide with full left and full right input, mouse still | Yaw rate 90°/s and speed kept; settles the `turn` function |
| T13 | P1 | capture | B-SLD-01 | Running at walk or sprint speed on flat, press crouch; then dash and crouch at speeds just above and below 250 | No slide below 350 u/s on flat (sprint alone 8.8 m/s cannot); dash threshold 250 |
| T14 | P1 | capture | B-SLD-09 | Slide-jump at slide start, mid-slide, and within 0.25 s after the slide ends | Air drag after a slide-jump (0.5 s); late slide-jump window 0.25 s |
| T15 | P1 | capture | B-AIR-02, B-AIR-03, B-GND-03 | In the air: hold one strafe key with no mouse turn; then strafe while turning the mouse; at carried speeds below 600 and above 800 u/s (use a dash or dash-jump). Also rapid A/D tapping on the ground and in the air | Wish-speed cap 80 u/s below 600, 50 above 800; air accelerate 10; air drag on both axes; whether the strafe-reversal factor (0.3 s window, 0.15 s ramp) reduces acceleration |
| T16 | P2 | capture | B-WAL-01, B-WAL-02, B-MNT-01 | Deferred behaviours | Only after the core port |

## Unity tests after porting (kind *unity*)

These lock each confirmed rule into the GAUNTLET test suite at the Deadlock tick (64 Hz) and at the
production tick rate, in metres (×0.0254):

| ID | Behaviour | Assertion |
| --- | --- | --- |
| U01 | B-GND-02, B-GND-03 | From rest at wishspeed 318.909 u/s: 95% in 23 ticks at 64 Hz |
| U02 | B-GND-01 | Release-to-stop tick count (value from T01) |
| U03 | B-DSH-01 | Ground dash covers 10 m in 0.62 s at a constant 635 u/s (bucket 1); friction suspended during the dash |
| U04 | B-SLD-08 | Dash-slide entry speed 650 (450 if early); 642.9 one tick later at 64 Hz |
| U05 | B-SLD-03 | Flat-slide friction regimes and the 170 u/s sustain stop |
| U06 | B-JMP-01 | Ground jump sets vz 300 and keeps horizontal velocity; 287.5 after one tick at 64 Hz |
| U07 | B-JMP-03 | Late jump within 0.15 s only while falling; free |
| U08 | B-DSH-04 | Dash-jump window and costs (values from T07) |
| U09 | B-STA-02 | Stamina costs: dash 1, dash-jump +1, air jump 1, down-dash 0.5 |
| U10 | all new state | Every new timer and flag survives `MoveData` rollback and serialization |

## Suggested order

1. Recorder prerequisites, smoke-tested on one trial each.
2. Analysis-only T01, T02 (first part), T02b, T04 (no game needed; can happen before the session).
3. Capture P0 in this order: T03, T07, T05, T06, T08, then T04's capture part.
4. P1 as time allows: T15, T13, T10, T11 (needs the ramps), T12, T14, T09.
