# Automated movement experiments

## Objective and scope

Replace repeated manual timing attempts with reproducible input experiments in
the private local Deadlock server. Deadlock must compute movement itself. The
runner must never implement a desired trajectory by writing velocity or position
during measurement. Unity movement changes remain out of scope.

The first milestone is a strict trial compiler and an independent verifier of
requested versus observed inputs. Live execution is a separate acceptance gate:
the native command path must preserve ability eligibility, stamina, prediction,
and ordinary movement. An offline test passing does not validate that path.

Mina plus Superior Stamina is the primary human reference. Danny bunnyhop trials
are a separate configuration, including recorded hero transitions. Ziplines and
vent boosts are excluded for now. Corner boosts use horizontal geometry edges;
edge boosts use vertical geometry edges. These are geometry-dependent wall-jump
behaviors to investigate, not special velocity impulses to hard-code.

## What every trial must preserve

1. Provenance: game build, map, plugin/SDK hashes, protocol version, run/trial ID,
   actual hero, items when known, world/pawn/session identity, and synthetic flag.
2. Initial conditions: actual position, velocity, ground support, movement mode,
   view angles, stamina and relevant ability/recharge state. Unknown is distinct
   from zero. Do not assume cooldown readiness from stamina alone.
3. Inputs: logical requested press/release/axes/view schedule, injection phase,
   requested engine ticks, actual application ticks, raw held/changed/scroll
   masks, and inputs independently observed at native processing. Keep physical
   device labels as user reports; a wheel event is not automatically a perfect hop.
4. Motion: per-step before/after position and velocity, raw command timing and
   fractions, engine clock, per-frame states, landing/takeoff transitions and
   supporting entity. Preserve duplicate/substep samples rather than average them.
5. Outcomes: action accepted/rejected/unknown; speed and direction changes,
   vertical impulse, apex, airtime, distance, slide onset/exit, resource changes,
   and trial failure reason. User success labels and computed classifications
   remain separate.
6. Quality: missing/duplicate observations, dropped samples, deadline misses,
   identity changes, actual starting-condition differences, interruptions,
   interventions, resets, and final input release. Failed trials remain evidence.

For geometry experiments add contact point/normal, collision plane/shape and
wall-jump eligibility observations when a verified read-only seam is available.
The current recorder does not expose these. Positions and velocity changes alone
cannot establish the collision algorithm. Do not invent normals from a peak.

## Lifecycle and safeguards

Prepare -> settle -> check preconditions -> arm -> execute -> release -> settle
-> verify -> save. Any failure transitions to release and an explicit failed or
indeterminate result, never a successful trial with skipped events.

Use whole engine ticks first, with observed timing rather than an assumed wall
clock frequency. Refuse skipped ticks, wrong target, respawn, world change,
unknown readiness, unexpected human input, or unaccounted input state. Bound all
waits and trial lengths. Repeat executions must start from matched observed state.
Any reset is a logged intervention outside the measured interval. First live
tests can use manually established starting positions; automatic resets are not
a prerequisite for proving input fidelity.

Use an explicit loopback host and an opt-in target slot/identity. Keep normal
recording read-only. A runner adapter must report raw input before intervention,
the applied request, and downstream observed input. Neutral release on completion
and abort must be verified. Stop testing if release cannot be established.

## Execution phases

### A. Offline contract and analysis

Implement strict declarative trials, deterministic compilation, explicit release
events, bounded schedules and immutable output files. Verify observed traces
against compiled trials using an explicit, versioned button mapping rather than
guessing that a traditional Source jump bit is the Deadlock ability jump.

### B. Native input seam calibration

Audit command processing order. A ProcessMovement callback alone may occur after
ability input has been consumed. AbilityThink button filtering alone does not
establish matching axis/view command submission. Do not silently combine these
as an equivalent client input path. Instrument phase ordering and prove that
press, release, held and scroll semantics reach the intended native consumers.

Start with no-input, walking/release, one jump, then one dash. Compare requested
and observed events and human trajectories. Include abort, pawn loss, timeout,
held-input release and insufficient-stamina rejection. Bot AI with zero recorded
axes was observed in earlier probes and is not an accepted baseline.

### C. Repeatability and focused sweeps

Repeat each calibrated basic trial at least five times and in an independent
session. Report spread and actual initial conditions. Then collect:

- Coyote timing: matched ledge run-offs, jump delays around the observed 141 ms
  ordinary-jump and 172 ms double-jump examples. These samples do not prove the
  precise cutoff; preserve last-grounded and first-airborne timestamps.
- Bunnyhop timing: matched entry speed, jump delays around landing, no steering
  versus fixed steering versus coordinated view changes. Distinguish ground
  friction loss from airborne loss. Keep heroes separate.
- Double jump: ascent/apex/descent and entry-speed sweeps; test horizontal speed
  changes, not only vertical launch. Human data shows an immediate vertical
  speed near 564 in sampled Mina trials, but this is not an exact universal rule.
- Dash/jump/slide combinations: coarse timing grid followed by boundary sweeps,
  cancellations, failed eligibility, and resource costs.
- Air steering and drag: fixed entry speed, controlled axes and camera paths.
- Slopes and wall/corner/edge boosts: measured geometry, matched approaches,
  ordinary wall-jump controls and near misses. Geometry instrumentation precedes
  claims about why a boost occurs.

### D. Reconstruction validation

Fit candidate rules on designated clean trials. Reserve other delays, entry
speeds, slopes and sessions as held-out tests. Use mixed free movement as a
behavioral reference, not an unlabelled source of exact constants. Compare the
full trajectory, transitions and accepted actions as well as endpoint error.

## Human capture status at handoff

The latest corner-boost mantle trial ended with two user-reported failures;
individual alignment remains to be reviewed. Its session
`human-boosts-20261019-01` finished cleanly and both file validators passed.
The preceding `human-doublejump-20261019-01` session was interrupted without
clean closing records and must retain that quality limitation. No controlled
vertical-edge boost block has been completed. Do not describe either missing
data or planned automation as already validated.
