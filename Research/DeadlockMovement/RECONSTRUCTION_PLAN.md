# Deadlock movement reconstruction plan

Researched: 2026-10-13. Status: research and experiment design; no gameplay implementation changes.

Update, 2026-10-18: the first human recording is finalized. See the
[pilot analysis](capture/HUMAN_PILOT_ANALYSIS.md) for empirical fits and limits,
and the [advanced capture plan](capture/ADVANCED_CAPTURE_PLAN.md) for the current
execution order: improve capture, advanced human tests, then finalize the input
runner and collect targeted automatic trials. The original phases below remain
the wider reconstruction framework; the initial capture pilot is no longer pending.

## Objective and scope

Recover a documented movement model from the installed Deadlock build and reproduce its observable behavior in GAUNTLET's Unity movement motor. A model includes equations, units, operation order, state transitions, input timing windows, collision behavior, and modifiers. Matching a few configured speeds is insufficient.

Start with walking/braking, jumping/air control, ground/air dash, sliding, and dash-slide-jump combinations. Then investigate wall jumps, steps, ledges, crouch clearance, and mantling. Treat ropes, ziplines, melee movement, hero abilities, and item-modified movement as separate later modules. A module is only claimed as reproduced after its validation set passes.

## What is established locally

- Installed Steam build was checked as 25841406 during the preceding inventory. Recheck before capture; a Steam build ID is different from an engine source revision or demo header build number.
- `research.py doctor` successfully runs the pinned Source 2 Viewer CLI 20.0.
- `local/runs/build-25841406-initial/manifest.json` records a successful ability/hero extraction. Both exported files matched their manifest SHA-256 hashes when checked.
- The initial export contains explicit slide friction/grace settings, slope settings, dash-slide settings, jump timing windows, and wall-jump parameters.
- `heroes.vdata` contains meter-labelled ground/air dash distances and durations plus `m_groundDashPositionCurve` (hero base at lines 51-57). Those suggest a distance/curve model worth testing; they do not establish executable behavior or the effective values for every hero.
- `abilities.vdata` contains dash-jump start/end settings at lines 1835-1837 and air-dash end velocity scaling at line 2222. These are configured values, not demonstrated timing or momentum equations.
- Unity already has movement state sampling in `Assets/Tests/ReplayHarness/ReplaySample.cs`, tick execution in `ReplayHarnessRunner.cs`, and input recording in `ReplayTraceRecorder.cs`.
- The existing Python comparison consumes only `time_s,speed_mps`. It has no Deadlock capture adapter, automatic event alignment, model fitting, or full trajectory comparison.

No real reference trials or parser compatibility test were performed for this planning task. Live console command availability, demo contents, units, update cadence, and input timestamps remain pilot questions.

## Source 2: what matters for the port

Investigate three layers independently:

1. **Game movement rules:** intended direction, acceleration, braking, jumping, dash curves, slide boosts, input windows, stamina, and modifiers.
2. **Collision queries and motor:** collider shape, movement sweeps, walkable normals, ground attachment, stepping, clipping, penetration recovery, and slope probes.
3. **Observation/presentation:** simulation clocks, network replication, client prediction, interpolation, third-person camera lag, FOV changes, and animation.

Valve publishes Source 1 movement code with friction, directional acceleration, gravity integration, sweeps, and step handling. Use it as a source of candidate models, not proof of Deadlock's equations. [Valve movement source](https://github.com/ValveSoftware/source-sdk-2013/blob/master/src/game/shared/gamemovement.cpp).

Deadlock's schema exposes a distinct movement-services component and recipient-specific network fields. This supports studying the movement layer separately, but field layouts do not reveal function bodies or guarantee availability in a demo. [Movement services](https://s2v.app/SchemaExplorer/deadlock/server/CCitadelPlayer_MovementServices), [player pawn](https://s2v.app/SchemaExplorer/deadlock/server/CCitadelPlayerPawn).

Valve's physics talks cover contact and debugging techniques; they are background material, not a Deadlock controller specification. [Valve publications](https://www.valvesoftware.com/en/publications).

