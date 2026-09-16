# Rollback and simulation audit

Reviewed 2026-09-14, baseline commit `4c8ef42`. Scope: character input, movement, collision queries, FishNet prediction/reconciliation, custom rollback, combat, pickups, respawn, presentation boundaries, and replay tests. Static source audit; no Unity tests or live network sessions were run for this report. Findings distinguish code defects from determinism risks requiring runtime measurement. No gameplay implementation was changed.

Implementation follow-up: the first reliable-character-rollback changes and subsequent test results are tracked in [RELIABLE_CHARACTER_ROLLBACK.md](RELIABLE_CHARACTER_ROLLBACK.md). Findings below describe the original audited baseline, not the current implementation status.

The requested direction is responsive P2P with no anti-cheat requirement. The immediate priority is correct replay under the existing transport. P2P alone does not guarantee responsiveness: local prediction, remote prediction, simulation cost, and correction presentation determine the experience.

**Overall assessment**

The motor already has useful replay foundations: explicit tick inputs, a supplied timestep, cloned movement snapshots, restoration of crouch geometry, and a controller whose gameplay state largely lives in MoveData. It is not accurate to say movement is entirely unreplayable. The missing contract is that restoring a snapshot must restore everything its next tick reads, and that each replay tick must execute exactly once from the correct starting state.

There are three separate milestones: same-process replay against a controlled world; complete replay of interactions among characters and pickups; and identical simulation across supported peer builds/machines. Passing the first does not establish the other two.

**1. P1 — FishNet and the custom manager both own replay**

Evidence: `Assets/Scripts/Multiplayer/Player/NetworkedCharacterPredictionReconcileService.cs:211-219`, `RollbackManager.cs:45-71`, `NetworkedCharacter.cs:284-314`; vendored FishNet `Assets/FishNet/Runtime/Managing/Prediction/PredictionManager.cs:665-730` and `Assets/FishNet/Runtime/Object/NetworkBehaviour/NetworkBehaviour.Prediction.cs:732-764`.

ApplyReconcile calls ApplyAuthoritativeCorrection, which restores the authoritative movement snapshot and immediately replays to the custom manager's current tick. FishNet then performs its own replay loop, invoking the replicate user logic with Replayed flags. RunReplicate does not skip those calls or restore a starting state for that second traversal; it invokes SimulatePredictedTick again. When a correction is ignored, the custom code does not restore the older state at all, while FishNet still has a replay cycle to perform. The application correction decision does not cancel FishNet's internal reconcile lifecycle.

Example: restore state after tick 100, replay 101–106 internally, then execute FishNet's replay ticks starting from the already-advanced state. Position, timers, stamina and edge-consumption history can all advance incorrectly. Exact tick ranges depend on FishNet's local/server tick mapping and packet history; the two owners are visible in source, but actual session incidence has not been measured.

Fix: establish one owner of rewind and replay. While retaining FishNet prediction, let its reconcile callback restore the appropriate authoritative state and let FishNet perform the replay. Keep the custom step/history utilities for offline testing, without a nested replay inside the FishNet callback. Apply visual smoothing separately. A future P2P driver can take over the same simulation interface.

Acceptance: instrument `(simulation pass, entity, input tick, starting state tick)` in a real owner/client connection. With delayed corrections and both large and small state differences, every replay tick must start from its predecessor, execute once per pass, and finish at the correct present tick.

**2. P1 — Aborted replay reports a completed timeline**

Evidence: `Assets/Scripts/Multiplayer/Player/RollbackReplayEngine.cs:18-39` and `RollbackManager.cs:63-71`.

ReplayRange logs and returns when an input is missing. Its void return loses the failure and last completed tick. ApplyAuthoritativeCorrection unconditionally assigns `_currentTick = replayToTick`. The character may be at an older frame while CurrentTick claims it is current; history is partly overwritten.

Fix: preflight the entire replay range, including tick/entity identity and history availability; return an explicit result with last completed tick. On failure, retain a consistent valid state and request/reset to a usable authoritative baseline. Define the supported rollback window and reject unavailable ranges explicitly. Do not silently synthesize success.

Acceptance: omit the first, middle and last input; supply a wrong-tick or wrong-entity input; exceed history capacity. No case may leave state.frame and CurrentTick inconsistent or commit partial history as a successful pass.

**3. P1 — Upgrade configuration is missing from character history and reconciliation**

Evidence: `Assets/Scripts/Gameplay/Pickups/MaxStaminaUpgradeEffectDefinition.cs:30`, `StaminaRegenUpgradeEffectDefinition.cs:36`; `Assets/Scripts/Gameplay/Player/Movement/SurfController.cs:26-29`; `MoveData.cs`, `SurfCharacter.cs:532-541`, and the reconcile payload.

