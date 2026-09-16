# Shared combat and world rollback

Work in progress, 2026-09-15. This extends the verified character rollback milestone; it is not yet an enabled replacement for connected prediction.

## Confirmed design

- Attacks that connect in the same simulation tick trade, including lethal hits. Collect intents before applying damage.
- Retain current movement, stamina, melee timing, parry reflection and upgrade persistence unless a change is explicitly agreed.

Pending designer choices: target selection when an attack overlaps multiple enemies; retaining FishNet transport for this milestone versus including the P2P session transition.

## Current implementation status

- Added deep-cloned shared character/pickup snapshots and a session-owned restorable RNG.
- Added whole-world history that validates every participant's input before replay and commits candidate history only after the complete simulation pass succeeds. Dynamic roster transitions are not integrated yet.
- Added a versioned movement serializer covering all 86 value fields, excluding scene Transform bindings. A manifest test detects future omissions.
- Added a pure simultaneous combat resolver for hit trades, reflected parry damage, hit-induced momentum changes, health and scheduled death state. It returns events instead of invoking live gameplay callbacks.
- These are foundations, **not an enabled world-rollback game mode**. Live hit queries, health integration, pickup spawning/collection, respawns, presentation event deduplication and the shared network driver still need migration.
- Foundation tests use synthetic world stepping and supplied hit intents. They do not validate historical hit geometry, pickup collection or connected world rollback yet.
- Verification: all 71 `Fragsurf.ReplayHarness` PlayMode tests passed on 2026-09-15 (`TestResults/world-rollback.xml`), including nine new foundation cases and the previous 62 character/connected tests.

## Implementation sequence

1. Value snapshots for all characters, health, scheduled respawns, pickup availability/selection/schedules and session RNG. Stable entity identity and one shared tick.
2. World tick phases: lifecycle, canonical inputs and movement, historical collision staging, collect combat intents, apply simultaneous outcomes, collect pickups, capture snapshot. Render transforms must not determine interactions.
3. World history and canonical per-player input history. Correct any changed input by restoring the entire world and replaying all participants. Preflight failures leave the existing timeline intact.
4. Integrate the shared driver with the selected transport. Do not run FishNet per-character replay and world replay over the same entities. Health SyncVars and spawn/despawn operations become presentation/integration outputs, never the replay state source.
5. Automated rewind tests across hit/miss/parry/reflection, lethal trades, delayed respawn, contested pickups, random pickup respawns and corrected inputs. Connected tests must exercise the enabled world path.

## Acceptance limits

Static map geometry and immutable game rules must match. Arbitrary unsnapshotted Unity scripts, animation-driven colliders and PhysX rigidbody simulation are not made rollback-safe merely by restoring character poses. Inventory active world behaviors and either migrate or explicitly reject unsupported participants. Cross-platform numeric determinism requires separate build testing.

Human playtesting follows implementation and automated verification. Keep incomplete paths identified until then.