The research did not locate a public complete Deadlock movement implementation or a complete specification of Source 2's movement/physics execution. We can still construct and validate a behaviorally equivalent model. Reproducing the whole rigid-body solver is outside the initial scope. Unity's current custom swept motor is the appropriate integration point; collision parity must be measured independently from free movement.

## Phase 1: capture feasibility pilot

Before collecting a large dataset, record a short local/private test: stand still, run straight, release input, jump without directional input, dash on flat ground, then dash-slide. Fix hero, equipment, upgrades, controls, map, and server conditions. Preserve a baseline without movement modifiers.

### Preferred route: demos plus synchronized video

Try a locally recorded demo first. `DemoFile.Net` documents Deadlock support and entity updates. Use it as the first parser candidate, as a standalone research utility outside Unity. `deadem` is an alternative with a documented input-message example. Pin the chosen package version/commit after the pilot succeeds. [DemoFile.Net](https://github.com/saul/demofile-net), [deadem](https://github.com/Igor-Losev/deadem/blob/main/packages/deadem/README.md), [input example](https://github.com/Igor-Losev/deadem/blob/main/packages/examples-node-deadem/scripts/109_parse_input_activity.js).

The alternative's stated compatibility range is not a match guarantee for our installation; its demo build numbers cannot be directly compared to Steam build IDs. Success means parsing our own pilot and verifying its observables, not merely installing a parser.

Inspect which pawn position, velocity, stance, ability, stamina, and input fields actually occur. Check whether user-command buttons, view direction, and finer timestamps are present for the tested player. Export absolute world position rather than camera origin; resolve any cell offsets or parent transforms used by the format. Keep network velocity, position-derived velocity, and rendered HUD velocity as separate measurements until their meanings agree.

Entity deltas must be reconstructed into state while retaining when each observable was actually updated. A held value is not proof of a fresh per-tick measurement. Distinguish absent, stale, reconstructed, quantized, and directly observed data. Local-player-exclusive fields may be missing from spectator/server recordings. A demo is recorded observable state, not necessarily every internal simulation state or input.

Record video alongside the pilot to verify event ordering and transitions. Prefer stable 120 fps if the machine sustains it; retain actual frame timestamps and report dropped/duplicate frames. A 120 fps frame spans about 8.3 ms, but the state it displays may update more slowly. Video frame rate does not establish simulation tick rate.

### Candidate native commands to verify

The inspected Deadlock command inventory lists `record`, `cl_showpos`, `cl_showtick`, `getpos_exact`, and bot input recording/playback commands. Test their flags, syntax, displayed fields, and release-build availability locally before building scripts around them. `getpos_exact` is a spot reading, not a demonstrated continuous telemetry exporter. Bot playback could improve repeatability if the pilot verifies the same starting state and inputs. [Deadlock command inventory](https://s2v.app/SchemaExplorer/deadlock/convars).

Also inventory movement-related console values locally, without altering the baseline. A variable's presence or default does not prove the Deadlock ability path uses it.

### Fallback if demos are incomplete

Capture readable position/speed HUD values and input-event markers in video. Use numeric overlays rather than distance inferred from camera perspective. Manually check representative OCR samples and quantify display rounding. If only positions are available, estimate velocity over a documented time window and fit models to positions rather than repeatedly differentiating noisy samples.

If input times are only visually estimated, report interval uncertainty and use coarser windows. Do not claim exact tick boundaries from such recordings. If neither demo nor video supplies adequate precision, stop fine-grained inference and investigate a specific alternative capture method before the full campaign.

### Pilot acceptance criteria

- Correct hero/entity tracked; stationary position stable within quantified resolution.
- Actual timestamps and update cadence identified; pauses and gaps tagged.
- Straight-run displacement agrees with integrated velocity within estimated error, accounting for discrete integration and any known observation phase.
- Jump apex and dash/slide events agree between state and video within event uncertainty.
- Coordinate axes, position origin, distance scale, and speed definition documented.
- Input timing precise enough for the intended experiment, or explicitly limited.
- The same setup can be reset and repeated at least five times.

Deliver `local/experiments/capture-pilot/` with raw capture, provenance, exported observations, and a short verdict selecting a capture method. Do not collect hundreds of trials before this passes.

## Measurement contract

Preserve raw values and convert into a canonical coordinate system with vertical +Y, metres, seconds, degrees, and velocity vectors. Determine axis signs with simple forward/right/jump trials. Choose an explicit distance conversion after calibration; do not assume a Source unit equals a Unity metre or import a generic Source conversion without checking Deadlock's conventions.

Use meter-labelled ability distances as calibration candidates and cross-check multiple distances/conditions. A configured dash distance may not equal achieved displacement after collisions, modifiers, or early exit. If independent physical scale cannot be established, retain Source units and mark the Unity scale as a design choice until validated.

The future common trace format should contain:

| Category | Required information |
| --- | --- |
| Provenance | Steam build, demo build/revision if available, hashes, parser/capture version, map, hero, items/upgrades, controls, server context |
| Clock | Trial time, simulation/demo tick when available, command time when available, raw sample time, update age, gaps |
| Kinematics | Position xyz, velocity xyz when available, horizontal speed, total speed, view yaw/pitch |
| Inputs | Movement axes, press/release/held states for jump/dash/crouch, event-time uncertainty, observed versus inferred input |
| State | Grounded, stance, dash/slide/jump/ability phases, stamina if exposed, each with availability/quality |
| Environment | Start position, collision contact/normal if available, measured slope, material, wall/ledge identity and geometry |
| Quality | Direct/reconstructed/derived/missing status, rounding, timing error, trial exclusions with reason |

Missing values remain missing. No invented normals, stamina, or hidden timers. Keep the existing speed-only CSV as a compatibility export, not the master measurement record.

## Phase 2: isolated and combination measurement campaign

Start with five repeats per condition. Add repeats when variability or confidence is insufficient. Vary initial speed, input direction, and terrain deliberately. Randomize condition order within a session where practical. Reset starting location, heading, stamina, velocity, and active modifiers; verify the reset rather than assuming teleport clears all state.

| Experiment | Controlled variations and recorded outputs | Question answered |
| --- | --- | --- |
| Walk, crouch, sprint | From rest; forward/side/back/diagonal; release and reversal; speed and heading over time | Acceleration law, directional limits, input normalization, braking, sprint onset |
| Jump/fall | Stationary jump; walk off ledge; several drop heights; apex, vertical velocity, landing time | Gravity, initial impulse, integration phase, horizontal retention, ground grace |
| Air control | No input, perpendicular/reverse input, view turns at low/high carried speed | Acceleration versus rotation-only steering, drag, caps, camera/input coupling |
| Ground/air dash | Rest and low/high speed; aligned/perpendicular/opposed input; release, turn, cancel; full displacement and velocity curves | Velocity set/add/minimum-speed rule versus distance curve, duration, end scaling, gravity lock |
| Flat slide | Low/medium/high start speed; no steering; running entry versus landing entry; continue to exit | Fixed deceleration versus proportional friction, grace duration, activation versus sustain threshold |
| Slope slide | Several measured uphill/downhill slopes; sideways traversal; flat-ramp seams | Slope acceleration/caps, uphill friction, direction dependence, forward probes and sampled normals |
| Slide steering | Side input and look-only changes at several speeds; measure heading plus speed | Fixed angular turn limit versus acceleration-based steering; steering speed loss |
| Dash-slide/dash-jump | Sweep negative and positive input delays relative to dash onset and eligibility; compare pre-eligibility taps, holds through transition, fresh eligible presses, and simultaneous inputs | Success/failure windows, buffering, held-input activation, boosts, lockouts, action priority |
| Slide-jump/landing | Jump at early/middle/late slide; several landing speeds/heights; hold versus press crouch | Momentum transfer, temporary air drag, landing boost, buffer/grace rules |
| Full dash-slide-jump chain | Cross crouch-to-dash and jump-to-slide input delays at several initial speeds; include cancellations, pre-eligibility taps/holds, and simultaneous jump/dash/crouch | Combined momentum transfer, transition order, cancellation, and interactions between timing windows |
| Wall jump/mantle | Several approach speeds and angles, repeated wall jumps, ledge heights/clearance | Out/up/lateral velocity rules, recharge, detection thresholds, mantle trajectory/refunds |
| Collision/grounding | Walls/corners, ramps, steps, ceilings, ledges; several speeds and headings | Collider envelope, clipping, stepping, snap distance, slope classification, stance blocking |
| Stamina | Single action and repeated actions; grounded/airborne recovery and relevant cancels | Costs, recovery delay/rate, refunds, action eligibility |

For timing sweeps, sample both sides of the reference event (for example -0.5 to +0.8 s relative to dash onset), extend the range if outcomes have not stabilized, locate changes, then refine to one observed simulation interval if capture permits. Also record delay relative to the observed eligibility transition, such as landing, slide entry, or the end of a lockout. Keep input time and action activation time separate: an earlier input may activate later. The extracted timing fields guide where to sample more densely; they are not assumed pass/fail boundaries. Regress against actual observed command delay, not the operator's intended delay.

At each relevant boundary, compare a press-and-release completed before eligibility, a press held through the transition, and a fresh press after eligibility. Include release-time variations and a no-input control. Compare simultaneous jump/dash, dash/crouch, jump/crouch, and jump/dash/crouch presses with nearby trials that reverse their input order. Verify same-command/same-tick timing when observable; if capture cannot resolve order, report the interval uncertainty and leave exact action priority unresolved. Record the resulting action order, activation/cancellation, velocity before and after each transition, and stamina changes. A pre-eligibility tap that activates after release supports buffering; activation only while held supports a held-input rule, subject to the other controls. If an early press changes the starting state, retain that result and compare matched-state controls rather than treating it as an otherwise identical trial.

Before fitting the first combination model, collect dash-slide, dash-jump, slide-jump, and full dash-slide-jump trials, including failed/cancelled attempts. Reserve whole timing conditions and entire trials for validation: negative-delay taps, held-through-transition cases, simultaneous-input cases, and delays inside/outside the candidate success windows. Use distinct fitting and withheld delays within each input family, and withhold joint timing combinations for the full chain. Set this split before fitting; neither the reserved trajectories nor their action outcomes may be used to tune parameters or priority rules. Combination momentum and cancellation cannot be inferred from isolated dash and slide trials alone.

For controlled slopes, use map collision geometry or validated surface probes if obtainable. Otherwise measure angle and uncertainty from coordinate samples on a genuinely planar patch, accounting for pawn origin and ground snap. Visual mesh appearance alone does not establish the collision normal. Start on existing sandbox/map geometry; a custom Deadlock test map is optional and must first be shown to load with usable collision.

## Phase 3: mathematical conclusions

Treat movement as a stateful discrete system:

`state[k+1] = F(state[k], input[k], environment[k], dt[k]; parameters)`.

Fit both the continuous evolution within a mode and the conditions that switch modes. Hidden grace timers and action phases must be reconstructed from event history or labelled unresolved. A speed-only curve cannot establish the whole state machine.

### Example: identify flat-slide friction

Compare these hypotheses in identical flat, no-input trials:

- Fixed deceleration: `v_next = max(0, v - a*dt)`.
- Proportional decay: `v_next = max(0, v*(1 - k*dt))`; an exact exponential update is a separate integration hypothesis.
- Threshold friction: `v_next = max(0, v - mu*max(v, stopSpeed)*dt)`.
- Piecewise rules: different parameters during entry/landing/dash grace, followed by the base rule.

The threshold family is motivated by Valve's Source 1 code; its use in Deadlock must be tested. Compare multiple starting speeds and inspect the low-speed tail. Equal absolute loss across speeds favors fixed drag; equal fractional loss favors proportional drag. Slope trials are separate so downhill acceleration cannot masquerade as reduced friction.

### Example: infer dash evolution

Test a velocity impulse/set model against the candidate `distance(t) = D*C(clamp(t/T,0,1))`, using the exported position curve `C`, effective distance `D`, and duration `T`. Compare whole displacement, not just average speed. Repeat above and below dash speed and in different directions to reveal how pre-existing momentum is composed and what happens at dash end. Determine effective hero/modifier values before comparing.

### Example: jump and steering

Use `height(t) = height0 + jumpVelocity*t - 0.5*g*t^2` as a free-flight baseline. Fit positions away from takeoff/landing; use vertical velocity and different drop heights to cross-check. Then test discrete integration order and any mode-dependent gravity or end-of-dash reset. A parabola alone does not establish the exact per-tick update.

For steering, compare heading change and speed change together. A fixed turn-rate rule predicts similar heading increments at different speeds. Fixed lateral acceleration generally gives a heading rate that decreases as speed increases. Directional acceleration can change both speed and heading; camera rotation without movement input distinguishes input-relative from view-driven rules.

### Fitting and validation procedure

1. Fix baseline configuration and units before fitting. Resolve resource inheritance, scale functions, hero overrides, and active modifiers; preserve the evidence for effective values.
2. Segment by observed mode/event. Compare plausible equations and operation orders, including jump versus friction, gravity versus displacement, and dash versus slide transitions.
3. Fit full forward-simulated trajectories to observations at their real timestamps. Use position, velocity, heading, event timing, and action outcomes where observable. Weight errors by measured uncertainty; avoid noisy acceleration as the main fitting target.
4. Use constrained least squares with sensible bounds. Robust losses may limit isolated capture errors, but do not use them to hide systematic mismatch. [SciPy fitting reference](https://docs.scipy.org/doc/scipy/reference/generated/scipy.optimize.least_squares.html).
5. Hold out entire trials and at least one speed/slope/heading condition relevant to the module. For combination models, also hold out whole input-delay conditions and joint timing combinations using the Phase 2 split. Neighboring frames from one trial are not independent validation data. Prefer the simplest model that predicts withheld conditions, action outcomes, and event boundaries.
6. Plot residuals against time, speed, slope, input angle, and mode. Persistent structure indicates a missing rule, clock mismatch, or wrong model. Do not retune constants separately for each trial.
7. Estimate parameter uncertainty across independent trials, such as bootstrapping complete trials. Do not resample correlated frames as independent observations. Report observational resolution and alternative models that remain indistinguishable.
8. If two models fit equally well, design a new trial that makes their predictions diverge. If achievable precision cannot distinguish them, choose a documented approximation rather than claiming recovery of the original equation.

Every conclusion must state its build, effective configuration, equation, units, operation order, supported domain, evidence, uncertainty, and remaining alternatives. Use the existing `EXPERIMENT_TEMPLATE.md` as the initial record format.

## Phase 4: Unity comparison and implementation plan

Only after the pilot and baseline experiments, implement the first validated module. Extend the research tools with a Deadlock trace adapter, common trajectory format, Unity sample exporter, and fitting/report generation. Preserve their separation from the shipped game.

Use existing `ReplayHarnessRunner` and `ReplayStateSample` to export simulated state after each tick with the actual tick time. Recreate simple measured surfaces in an isolated test scene. Existing replay tests check our game's consistency, not Deadlock parity.

First compare free trajectories and isolated flat movement. Then compare collision, slopes, and transitions. Collision disagreement should not be compensated by changing acceleration. Match shape, ground handling, sweep behavior, and contact tolerances separately; Unity query semantics also matter. [Unity CapsuleCast reference](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Physics.CapsuleCast.html).

Run at the measured reference simulation interval where possible, then at the production GAUNTLET interval. Report any timing-window or distance change caused by different discretization. Assess input quantization and any finer event timing only if observed; do not assume CS2's timing architecture applies to Deadlock. Server/local simulation comes before network prediction and visual smoothing comparisons.

Persistent movement timers/flags must live in `MoveData` and be included in rollback state and serialization. Follow the gameplay/multiplayer folder contracts and update serializers/tests when implementing new states. Presentation consumes simulation events and does not decide success windows.

Choose acceptance tolerances before evaluating a candidate, from pilot resolution plus an explicitly chosen design margin. If capture noise exceeds the needed gameplay precision, improve capture rather than widen tolerance silently. Report position error, horizontal/vector velocity error, heading error, jump height/time, dash/slide distance, transition timing, and action/stamina outcomes. RMSE alone cannot approve a model with the wrong timing boundary. Mark unavailable metrics unverified.

Validate new conditions without per-trial retuning, then run appropriate existing movement and rollback tests. Camera lag, FOV, animation, and audio are a later feel pass against their own reference observations.

## Escalation: focused executable analysis

Use static executable analysis only for a named question that survives the measurement campaign, such as which friction multiplier is applied or how sampled slope normals are combined. Save a hashed build-specific binary copy and tool/version provenance under `local/`. Identify the actual game movement call path, not merely an engine function with a familiar name. Confirm inferred branches against experiments.

Ghidra/REA are not installed or configured by this plan. Generic physics-engine investigation becomes worthwhile only when a measured collision discrepancy cannot be explained by shape, geometry, stepping, timing, or query differences.

## Deliverables and order of work

| Stage | Deliverable | Proceed when |
| --- | --- | --- |
| 1. Capture pilot | One repeatable trace, field inventory, clock/unit/noise report, selected pinned parser/capture route | Observable data supports the precision we need |
| 2. Evidence inventory | Movement parameter/curve table with effective-value resolution and evidence links | Baseline hero/modifiers and unresolved defaults are explicit |
| 3. Baseline and combination experiments | Run/release, jump, dash, flat slide, dash-slide, dash-jump, slide-jump, and full dash-slide-jump; negative-delay taps, holds through eligibility, simultaneous inputs, and cancellations; five repeats per condition to start; preselected withheld speed/timing conditions | Isolated and combined trajectories, momentum transfer, action order, and cancellation outcomes are repeatable; capture precision supports timing inference; fitting and validation sets are separated |
| 4. First model | Equations/state transitions plus withheld-trial report for dash-slide-jump, including buffering/held-input behavior and action priority where resolvable | Model predicts withheld speed conditions, input delays and joint timing combinations, momentum transfer, cancellation, and action outcomes within chosen tolerances; unresolved rules remain explicitly unverified |
| 5. Terrain/collision | Slope, steering, landing, step/ledge/clearance validation | Geometry and motor behavior explain discrepancies |
| 6. First Unity module | Isolated replay comparison and rollback-compatible implementation | Reference comparisons and relevant regression checks pass |
| 7. Extensions | Wall jumps/mantle, then separately chosen traversal/combat/modified modules | Each module has its own measured and validated contract |

The selected first route is the [native Modlock capture MVP](capture/README.md), using our paired before/after observer and raw engine timing fields without a LockTimer dependency. Build and synthetic validation precede the first human pilot. That pilot must establish installed-build compatibility, pawn identity, units, clock semantics and completeness before fitting equations. Demos and video remain independent cross-checks or fallbacks if native observations prove unreliable. This plan does not authorize claims of exact code recovery from data alone.

## Documentation map and limits

| Resource | Useful for | Limit |
| --- | --- | --- |
| [Source 2 Viewer CLI](https://s2v.app/ValveResourceFormat/guides/command-line.html) | Resource extraction and inspecting maps/models/data | Exports do not reconstruct executable movement logic; current docs may exceed pinned 20.0 capabilities |
| [Slide schema](https://s2v.app/SchemaExplorer/deadlock/server/CAbilitySlideVData) | Parameter intent, slope/friction/grace/probe metadata | Public schema revision may differ from our installed build; schema fields are not equations |
| [Movement services](https://s2v.app/SchemaExplorer/deadlock/server/CCitadelPlayer_MovementServices) | Field layout and networking metadata | Not all fields are replicated to every recipient |
| [Valve Source 1 movement code](https://github.com/ValveSoftware/source-sdk-2013/blob/master/src/game/shared/gamemovement.cpp) | Candidate update/collision patterns | Different engine/game; no automatic parity assumption |
| [Valve publications](https://www.valvesoftware.com/en/publications) | Physics debugging and contact-generation background | Not a Deadlock gameplay specification |
| [Valve Developer Community Source 2](https://developer.valvesoftware.com/wiki/Source_2) | Community engine/tool documentation locator | The page could not be read through the web tool in this research; no claims rely on it |
| [DemoFile.Net](https://github.com/saul/demofile-net) | First Deadlock demo parser candidate | Requires a matching-build pilot, field coverage and timestamp checks |
| [deadem input example](https://github.com/Igor-Losev/deadem/blob/main/packages/examples-node-deadem/scripts/109_parse_input_activity.js) | Alternative user-command parsing route | Must verify actual inputs and finer timings present in our capture |

Keep raw game resources, binary copies, demos, and captures under ignored `local/`. Commit tooling, synthetic tests, this plan, and reviewed conclusions with provenance. Record fetched schema/parser revisions so future updates cannot silently alter the interpretation of an old trial.
