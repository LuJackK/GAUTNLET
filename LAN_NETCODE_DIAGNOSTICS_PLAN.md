# Two-machine LAN netcode diagnostics: development plan

Date: 2026-09-16. Status: proposed tool; implementation has not begun under this plan.

## Purpose and scope

Capture a real session on two PCs and reconstruct what each simulation knew, used, decided, and displayed. Identify the first meaningful disagreement and distinguish expected prediction, input/tick errors, incomplete restoration, world-state differences, and presentation artifacts.

The first version observes the existing FishNet path. It does not require a new networking implementation and must not change gameplay decisions. Later offline replay provides evidence for the [four networking options](C:/Users/LukaJack/Desktop/GAUTNLET/NETWORK_IMPLEMENTATION_OPTIONS.md).

Two PCs on a LAN do not automatically simulate equivalent worlds or have synchronized clocks. Current owner, server, and proxy paths have intentionally different responsibilities. The tool must represent those differences instead of labelling every unequal position a determinism failure.

## Questions every useful capture should answer

1. Which input was sampled, transmitted, received, substituted, and actually consumed for this tick?
2. Which state, movement epoch, simulation role, and collision world were used?
3. Why did reconciliation run, get ignored, or fail to find history?
4. What changed after replay, and what did the player actually see?
5. Does the same complete state and input trace reproduce the same outcome locally and on the other PC?
6. Are differences tied to hosting role, hardware, frame pacing, network impairment, or incomplete capture?

## Existing components to reuse

| Component | Observed capability | Planned use |
|---|---|---|
| [NetcodeDebugEngine](C:/Users/LukaJack/Desktop/GAUTNLET/Assets/Scripts/Multiplayer/Player/Debug/NetcodeDebugEngine.cs) | Event categories, role/tick context, suspect flags, scenarios, bounded event list | Add structured sink and session markers; retain existing scenario vocabulary |
| [NetcodeDiagnosticsHUD](C:/Users/LukaJack/Desktop/GAUTNLET/Assets/Scripts/Multiplayer/Player/Debug/NetcodeDiagnosticsHUD.cs) | Correction counters, rollback/tick data, movement display | Add capture readiness, peer state, marker and export status |
| [Prediction reconcile service](C:/Users/LukaJack/Desktop/GAUTNLET/Assets/Scripts/Multiplayer/Player/NetworkedCharacterPredictionReconcileService.cs) | Input resolution, replicate/reconcile and spectator paths | Capture before/after input resolution, authoritative snapshot construction, correction decisions and presentation application |
| [RollbackReplayEngine](C:/Users/LukaJack/Desktop/GAUTNLET/Assets/Scripts/Multiplayer/Player/RollbackReplayEngine.cs) | Corrected state loading and per-input replay | Emit restore baseline, replay span, per-tick state and completion/failure |
| [ReplayTraceRecorder](C:/Users/LukaJack/Desktop/GAUTNLET/Assets/Tests/ReplayHarness/ReplayTraceRecorder.cs) | Local InputGathered capture into a trace asset with initial pose | Reuse input conversion; add standalone runtime capture beyond asset-based recording |
| [Replay harness plan](C:/Users/LukaJack/Desktop/GAUTNLET/DETERMINISTIC_REPLAY_HARNESS_PLAN.md) | Existing local replay and delayed-correction approach | Import session bundles through an adapter; expand state coverage explicitly |
| [WorldRollbackHistory](C:/Users/LukaJack/Desktop/GAUTNLET/Assets/Scripts/Multiplayer/World/WorldRollbackHistory.cs) | Shared snapshot/history foundation | Future full-world replay adapter after live integration |

Audit these seams before implementation. Existing logs and historical test claims are not a complete session recorder. Do not change vendor FishNet code when a project-owned seam provides the needed information.

## User workflow

