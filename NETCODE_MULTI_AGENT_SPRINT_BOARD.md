# Netcode Multi-Agent Sprint Board

## Objective
Stabilize the networking stack by separating deterministic gameplay simulation from Unity presentation, collapsing duplicate authority paths, and making rollback/reconciliation measurable and replay-safe.

This board is designed for parallel agent work with minimal merge conflicts.

---

## Root Problem

The current desync and snapping behavior is coming from three interacting issues:

1. The project is still running a hybrid networking architecture.
   - `NetworkedCharacter` contains both a legacy owner/server broadcast flow and a FishNet prediction/reconcile flow.
2. The rollback layer is replaying code that still mutates live Unity objects.
   - Simulation code currently touches transforms, colliders, child local positions, and camera/view state.
3. Presentation and gameplay boundaries are still blurred.
   - More than one system writes pose and look state, and some visual systems still feed back into gameplay state.

---

## Source Of Truth

These files are the core work surface:

- `Assets/Scripts/SourcePhysics/Movement/NetworkedCharacter.cs`
- `Assets/Scripts/SourcePhysics/Movement/RollbackManager.cs`
- `Assets/Scripts/SourcePhysics/Movement/SurfCharacter.cs`
- `Assets/Scripts/SourcePhysics/Movement/SurfController.cs`
- `Assets/Scripts/SourcePhysics/Movement/MoveData.cs`
- `Assets/Scripts/SourcePhysics/Movement/InputFrame.cs`
- `Assets/Scripts/SourcePhysics/Combat/Hitbox.cs`
- `Assets/Scripts/SourcePhysics/Combat/Hurtbox.cs`
- `Assets/Scripts/SourcePhysics/Movement/CharacterRenderer.cs`
- `Assets/Scripts/SourcePhysics/PlayerAiming.cs`
- `Assets/Scripts/PlayerVFXController.cs`

---

## Agent Model

Use 5 agents with hard file ownership.

### Agent A - Network Authority Consolidation
Owns:
- `Assets/Scripts/SourcePhysics/Movement/NetworkedCharacter.cs`

Mission:
- Make one authority path the real runtime path.
- Keep the losing path behind legacy flags until final cleanup.
- Ensure there is exactly:
  - one tick ingress
  - one reconcile path
  - one proxy correction path
  - one look-authority path

Must not:
- Change gameplay simulation rules in `SurfCharacter` or `SurfController`.

Definition of done:
- No duplicate pose writers remain in `NetworkedCharacter`.
- Legacy/FishNet path ownership is explicit and gated.
- Authority flow is documented in code comments and this file.

### Agent B - Rollback And Replay Correctness
Owns:
- `Assets/Scripts/SourcePhysics/Movement/RollbackManager.cs`
- `Assets/Scripts/SourcePhysics/Movement/InputFrame.cs`

Mission:
- Separate predicted local timeline from confirmed authoritative timeline.
- Make replay ownership explicit and deterministic.
- Add replay diagnostics and checksum support.

Must not:
- Touch render interpolation or camera code.

Definition of done:
- Rollback buffers clearly distinguish predicted, confirmed, and replayed frames.
- `ReconcileAuthoritativeFrame` is replay-safe.
- Replay metrics exist:
  - correction count
  - rollback count
  - max prediction lead
  - state checksum/hash

### Agent C - Pure Simulation Boundary
Owns:
- `Assets/Scripts/SourcePhysics/Movement/MoveData.cs`
- `Assets/Scripts/SourcePhysics/Movement/SurfCharacter.cs`
- `Assets/Scripts/SourcePhysics/Movement/SurfController.cs`

Mission:
- Make authoritative simulation rewindable.
- Remove live Unity object mutation from simulation.
- Separate authoritative state from runtime handles and visual state.

Must not:
- Rewrite network transport logic in `NetworkedCharacter`.

Definition of done:
- Authoritative sim no longer mutates:
  - child local positions
  - camera local position
  - presentation-only transforms
- `MoveData` no longer carries scene-object references as rollback-critical state.
- Sim code consumes pure input and query results, then emits state/events.

### Agent D - Environment And Combat Query Extraction
Owns:
- `Assets/Scripts/SourcePhysics/Movement/SurfController.cs`
- `Assets/Scripts/SourcePhysics/Combat/Hitbox.cs`
- `Assets/Scripts/SourcePhysics/Combat/Hurtbox.cs`
- Trace/query helper files as needed