Pickups mutate movementConfig.maxStamina and movementConfig.staminaRegenRate. Those are read on subsequent movement ticks but are not in MoveData, restored by LoadState, or transmitted in the movement reconcile payload. With server-authoritative pickups, no corresponding upgrade replication was found in the reviewed scripts.

Example: a capacity upgrade at tick 50 changes the cap from 3 to 5. Restore tick 40: the old movement snapshot now regenerates toward 5, using a future upgrade. On an owner client whose cap remains 3, corrected stamina above 3 is subsequently moved back toward its local cap. This is both a historical replay defect and a server/client consistency defect.

Fix: freeze base MovementConfig for the session. Store per-character upgrade/modifier state in the rollback snapshot and derive effective movement parameters from base config plus that state. Capture refill effects at a simulation tick as well. Replicate these modifiers while FishNet remains authoritative.

Acceptance: snapshot before an upgrade, cross the pickup tick, restore, and replay. Compare cap, regen, stamina and later dash eligibility on every tick, including on a separate client.

**4. P1 for full rollback — Melee simulation intentionally changes rules during replay**

Evidence: `Assets/Scripts/Gameplay/Player/Movement/SurfCharacter.cs:423-478`; `Assets/Scripts/Multiplayer/Player/NetworkedCharacter.cs:503-546`; `Assets/Tests/ReplayHarness/AuthoritativeStateRegressionTests.cs:228-267`.

Fresh authoritative simulation performs a hit query, records target/tick, stops horizontal attacker velocity, and invokes damage/parry logic. Custom replay and client prediction skip the entire query. A pre-hit state cannot reproduce the authoritative hit from inputs alone. Reconciled hit fields can restore a hit already included in a snapshot, but there is no tick-indexed event history to reproduce a later hit when replay starts before it.

Health is a separate SyncVar, respawn status is a component flag/coroutine, and parry callbacks mutate another character. A one-character MoveData rollback cannot restore those interactions. This is a deliberate current limitation, not a reason to re-enable queries against live targets during replay.

Fix in stages: for the current server model, supply tick-indexed authoritative gameplay events and sufficient historical state for movement replay. For full P2P rollback, restore all interacting characters and world objects, evaluate hits/parries from the same historical tick, and rerun gameplay outcomes. Suppress or deduplicate audiovisual/external effects, not the gameplay calculations themselves. Define whether same-tick hits trade or resolve in a chosen order.

Acceptance: rewind across hit, miss, parry, reflected damage and death. Full world state must match a clean forward run; no damage or presentation event may be applied twice to the final timeline.

**5. P1 for full rollback — Collision targets belong to the live/rendered world**

Evidence: `Assets/Scripts/Gameplay/Player/Movement/SurfCharacter.cs:691-709`, `SurfPhysics.cs:18-27`, `Assets/Scripts/Gameplay/Combat/Hitbox.cs:51-69,145-166`, and `ProjectSettings/DynamicsManager.asset:22`.

The movement motor passes an explicit historical origin to its own sweeps, which is good. However, the targets queried are live Unity colliders. Penetration resolution explicitly reads other.transform.position/rotation. Melee overlaps and ClosestPoint read the current target collision world. Character Update interpolates the root transform and also adjusts playerHitbox locally; there is no global tick phase that first stages every character's historical collision pose before interaction queries.

AutoSyncTransforms is disabled. FishNet does synchronize transforms at parts of its lifecycle, but that does not restore historical targets and the custom nested replay runs inside reconciliation before FishNet's subsequent synchronization. A blanket claim that physics is never synchronized would be incorrect. The defect is the absence of a complete historical query-world contract.

A stationary level can be a valid immutable replay dependency. Moving blockers, animated hit targets and spawn/despawn changes cannot be assumed stationary. This report does not claim a measured static-map PhysX divergence.

Fix: separate simulation collision poses from visual transforms. Use an explicit query-world interface and either immutable static collision data plus snapshot-driven dynamic shapes, or a dedicated physics scene whose collision poses are staged and synchronized per tick. Tick all movement, stage the relevant collision poses, then resolve interactions in a defined phase/order. Never interpolate the collision world used for gameplay.

Acceptance: replay the same inputs while deliberately varying render rate and render poses; results must remain unchanged. Add a moving blocker, crouching opponent, and different character update orders. The policy for simultaneous interactions must be explicit.

**6. P2 — Missing-input detection and prediction policy are inconsistent**