1. Build once and copy the same development build and gameplay content to PCs A and B.
2. A creates a diagnostic session; B joins using LAN address and a short session code. Both show matching build/config status, assigned roles, and capture readiness.
3. Arm recording on both machines. Begin after both acknowledge a future mapped session tick. If either cannot arm, show a failed start rather than silently recording only one side.
4. Run a named scenario. Either tester can mark a suspicious moment with a configurable unused hotkey or button and optional note. Record both the marker's original tick and when the other process receives it.
5. Stop and export. Each PC saves its own bundle; optionally copy B's completed bundle to A over a separate diagnostic connection. Manual folder transfer remains supported.
6. Open a merged local report showing coverage, timeline, first discrepancy, and both perspectives. Repeat with host roles swapped.

No cloud account is needed. Diagnostics pairing controls capture only; it must not confer gameplay authority. Permit only bounded capture commands and bundle transfer, with explicit session pairing rather than arbitrary remote execution.

## Capture architecture

Use a development-only recorder on each process, a small coordination channel, and an offline merger/report generator. Keep bulk logging local during gameplay so diagnostic traffic does not materially change the network under test.

Proposed components: `SessionCaptureCoordinator`, `NetcodeTraceRecorder`, `TraceBundleWriter`, `TraceMergeAnalyzer`, and `ReplayImportAdapter`. Names are proposed, not existing APIs. Place runtime tooling beside the current multiplayer debug code, test adapters under the replay harness, and the offline report tool outside gameplay assemblies. Follow applicable directory instructions during implementation.

Copy value data on the simulation thread into bounded buffers. Serialize and compress detached records on a worker; never access Unity objects from that worker. Avoid per-tick console output and synchronous file I/O. Record queue depth, dropped records, writer errors, allocation cost, and capture overhead. Overflow marks a coverage gap; it never blocks a gameplay tick.

Use a configurable rolling buffer, initially targeting 30 seconds before and 10 seconds after a marker, subject to a hard memory limit. Pin a restorable checkpoint preceding the retained window plus all subsequent required inputs. If these cannot be retained, label the window non-replayable. Support continuous bounded chunk recording for longer sessions.

## Trace contract

### Session manifest

Record schema version, session/run IDs, stable process IDs, A/B role assignment, topology, build artifact hash, source revision and dirty-state indicator, Unity/runtime/backend versions, OS/CPU architecture, scene/content hashes, effective movement/combat configuration, tick rate, physics settings, transport and prediction configuration, capture mode, and impairment seed/settings.

Record a stable roster and entity mapping, including spawn generations/movement epochs. Unity instance IDs and local object allocation order are not portable identity. Dynamic entities require an agreed ID or a logged deterministic mapping. Document excluded world behaviors in the manifest.

### Every event

Include session/process ID, monotonic timestamp and frequency, local sequence, render frame, original local/FishNet/input tick values, mapped session tick when established, entity generation, execution role, event type, simulation phase, and replay pass ID. Keep original identifiers when mapping so tick errors remain diagnosable.

A host process must label server simulation and local presentation separately even when they share an object or call path. Record whether a state is predicted, authoritative, interpolated, or replayed. Replay revisions must not overwrite the original prediction in the trace.

### Required data groups

| Group | Records to collect |
|---|---|
| Inputs | Sampled buttons/edges/axes/look; canonical serialized payload; owner/entity; target tick; application send/receive IDs; actual consumed input; prediction/default reason; rejected stale or wrong-epoch inputs |
| Simulation | Explicit pre/post-tick gameplay values; position, velocity, stance, grounded state, timers, dash/jump/melee/stamina state; prior input seed; readiness/spawn gates; timestep and execution order |
| Reconciliation | Snapshot source tick and arrival; predicted history lookup; field differences; correction decision/reason; restored baseline; replay range/count/cost; resulting state; history exhaustion or failure |
| Combat/world | Attack identity and phase; available hit candidates/selected target; query tick and collider state source; hit/parry/trade decisions; health/momentum effects; pickups/RNG/respawn events where available |
| Network | RTT samples, arrival intervals, input lateness, reorder/duplicate observations, disconnects, available transport counters; distinguish observed transport loss from application sequence gaps |
| Presentation | Simulation root versus rendered pose, spectator source/interpolation ticks, camera correction, smoothing duration, displayed attack/hit event IDs and cancellations |
| Performance | Frame/tick/replay durations, stalls, GC and recorder overhead; clock mapping uncertainty and trace gaps |