Mission:
- Replace direct scene-object dependence with deterministic query results.
- Move hit detection and hit dedupe toward sim-owned data.
- Keep VFX/audio triggers out of rollback replay.

Must not:
- Reintroduce transform writes into simulation.

Definition of done:
- Ground/water/ladder/contact checks return stable query results.
- Combat side effects are event-driven rather than scene-driven.
- Hit dedupe survives rollback without hidden runtime caches.

### Agent E - Presentation Isolation And Validation
Owns:
- `Assets/Scripts/SourcePhysics/Movement/CharacterRenderer.cs`
- `Assets/Scripts/SourcePhysics/PlayerAiming.cs`
- `Assets/Scripts/PlayerVFXController.cs`
- Debug/test scene helpers and docs

Mission:
- Make camera, animation, VFX, crouch visuals, and future IK strictly read-only from sim output.
- Build replay and latency validation tooling.

Must not:
- Write authoritative gameplay state.

Definition of done:
- Presentation systems do not feed gameplay state back into the sim.
- There is one render-space position writer.
- Validation surfaces exist for:
  - deterministic replay
  - correction visibility
  - latency/loss playtests

---

## Merge Safety Rules

1. Only one agent may edit `SurfCharacter.cs` at a time.
2. Agent A owns network authority decisions.
3. Agent B owns history/replay semantics.
4. Agent C owns authoritative simulation purity.
5. Agent D owns environment/combat query contracts.
6. Agent E owns visuals, instrumentation, and validation only.
7. Every agent must preserve feature-flagged fallback paths until its replacement is verified.

---

## Execution Order

### Phase 0 - Instrument First
Owner:
- Agent E

Goal:
- Make regressions measurable before architecture churn lands.

Deliverables:
- Correction/snap counters
- State hash/checksum logging
- Host + client test checklist
- 10-minute endurance checklist
- Feature-flag matrix

Exit criteria:
- We can compare before/after behavior with logs instead of feel alone.

### Phase 1 - Freeze Authority Surface
Owner:
- Agent A

Goal:
- Stop the network contract from moving under the other agents.

Deliverables:
- Clear runtime path selection
- One authoritative look path
- One proxy correction path
- Commented feature flags describing what is still legacy

Exit criteria:
- Other agents can build against a stable authority model.

### Phase 2 - Extract Pure Sim Boundary
Owner:
- Agent C

Can run with:
- Agent E

Goal:
- Remove Unity presentation writes from rollbacked simulation.

Deliverables:
- `MoveData` split or reduced so rollback state is pure
- No child/camera transform mutation inside simulation tick
- Runtime handles moved out of authoritative state

Exit criteria:
- A recorded input sequence produces stable state over repeated local replays.

### Phase 3 - Fix Replay Semantics
Owner:
- Agent B

Can run with:
- Agent C after Phase 1 is stable

Goal:
- Make correction/replay logic line up with the new pure state boundary.

Deliverables:
- Cleaner frame ownership
- Replay-safe authoritative reconcile
- Deterministic input history semantics

Exit criteria:
- Replay of authoritative correction no longer depends on live scene state.

### Phase 4 - Replace Scene-Driven Queries
Owner:
- Agent D

Depends on:
- Agent C establishing sim/query boundaries

Goal:
- Stop combat and environment interactions from smuggling nondeterminism into rollback.

Deliverables:
- Stable query interfaces/results
- Event-driven hit results
- Rollback-safe hit dedupe

Exit criteria:
- Grounding/contact/hit logic no longer depends on hidden runtime caches or scene-object identity.

### Phase 5 - Presentation Cleanup
Owner:
- Agent E

Depends on:
- Agent C starting to remove sim-side transform writes

Goal:
- Keep visuals expressive without letting them destabilize networking.

Deliverables:
- Visual-only smoothing
- Visual-only crouch/camera offsets
- Animation/VFX triggered from confirmed or rollback-safe events
- Prefab/animator audit for hidden feedback paths

Exit criteria:
- Animation, camera, and future foot IK are derived only from sim output.

### Phase 6 - Final Integration And Deletion Pass
Owner:
- Agent A

Goal:
- Remove the dead path and finish the migration.