Evidence: `Assets/Scripts/Multiplayer/Player/NetworkedCharacterPredictionReconcileService.cs:45-80`, `NetworkedCharacterPredictionData.cs:5-16`, and `Assets/Scripts/Gameplay/Player/Input/InputFrame.cs:36`.

RunReplicate overwrites input.frame from a positive transport tick before checking IsValid. An explicitly invalid InputFrame thereby becomes valid. A default struct also has frame 0, which IsValid accepts. Consequently a missing/default payload can bypass the documented last-input recovery and simulate zero controls/look. ReplicateState already distinguishes created input from prediction, but the policy is not applied as the primary validity decision.

The fallback also copies justPressed with held controls. If that edge is re-stamped onto another tick, the lastConsumedPressFrame fields cannot recognize it as the original press. This is a latent repeat-press risk in recovery paths, distinct from the first bug that bypasses recovery.

Fix: determine input availability before stamping tick identity. Explicitly define predicted controls (e.g. retain held buttons/sticks/look, clear one-shot edges), canonical input replacement, and conflicting canonical packet handling. Validate requested tick and entity when replay consumes history.

Acceptance: created, missing, predicted, duplicate, reordered and recovered inputs, including packet loss immediately after jump/dash/parry. Verify held movement continues according to policy and no new press is invented.

**7. P1 for full rollback — Pickups and respawn are outside simulation time/history**

Evidence: `Assets/Scripts/Gameplay/Pickups/PickupWorldItem.cs:83-132,149-158`, `PickupSpawnDirector.cs:59-120`, `PickupSpawnLocation.cs:34`, `Assets/Scripts/Multiplayer/Player/NetworkedCharacter.cs:560-615`, and `Spawning/PlayerSpawnService.cs:276`.

Pickups are consumed through OnTriggerEnter and despawn/destroy immediately; their consumed state is not in rollback history. Floating/spinning moves the same root that owns the trigger collider using Time.time/deltaTime, so apparent decoration changes collection geometry. Spawning uses wall-clock timers and Unity random selection. Respawn uses WaitForSeconds and an unticked pose RPC; the local spawn application resets the spectator buffer but does not reset the custom predicted/input history or establish a new history epoch.

Rewinding cannot reconstruct a destroyed pickup, its prior availability, or its effect tick. A correction from before respawn can cross the teleport boundary without replay knowing a respawn occurred. This is an application-level history boundary problem; packet ordering details need a live integration test.

Fix: tick-stamp collection, despawn and respawn; snapshot availability, scheduled ticks, stable spawn IDs and a dedicated deterministic RNG state. Apply bob/spin to a visual child only unless moving pickup collision is a deliberate mechanic. For a transitional character-only rollback, use explicit lifecycle epochs to reject stale corrections and reset both application/FishNet histories through their supported lifecycle. Full-session rollback should represent lifecycle changes in world state instead.

Acceptance: rollback across contested pickup, refill, upgrade, death and respawn; deliver an old correction after respawn. Verify one consumption, reproducible selection, no resurrection of old player movement, and reproducible object availability.

**8. P2 — Hit selection has no stable tie policy; cross-machine collision determinism is unproven**

Evidence: `Assets/Scripts/Gameplay/Combat/Hitbox.cs:57-69`, `Assets/Scripts/Shared/Physics/Tracer.cs:36-51`, `SurfPhysics.cs:28-43`.

Hitbox returns the first eligible overlap, so the selected target is coupled to physics enumeration rather than a defined game rule. Single-hit sweeps likewise do not provide an application tie rule for equal-distance contacts. Depenetration is better: it chooses deepest penetration and breaks exact ties by normal, and its overflow path avoids silently truncating overlap candidates. Preserve those improvements.

Fix: define target priority, deduplicate hurtboxes and use session-stable IDs for ties. Record collision identity/distance/normal in determinism diagnostics. Verify static geometry ordering and asset/build configuration. An isolated physics scene and sorted candidates improve control but do not prove cross-machine numeric determinism.

