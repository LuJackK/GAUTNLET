# Deterministic Replay Harness Plan

Date: 2026-03-30
Owner: Codex + Luka
Scope: isolated replay harness package for surf movement hardening ahead of host-authoritative networking.

---

## Goal

Build a removable, isolated test package that can answer two first-pass questions:

1. Does the local simulation produce the same result for the same recorded input trace when replayed from the same start state?
2. Does the local predicted timeline stay aligned with an authoritative timeline when delayed authoritative corrections are applied back into rollback/replay?

This first pass intentionally excludes:
- ladders
- crouching
- underwater

This first pass intentionally includes:
- dash
- double jump
- heavy melee

---

## Package Boundary

Everything for the harness should live under:

- `Assets/Tests/ReplayHarness/`

Design rules:

- Do not modify gameplay scenes to support the harness.
- Prefer runtime-built disposable arena/setup over persistent test scenes.
- Avoid production code edits unless a seam is truly required.
- If a production seam becomes necessary, keep it tiny, explicit, and easy to delete later.

---

## Resolved Decisions

### Runner shape

Use a headless-style PlayMode harness, not pure EditMode.

Reason:
- the character simulation depends on runtime component setup
- collision queries depend on a live scene/physics world
- this keeps the harness close to the real movement stack while remaining disposable

### Pass/fail criteria

Use explicit replay-critical field comparisons as the main gate:

- position
- velocity
- grounded
- jump count / jump timer
- dash state / dash timers
- melee state / melee timers
- look yaw/pitch

Keep a quantized fingerprint/checksum as debug output only, not as the primary pass/fail contract.

### Timeline coverage

First pass covers both:

- same local replay
- predicted vs authoritative with delayed corrections

The predicted vs authoritative harness will be simulated locally by:

1. generating an authoritative reference run from the input trace
2. replaying the same trace through rollback
3. delivering delayed authoritative states back into the rollback manager
4. checking that the corrected predicted run converges to the same per-tick outcome

### Trace source

Target trace source is captured live input.

Development can begin with synthetic traces so the package is runnable before live capture tooling is finished.

---

## Milestones

## Phase 1 - Core Harness Skeleton

- [x] Create tracked markdown plan
- [x] Create isolated trace and result types
- [x] Create runtime-built minimal deterministic arena
- [x] Create harness runner for same-trace replay
- [x] Create harness runner for delayed authoritative correction replay

Exit criteria:
- A replay trace can be executed locally in isolation without touching gameplay scenes.

## Phase 2 - First Determinism Assertions

- [x] Compare replay run A vs replay run B from the same start state
- [x] Compare authoritative reference run vs predicted-with-corrections run
- [x] Report first mismatch frame and field diff
- [x] Include rollback/correction metrics in reports
- [x] Split final convergence from transient prediction divergence detection

Exit criteria:
- The harness can tell us the first frame where determinism or reconciliation diverges.

## Phase 3 - Live Input Capture

- [x] Add capture component or utility for live local input traces
- [ ] Record fixed tick, buttons, stick axes, just-pressed bits, and look angles
- [ ] Store initial pose/config metadata with the trace

Exit criteria:
- We can capture a real gameplay input trace and replay it through the harness.

## Phase 4 - Hardening

- [ ] Add curated golden traces for dash, double jump, and heavy melee
- [ ] Add longer endurance trace
- [ ] Add latency presets for authoritative correction delay
- [ ] Add optional debug export for mismatch reports
- [~] Add a second bootstrap profile that uses the real player prefab instead of a hand-built replay character
  Current status:
  The harness can instantiate the real `Assets/Prefabs/Player 1.prefab` path, but the new lane still needs a clean PlayMode rerun from a free editor session to validate the behavior end-to-end.

Exit criteria:
- The harness is useful for routine netcode hardening work instead of only one-off debugging.

---

## First-Pass Implementation Notes

### Minimal deterministic arena

The arena should be created at runtime and contain only:

- stable flat ground
- a controlled spawn point
- one replay character

Avoid:

- water volumes
- ladders
- moving platforms
- extra triggers

### Bootstrap profiles

The harness now has two intended bootstrap profiles:

- synthetic disposable replay character
- real player prefab bootstrap using the actual movement/network component stack

The synthetic profile stays useful for fast isolation.
The real prefab profile is the next step toward reproducing live session desync without needing a full host/client scene.

### Authoritative reference run

The first authoritative reference does not require a real network session.

It is simply the clean reference run of the same input trace from the same initial state.

### Predicted-with-corrections run

The correction run should:

1. tick local prediction forward every frame using the trace
2. inject delayed authoritative frames from the reference run
3. call observe/reconcile on rollback
4. compare the resulting corrected state stream against the authoritative reference

This lets us exercise rollback/replay semantics without needing a full host/client test harness yet.

---

## Known Risks

1. Heavy melee currently still touches live hitbox query state.
   First pass should avoid requiring a hit target to validate melee movement/state progression.

2. Water and trigger state are still scene-driven.
   They are excluded from first-pass determinism coverage.

3. Unity test assembly wiring may need an extra pass.
   The runtime harness should come first; test-runner packaging can follow if Unity assembly discovery needs adjustment.

4. `LocalInputCollector.GatherInput()` is stateful.
   Live capture should use a deliberate seam or recorder contract, not a naive second sampler that could disturb edge-button latching.

5. Final convergence can hide transient divergence.
   The harness should keep both signals:
   one test for "eventual reconcile convergence"
   and one test for "no transient checksum/state mismatch before reconcile catches up."

6. Batch test runs can be blocked by editor-only startup state.
   Scene recovery popups, editor recompiles, or an already-open Unity instance can prevent headless reruns even when the harness code itself is valid.

---

## Cleanup Plan

If the harness is no longer needed, cleanup should be as simple as deleting:

- `Assets/Tests/ReplayHarness/`
- this markdown plan if desired

The package should not require permanent scene, prefab, or gameplay-path changes.