Deliverables:
- Legacy path disabled by default or deleted
- Consolidated authority comments/docs
- Final branch merge checklist

Exit criteria:
- One architecture remains.

---

## PR Slices

Keep each PR narrow. Recommended order:

1. PR-00 Metrics Baseline
   - Owner: Agent E
   - Scope: counters, logs, test checklist, no architecture changes

2. PR-01 Authority Path Freeze
   - Owner: Agent A
   - Scope: `NetworkedCharacter` only

3. PR-02 Pure State Extraction Part 1
   - Owner: Agent C
   - Scope: `MoveData` cleanup and simulation/runtime boundary scaffolding

4. PR-03 Replay Buffer Rewrite
   - Owner: Agent B
   - Scope: `RollbackManager` and `InputFrame`

5. PR-04 Pure State Extraction Part 2
   - Owner: Agent C
   - Scope: remove sim-time transform/camera writes

6. PR-05 Query Adapter And Combat Events
   - Owner: Agent D
   - Scope: hit/environment query path

7. PR-06 Presentation Isolation
   - Owner: Agent E
   - Scope: renderer/aim/VFX smoothing and event wiring

8. PR-07 Final Authority Cleanup
   - Owner: Agent A
   - Scope: remove obsolete path and simplify feature flags

---

## Acceptance Criteria Per PR

Each PR must include:

- A short note listing touched files
- A note on which feature flags are affected
- Verification notes for:
  - Host + 1 client
  - 80-120ms RTT simulation
  - 1-3% packet loss simulation if relevant
- Statement of whether deterministic replay logging changed

No PR should merge if it increases visible snap frequency without reducing it elsewhere.

---

## Required Test Matrix

Run after each phase that changes simulation or authority:

- Host + 1 client
- Host + 2 clients
- Late join during movement
- Late join during melee/combat
- 80-120ms RTT simulation
- 1-3% packet loss simulation
- 10-minute repeated dash/slide/melee endurance

Track:

- corrections per minute
- rollback count
- max correction distance
- visible snap count
- checksum mismatch count
- dropped or duplicated input count

---

## Immediate High-Risk Findings To Fix Early

These are the first leaks to remove:

- `NetworkedCharacter` currently mixes multiple authority paths in one class.
- `NetworkedCharacter` and `SurfCharacter.Update()` both write pose/interpolation state.
- `SurfController.Crouch()` mutates child transforms and camera local position during simulation.
- `SurfCharacter.SimulationTick()` still reads trigger/camera/runtime object state.
- `ProcessHitboxes()` still performs side effects during simulation/replay.
- `MoveData` still carries runtime references that should not be rollback-critical.

---

## Suggested Agent Prompts

### Agent A Prompt
"Own `Assets/Scripts/SourcePhysics/Movement/NetworkedCharacter.cs`. Consolidate runtime authority to one actual path, keep the losing path behind legacy flags, and remove duplicate movement/look writers. Do not change gameplay rules."

### Agent B Prompt
"Own `Assets/Scripts/SourcePhysics/Movement/RollbackManager.cs` and `Assets/Scripts/SourcePhysics/Movement/InputFrame.cs`. Make replay semantics deterministic, separate predicted and confirmed history, and add checksum/debug counters. Do not touch render code."

### Agent C Prompt
"Own `Assets/Scripts/SourcePhysics/Movement/MoveData.cs`, `SurfCharacter.cs`, and `SurfController.cs`. Extract a pure rollback-safe gameplay core by removing transform/camera/child-object mutation from simulation. Do not edit network transport."

### Agent D Prompt
"Own combat/environment query extraction in `SurfController.cs`, `Hitbox.cs`, and `Hurtbox.cs`. Replace scene-driven hit/contact logic with deterministic query results and event-driven combat outcomes."

### Agent E Prompt
"Own `CharacterRenderer.cs`, `PlayerAiming.cs`, `PlayerVFXController.cs`, and validation tooling. Make all visuals read-only from sim output and add replay/hash/latency verification surfaces."

---

## Notes

- I did not find a dedicated foot IK script under `Assets/Scripts`.
- That does not rule out prefab-side or animator-side planting feedback.
- Agent E should explicitly audit prefabs, animator settings, and any rig/camera hierarchy that may still be feeding movement state indirectly.

