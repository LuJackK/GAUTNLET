# Netcode Debug Progress

Last updated: 2026-04-08

## Current focus

- [x] Create a shared tracker for the multiplayer debug plan.
- [x] Inspect the lifecycle for early simulation, runtime readiness, spawn gating, and input binding.
- [x] Land the first high-confidence fixes with minimal surface area.
- [ ] Re-test in Unity: host-only idle, walk, jump/fall.
- [ ] Continue on control-gate validation if host still does not reach `controlReady=True`.
- [ ] Sweep the player prefab for any remaining missing-script cleanup after the runtime path is stable.

## Module 1: Simulation Starts Too Early

Status: In progress

Findings:
- `NetworkedCharacter.TimeManager_OnTick()` only blocked local-authority ticks when `controlReady` was false.
- That allowed prediction to keep ticking on initialized objects before authoritative spawn pose application and before runtime readiness had fully settled.

Changes:
- Added a centralized prediction gate in [NetworkedCharacter.cs](C:\Users\LukaJack\Desktop\GAUTNLET\Assets\Scripts\Multiplayer\Player\NetworkedCharacter.cs) that blocks ticks until:
  - `SurfCharacter` exists,
  - simulation runtime is ready,
  - authoritative spawn pose has been applied,
  - local-control readiness is satisfied for the owning player.
- Added gated trace output so the next host-only run should prove exactly why startup ticks are skipped.

Expected validation log:
- `TimeManager_OnTick skipped-spawn-pose-not-ready`
- No movement/gravity simulation before spawn pose + runtime readiness are true.

## Module 2: Runtime Readiness Is Unstable

Status: First fix landed

Findings:
- `SurfCharacter.EnsureRuntimeInitialized()` grabbed a collider from the root object, destroyed it, and stored that reference in `_collider`.
- Because Unity destroys components asynchronously, `_collider` could become a dead/null reference on later frames, matching the observed `simReady=True` then `missing=[collider]` regression.

Changes:
- Updated [SurfCharacter.cs](C:\Users\LukaJack\Desktop\GAUTNLET\Assets\Scripts\Gameplay\Player\Movement\SurfCharacter.cs) to:
  - destroy the root collider without retaining it as the runtime collider reference,
  - prefer/reacquire the collider that lives under `PlayerCollider`,
  - refresh runtime component references before readiness checks.

Expected validation log:
- `SimulationReady` should remain true after initialization unless the runtime child collider is genuinely absent.

## Module 3: Local Control Gate Never Fully Opens

Status: Investigated, not yet changed directly

Findings:
- The ownership gate already flips `HasAuthoritativeSpawnPose` from `OnAuthoritativeSpawnPoseApplied()`.
- The new startup prediction gate should make it much easier to verify whether host startup is actually missing the spawn-pose callback or just simulating too early.

Next check:
- Confirm host path reaches `spawnPose=True` and `controlReady=True` after `PlayerSpawnService` applies the spawn pose.

## Module 4: LocalInputCollector Is Missing

Status: Fix landed

Findings:
- `Player 1.prefab` referenced `LocalInputCollector` using GUID `05bacbce0fbb697479afcadeb32a505b`.
- The current [LocalInputCollector.cs.meta](C:\Users\LukaJack\Desktop\GAUTNLET\Assets\Scripts\Gameplay\Player\Input\LocalInputCollector.cs.meta) had a different GUID, so the prefab reference was stale/broken.

Changes:
- Restored the `LocalInputCollector` script GUID to the value already serialized by the prefab to repair the existing asset reference.

Expected validation log:
- No more `Missing LocalInputCollector` warning in `NetworkedCharacter.Awake()`.

## Module 5: Missing Script Cleanup

Status: Pending

Notes:
- I have only repaired the confirmed `LocalInputCollector` script binding so far.
- I have not yet done a full prefab/scene sweep for every other missing-script component.

## Validation notes

- Unity runtime re-test still needed from the editor/player to confirm behavior.
- CLI build check is currently blocked on this machine because `Assembly-CSharp.csproj` targets `.NET Framework 4.7.1` and the required targeting pack is not installed.
- If host still fails after this pass, the next likely focus is the host-side spawn-pose/control-ready ordering around [PlayerSpawnService.cs](C:\Users\LukaJack\Desktop\GAUTNLET\Assets\Scripts\Multiplayer\Player\Spawning\PlayerSpawnService.cs) and [NetworkedCharacterOwnershipGateController.cs](C:\Users\LukaJack\Desktop\GAUTNLET\Assets\Scripts\Multiplayer\Player\NetworkedCharacterOwnershipGateController.cs).