NVIDIA documents PhysX determinism limitations across platforms/compiler configurations. That is a reason to test the actual supported builds and potentially use deterministic collision/math, not proof that every static sweep here currently diverges. See [NVIDIA PhysX determinism guidance](https://nvidia-omniverse.github.io/PhysX/physx/5.1.3/docs/BestPractices.html#determinism). Unity documents explicit transform flushing in [Physics.SyncTransforms](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Physics.SyncTransforms.html).

Acceptance: create equivalent worlds in different object creation orders; compare multiple-target attacks and corner/edge contacts. Run recorded sessions on supported CPUs, builds and platforms, compare exact state bytes, and stop at the first mismatch.

**9. P2 — Current diagnostics/tests do not certify the production rollback path**

Evidence: `Assets/Tests/ReplayHarness/ReplayHarnessRunner.cs:27-73`, `ReplaySample.cs:121-187`, `ReplayHarnessPlayModeTests.cs:15-144`, `Assets/Scripts/Multiplayer/Player/StateDivergenceEvaluator.cs`, and `CorrectionGateConfig.cs`.

The harness has valuable same-trace and delayed-correction tests, including a real prefab bootstrap and pre-correction checksum checks. However, it calls the custom rollback manager directly, bypassing FishNet's replay scheduling and wire state path. Most traces exercise one character against a fixed environment. Fingerprints round floats to thousandths and omit external configuration/world state. Equality of these fingerprints is not full-state or bitwise equality. Final convergence can also benefit from delivering authoritative snapshots for all frames; retain the pre-correction checks and add comparisons immediately after each replay.

The production divergence gate intentionally ignores small position/timer differences. That can be a correction policy, but must not serve as the determinism oracle: a small timer difference can change which frame an action becomes available. MoveData.Clone correctly copies its scalar/vector fields; its Transform references remain live dependencies. Replacing MemberwiseClone alone would not fix the identified gaps.

Fix: a canonical versioned state serializer with field-level comparison, exact floating-point diagnostics and stable IDs. Classify each state field as persisted, immutable, derived per tick, presentation-only, or retired. Add a coverage test so new gameplay state cannot silently escape capture/restore/hash/reconcile. Do not indiscriminately serialize wish flags that are recomputed from each input or retired ladder fields.

Acceptance: the tests below cover both standalone simulation and actual FishNet-connected objects. Performance measurements should include worst supported replay depth; correctness must be established before widening tolerance thresholds.

**Implementation order and exit criteria**

| Stage | Deliverable | Required exit evidence |
|---|---|---|
| 1. Establish timeline correctness | One replay owner; explicit input availability/edge policy; transactional replay failure handling; trace local/server/input/state ticks and verify their mapping | Live FishNet correction advances each tick once; malformed/missing history cannot corrupt the timeline |
| 2. Close character state | Immutable base config, snapshot modifiers, unified collider-shape derivation, versioned state manifest/serialization, lifecycle boundary handling | Save/restore into a fresh character and replay from many cut points matches each forward tick, including upgrades and respawn |
| 3. Isolate collision world | Separate render and simulation transforms; explicit static/dynamic query dependencies; stable collision IDs and tie policies | Render cadence/pose and object creation order do not change replay outcomes in the supported test environment |
| 4. Replay interactions | World snapshot for all characters, health/parry, pickups, spawn schedule and RNG; explicit movement/query/outcome phases; event ledger for presentation | Rewind across interacting players and world lifecycle produces the same world and a single final set of presentation events |
| 5. Qualify P2P | Transport-independent session stepper, remote-input prediction/confirmation, bounded rollback, full-state mismatch diagnostics; numeric/collision determinism choice based on evidence | Multiple processes/machines match state on confirmed frames under latency, jitter, loss and reordered packets within the supported window |

The reusable simulation contract should be equivalent to `Step(worldState, inputsForTick, immutableRules, collisionWorldAtTick, tickDuration) -> (nextWorldState, events)`. A FishNet adapter and a later P2P adapter can both drive it. Configuration and static geometry need matching session/build identifiers; transient Unity object references should not define serialized gameplay identity.

At minimum, test: save/load without stepping; save/load into another instance; multiple rollback cut points; repeated rollback of the same interval; controller reconstruction; crouch under ceilings; slide/jump/dash transitions; thin walls and corner contacts; upgrade/refill boundaries; actual hit/parry/death/respawn; competing pickup claims; history overflow and input gaps; different render rates; and two-process FishNet/P2P input timelines. Exact comparison should be available alongside readable tolerances so numeric drift is visible before it becomes a branch difference.

Preserve current movement feel while performing stages 1–3. Do not start by replacing all movement math, switching transports, enabling live melee queries during replay, or adding larger correction tolerances. Physics replacement, if needed for cross-machine equality, should be evaluated against recorded movement traces.

**Design decisions needed before interaction implementation**

No designer clarification blocks this audit or stages 1–2. Before finalizing interaction rules, confirm same-tick hit trading versus priority, multi-target selection, body blocking/moving gameplay geometry, and upgrade persistence through death. Player count and supported platforms are still needed to qualify the P2P performance/determinism target. These are design choices; this audit does not silently change them.

A central service remains compatible with P2P for matchmaking, rendezvous/relay and session diagnostics. Anti-cheat is out of scope per the requested direction. GGPO's contract remains save/load and deterministic frame advancement of the complete relevant state; see [GGPO](https://www.ggpo.net/).