Do not fill unavailable client combat data with invented results. Record `not executed` or `not captured` with the role reason. Do not call a render discrepancy a gameplay disagreement.

### State and collision fidelity

Use versioned explicit serialization, stable field/entity order, and raw float bits for exact fingerprints. Also export readable field values and absolute/relative differences. Reject or explicitly report non-finite values. Do not hash arbitrary object memory, process-local references, or unstable dictionary order.

Reuse the project's movement serializer after verifying its coverage. Maintain a manifest test for newly added gameplay fields. Tolerant/quantized comparisons describe magnitude; they cannot prove deterministic equality. A matching hash is an efficient signal over captured fields, not proof that all necessary state was captured.

In optional detailed collision capture, record the existing query's type, origin, shape, direction, distance, layer/trigger policy, candidate IDs/order, selected result, contact normal/distance, and relevant collider poses. Observe the actual query results; do not issue extra queries in the live path just for logging. Include static geometry identity and dynamic collision state in replay prerequisites.

## Tick and clock alignment

Pair records by logical tick, entity generation, role, phase, input history, and replay revision. Do not compare whatever two machines happened to display at the same wall-clock time.

Establish and log mappings among input frames, FishNet ticks, and session ticks through shared input/snapshot identities. Preserve reconnect/reset boundaries and handle tick wrap explicitly. A finalized comparison requires matching known inputs and equivalent starting state/world for the compared scope. Current proxy snapshots are not full-world replay evidence.

Exchange periodic ping-style clock samples to estimate offset/drift and uncertainty for visualization. Use local monotonic clocks for durations. RTT is measurable locally; one-way delay derived from clock mapping is only an estimate, and RTT/2 is not an exact measurement on asymmetric paths. UTC labels help humans locate files but do not align simulation truth.

## Analysis and replay lanes

### A. Live session comparison

Compare authoritative snapshots to the corresponding owner prediction/history, then to post-correction results. Show missing input and packet arrival context. Expected owner/proxy or server/client combat differences are labelled by role. This lane diagnoses current networking; it cannot by itself establish cross-machine determinism.

### B. Same-machine restore/replay

Replay captured input from a complete checkpoint in an isolated test environment. Compare the original stepping with save/restore/replay. A failure points toward missing state, ordering, side effects, or non-repeatable simulation. Never rewind the live match to run this test.

### C. Same trace on both machines

Run the same checkpoint, full input history, configuration, world state and simulation role on both PCs offline. Locate the first differing tick/field, then inspect collisions and lifecycle events. First test each machine's repeatability separately. Different live roles do not meet this comparison's prerequisites.

Movement-only evidence must remain labelled movement-only. Full combat/world replay is blocked until all relevant state and live world integration are covered; exporting movement plus inputs cannot reconstruct arbitrary live opponent interactions.

### D. Delayed-input and architecture experiments

Once the relevant replay lanes work, apply recorded, seeded impairment schedules. Compare prediction accuracy, correction magnitude, displayed reversals and replay cost. Later add symmetric world rollback or rotating-authority adapters; record authority epochs and handoff decisions. Instrumentation is reusable, but adapters are not part of the first release.

## Report design

Produce an offline HTML report plus machine-readable JSON findings. The first screen shows run compatibility, trace completeness, topology and scenario, followed by:

- First unexplained discrepancy with tick, entity, phase, field and both values.
- Side-by-side input knowledge, authoritative/predicted/replayed states, and rendered presentation.
- Rollback depth and CPU cost; correction distance/angle and duration; median, p95 and maximum by role.
- Attack/hit/parry outcome changes, marker notes, missed history and missing-record counts.
- Evidence links into local raw chunks and a minimal replay bundle when sufficient state exists.

