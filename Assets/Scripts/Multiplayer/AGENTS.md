# Multiplayer Folder Orientation

This folder owns the FishNet-backed multiplayer player flow: spawning, ownership-safe local control, prediction/reconcile, rollback correction, spectator presentation, and netcode diagnostics. It does not contain the base movement implementation; it wraps and synchronizes `SurfCharacter`, `MoveData`, `InputFrame`, `LocalInputCollector`, and `PlayerAiming` from the movement/combat code.

## Layout

- `Player/NetworkedCharacter.cs` is the central `NetworkBehaviour`. Start here for most multiplayer player changes.
- `Player/NetworkedCharacterPredictionData.cs` defines FishNet replicate/reconcile payloads.
- `Player/NetworkedCharacterPredictionReconcileService.cs` runs input replicates, builds authoritative reconcile data, evaluates divergence, applies rollback, and updates spectator yaw presentation.
- `Player/RollbackManager.cs`, `RollbackReplayEngine.cs`, and `RollbackStateHistory.cs` store predicted `MoveData` and replay buffered inputs after authoritative corrections.
- `Player/Correction*.cs` and `StateDivergence*.cs` implement the correction gate that decides `Ignore`, `ObserveOnly`, `HardCorrect`, or `ForceCorrect`.
- `Player/NetworkedCharacterOwnershipGateController.cs` delays local input/aiming until ownership and spawn pose are settled.
- `Player/NetworkedCharacterCameraOwnershipGuard.cs` ensures only the locally owned player has active camera, audio listener, and child virtual camera.
- `Player/Spawning/PlayerSpawnService.cs` auto-creates a persistent spawn service, waits for FishNet player objects, repairs obvious ownership mismatches, and applies authoritative spawn/respawn poses.
- `Player/Spawning/SpawnPoint.cs` marks valid scene spawn locations.
- `Player/Debug/NetcodeDebugEngine.cs` and `NetcodeDiagnosticsHUD.cs` provide runtime logs, scenario prompts, counters, and on-screen diagnostics.
- `CorrectionThresholdGatePlan.md` and `SpectatorStateReplicationPlan.md` are design notes for the current correction and spectator-presentation direction.

## Main Flow

1. FishNet starts a `NetworkedCharacter`; it resolves local references, initializes `RollbackManager`, subscribes to ticks, and keeps local input/aiming disabled until ownership is ready.
2. `PlayerSpawnService` waits for the server-side player object, chooses a `SpawnPoint` or fallback pose, then calls `ApplyAuthoritativeSpawnPoseServer`.
3. On each FishNet tick, `NetworkedCharacter` gathers local owner input or reuses replicated input for proxies, then calls the `[Replicate]` method `RunInputs`.
4. `NetworkedCharacterPredictionReconcileService.RunReplicate` resolves input availability and generation, seeds missing input from the restored state, runs `RollbackManager.SimulatePredictedTick`, and refreshes spectator yaw presentation for non-owners. FishNet retains the connected input history.
5. On server post-tick, `CreateReconcile` serializes authoritative movement/combat state into `NetworkedCharacterReconcileData`.
6. Clients rebuild authoritative `MoveData` using FishNet's mapped `GetTick()` for the owner input clock. `StateDivergenceEvaluator` is diagnostic; every accepted reconcile restores a baseline.
7. FishNet alone replays connected inputs. Never call `ApplyAuthoritativeCorrection` inside a FishNet reconcile: that API preflights and replays standalone/harness input. `RestoreAuthoritativeState` only loads a baseline. Replay passes suppress live melee queries.
8. Respawns advance `MovementEpoch`. Old-life inputs and reconciles are rejected; explicit spawn RPCs clear FishNet caches. A new-epoch reconcile may arrive before the pose RPC and takes precedence.

## Key Dependencies

- FishNet prediction APIs: `[Replicate]`, `[Reconcile]`, `IReplicateData`, `IReconcileData`, `NetworkObject.EnablePrediction`, and state forwarding.
- Movement types in `Fragsurf.Movement`: `SurfCharacter`, `MoveData`, `InputFrame`, `LocalInputCollector`, `PlayerAiming`, and `MoveType`.
- Combat/health hooks: `Fragsurf.Combat.Hitbox`, player hurtbox callbacks, and `PlayerHealthBillboard`.
- Unity scene objects: active `SpawnPoint` components are discovered after server scene loads; otherwise `PlayerSpawnService` uses its fallback pose.

## Gotchas

- FishNet prediction must be enabled on the player `NetworkObject`; otherwise remote movement may freeze because legacy broadcast paths are disabled.
- Local control is intentionally gated. Do not enable `LocalInputCollector` or `PlayerAiming` directly; go through `RefreshOwnershipState`/ownership gate behavior.
- Spawn pose matters for prediction startup. The owner normally waits for an authoritative spawn pose, with a short fail-safe for local authority.
- FishNet owns connected input history. `HasInput` and `ReplicateState.Created` determine input availability before tick stamping. Predicted input holds controls/look, clears press edges, and uses `MoveData.lastInput` as its restored seed.
- `RollbackStateHistory` is a 256-frame ring buffer. Standalone correction preflights the range, returns false with `LastReplayError` on invalid inputs/window, and retains the old timeline. Callers must handle failure.
- Owner, server, and spectator simulation now consume identical crouch input. `ShouldIgnoreCrouchForForeignSimulation` returns false for compatibility. Crouch changes collision geometry and lunge cancellation; never strip it according to ownership.
- Spectator presentation currently applies authoritative yaw only; pitch is not a steady-state spectator presentation target.
- Camera ownership is enforced repeatedly in `LateUpdate` so remote player cameras/audio listeners do not become active.
- `PlayerSpawnService` auto-creates after scene load and calls `DontDestroyOnLoad`; check for duplicates or stale persistent instances when debugging scene transitions.

## Where To Start

- Prediction or reconcile bugs: start in `NetworkedCharacterPredictionReconcileService`, then inspect `NetworkedCharacterPredictionData` and `RollbackManager`.
- Over-correction, drift, or jitter: start with `StateDivergenceEvaluator` and `CorrectionGateConfig`, then watch `NetcodeDiagnosticsHUD`.
- Spawn/respawn or ownership races: start in `PlayerSpawnService` and `NetworkedCharacterOwnershipGateController`.
- Remote player facing/presentation: start with `ApplySpectatorPresentationYawFromCurrentState` in `NetworkedCharacter` and the spectator sections of `NetworkedCharacterPredictionReconcileService`.
- Camera/input leaking onto remote players: start in `NetworkedCharacterCameraOwnershipGuard` and ownership gate refreshes.
- Debugging live sessions: add or enable `NetcodeDiagnosticsHUD`; use `NetcodeDebugEngine` hotkeys/scenario presets for repeatable manual checks.
