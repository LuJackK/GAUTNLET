# Reliable character rollback — first implementation

2026-09-15. Implements the foundational character corrections from ROLLBACK_AUDIT.md, preserving the existing movement rules and FishNet transport.

**Implemented behavior**

- FishNet alone owns connected replay. Reconcile restores the authoritative baseline; it never calls the standalone replay loop. Correction thresholds now classify divergence for diagnostics rather than deciding whether to skip the restore.
- Owners gather inputs on LocalTick; reconciliation uses FishNet's mapped GetTick instead of the server Frame field to index owner history.
- Default/missing inputs are distinguished with HasInput before stamping tick identity. Prediction retains held controls/look, clears one-shot edges, and starts from lastInput in the restored snapshot. Before any input exists, neutral prediction preserves the spawn look direction. Replay calls cannot resolve live melee hits.
- Standalone correction preflights the complete input range, timestep, entity identity and 256-state window before changing state/history. It returns false with LastReplayError on validation failure. This is validation-failure atomicity; unexpected exceptions from the simulation itself are not a supported recovery mechanism.
- Capacity and regeneration upgrades are snapshotted modifiers, included in reconciliation. Base MovementConfig no longer changes when collecting these upgrades. Movement, refill pickups and HUD read effective values. Upgrade persistence through death is preserved.
- Collider height, crouch settings, slide enablement and step settings are included in reconciliation. Capsule height restoration shares the motor's minimum-diameter rule. LoadState rebinds Transform references to the receiving character.
- A movement generation number distinguishes respawns. Spawn RPCs clear FishNet cached replicates and reset custom history/input. Old-generation reconciles and replay inputs cannot move the new life. A newer reconcile can supersede an older spawn RPC. New network sessions reset the generation.
- Removed the redundant custom network input ring. FishNet owns connected input history; the standalone harness supplies its own canonical trace.

**State contract for this milestone**

| Category | State/dependencies | Treatment |
|---|---|---|
| Persistent movement | Position, velocity, view angles, grounded/jump state, dash/stamina/slide/melee/parry timers and transitions, consumed press ticks | MoveData snapshots and reconcile payload |
| Prediction seed | Last simulated InputFrame | MoveData.lastInput and reconcile payload; missing-input extrapolation never reuses a future live input |
| Runtime upgrades | Capacity and regen modifiers | MoveData bonuses and reconcile payload |
| Character motor settings | Height, crouch height/speed, slide enablement, step enablement/height | MoveData and reconcile payload |
| Immutable session dependencies | Base MovementConfig, static map collision, collider width/radius/center/scale, collision masks | Must match and remain fixed; not historical world state |
| Derived each tick | Wish buttons, movement axes, slide request; ground object discovered by query | Recomputed from input/state/immutable collision world |
| Presentation | Unity object bindings, render interpolation and VFX state | Not peer-serializable simulation state; object bindings are rebound on load |
| Retired compatibility fields | Ladder/water state and unused movement fields | Not a supported active gameplay dependency; water/ladder branches remain disabled |
| External gameplay | Hit outcomes after the baseline, health, pickup collection, world lifecycle | Still authoritative external events; full historical interaction replay is a later milestone |

The snapshot remains a MoveData clone with a manually defined FishNet payload. This change does not introduce a universal binary world snapshot or claim that every compatibility/presentation field is network-serialized. Changing immutable rules mid-session still invalidates replay assumptions.

**Validation**

Unity 6000.3.2f1 PlayMode suite: `Fragsurf.ReplayHarness`. Results: `TestResults/reliable-rollback.xml`; log: `TestResults/reliable-rollback.log`.

All 62 character-stage tests passed on 2026-09-15, including the connected latency/respawn test. Later world-foundation work and its separate verification are tracked in `WORLD_ROLLBACK_IMPLEMENTATION.md`.

Coverage includes existing motor/real-prefab replay tests plus 18 new cases: accepted small/large corrections followed by FishNet's actual history dispatcher, FishNet packet serialization round trips, missing first/middle/last replay input, wrong tick/entity, oversized range, repeated exact-field rollback, input edge policy, upgrade restore/replication, old-life rejection, capsule geometry restore followed by simulation, and two fresh-instance restores with exact field comparisons at every future tick. The harness now treats a rejected correction as a test failure rather than ignoring its return value.

The connected test starts two independent FishNet managers over a real loopback socket, spawns a remote owner, and sends canonical input through the generated replicate method. With client-side latency simulation configured to 80 ms, 10% reordering and 5% packet loss, it verifies the mapped frame before/after replay and exactly one timestep of timer advancement. It then respawns under latency and checks the new generation, position, replicated upgrade and continuing replay. The test bypasses physical input/render updates and uses a runtime-created character in an empty collision world.

These are automated in-process tests. They do not establish two-machine/build behavior, WAN performance, cross-platform numeric determinism, or render-rate-independent dynamic target collisions. Fresh-instance tests share immutable movement configuration and run on the same platform.

**Next acceptance work**

1. Run separate host/client or dedicated-server/client builds on actual game maps with delayed/reordered inputs and respawn under latency; inspect rendered corrections and input feel. Both builds must use the changed packet schema/RPC signature.
2. Expand fresh-instance snapshot tests to map collision seams; add a versioned state manifest/serializer with exact per-field diagnostics for all future gameplay fields.
3. Isolate visual transforms from the collision world, then introduce tick-indexed shared combat/pickup state. Replaying from before a pickup currently cannot regenerate its collection event; the modifier fix restores already-captured upgrades without leaking future configuration backward.
4. Resolve the remaining combat design choices before full interaction rollback: simultaneous hits, target priority and collision/body-blocking rules.

No changes to combat rules, hit trading, parry windows, P2P transport, or cross-platform physics have been made in this milestone.