Classify findings as expected prediction, input/tick mismatch, restore/replay mismatch, cross-machine mismatch under verified prerequisites, intentional role difference, presentation-only mismatch, or insufficient evidence. Flags identify hypotheses, not automatically proven root causes. Use millimetres/degrees/milliseconds where applicable and document game-unit conversion.

## Test matrix

| Scenario | Main purpose |
|---|---|
| Spawn, idle, reconnect, respawn | Readiness, entity generation and clock/tick reset correctness |
| Flat movement, air control, slopes, edges, dash, double jump | Surf feel, contact branches and movement history |
| Hit/miss boundaries, dodge, parry/reflection, simultaneous lethal attacks | Combat decisions, trade rule, momentum and presentation |
| Contested pickups and scheduled respawns | Shared state and RNG coverage; gated on capture/replay support |
| A hosts/B joins, then B hosts/A joins | Separate role effects from hardware effects |
| Different frame caps and a controlled CPU stall | Render/simulation coupling and timing pressure |
| Same-build offline trace replay on A and B | Cross-machine repeatability under common inputs |

Start with clean wired LAN. Then use explicitly configured per-direction delay presets, for example +25/+25 ms and +10/+60 ms, followed by seeded jitter, loss, reordering, and a short burst. These are added one-way test delays, not promised RTT values. Record injection placement and avoid accidentally applying the same delay twice. Exercise the host local path separately: transport-only injection may not delay it.

With only two PCs, a server-only process can run beside one client for functional tests, then move to the other PC. Record shared CPU/load and local route advantage. A genuinely independent-server fairness test requires a third machine or external server and is outside the initial two-PC setup.

Repeat paired scenarios with the same inputs and impairment seeds. Human sessions reveal feel; scripted traces permit controlled comparison. Neither replaces the other.

## Implementation milestones and acceptance

1. **Schema and seam audit.** Document actual tick semantics, stable identities, state coverage, execution roles and available counters. Fixture tests reject incompatible manifests and distinguish missing records from equality.
2. **Local recorder.** Standalone development build writes a readable, versioned bundle with inputs, simulation and reconciliation. Exercise marker windows, truncated files, overflow and disk errors. Gameplay continues on capture failure, with the failure visible.
3. **Paired LAN capture.** Both processes agree on a session and capture start; exports merge by causal IDs. Host-role swapping works. Intentionally delayed, duplicated and missing events appear correctly. Disconnection leaves usable partial bundles.
4. **Comparison report.** Known synthetic input mistakes, state differences and presentation-only differences receive distinct classifications. The report finds the planted first differing tick and does not report expected proxy differences as determinism failures.
5. **Replay import.** Movement capture reproduces in the existing isolated harness; same-machine restore and cross-machine common-trace comparisons work. Unsupported world captures explicitly refuse a full-world determinism verdict.
6. **Detailed combat/world capture.** Extend coverage alongside enabled world simulation. Verify hit/miss/parry/trade and pickup/lifecycle traces before claiming complete replay. Add collision detail and presentation event deduplication diagnostics.
7. **Architecture evaluation.** Run the agreed scenario matrix and report evidence for the networking decision, including unresolved scope and performance limits. Rotating authority remains a separate implementation project.

Provisional performance gate: over a 10-minute baseline session, no unreported dropped records and no sustained queue growth; target recorder main-thread p95 overhead below 2% of the tick budget on the slower PC. Measure enabled versus disabled runs. Treat this as a target to validate, not an achieved measurement. A heavy detailed-collision mode must identify its additional cost.

## First deliverable

The smallest useful release is two paired development builds, a local recorder for input/simulation/reconciliation, synchronized markers, reliable bundle export, and a report aligning both machines by tick and role. It should explain one real correction end to end before adding full-world replay or a large live dashboard.

Completion of the diagnostic tool does not itself select a networking architecture. The output is evidence: where divergence begins, whether it is reproducible, how players experience corrections, and what replay costs on both machines.
