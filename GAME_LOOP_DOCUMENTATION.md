# GAUNTLET Game Loop Documentation

Date: 2026-03-22
Scope: runtime loop, data flow, object state transitions, and multiplayer networking behavior.

---

## 1) High-level runtime model

GAUNTLET uses a **host-authoritative multiplayer loop** built on FishNet.

At runtime, each player object is driven by this chain:

1. `LocalInputCollector` captures local controls into `InputFrame`.
2. `NetworkedCharacter` owns authority gates and FishNet RPC flow.
3. `RollbackManager` applies input history/prediction/correction.
4. `SurfCharacter.SimulationTick(...)` mutates `MoveData` via `SurfController` + combat checks.
5. `SurfCharacter.Update()` renders/interpolates state to transforms and animation/VFX.

Key idea:
- **Simulation is tick-based** (`TimeManager.OnTick`).
- **Visuals are frame-based** (`Update`, interpolation).
- **Host is authoritative** for accepted input and replication.

---

## 2) Boot and session loop (menu -> network -> scene)

## 2.1 Menu and startup

`MenuController` starts host/client and loads gameplay scenes globally:
- Host/Tutorial buttons: start server + local client, then `LoadGlobalScenes(...)`.
- Join button: configures Tugboat client address, starts client.

Scene loading is global so joining clients align to the same active map.

## 2.2 Spawn service lifecycle

`PlayerSpawnService` is auto-created (`RuntimeInitializeOnLoadMethod`), survives scene loads, and binds network callbacks.

Server callback path:
- `OnClientLoadedStartScenes` -> waits for the client player object.
- Resolves the player `NetworkObject` (prefers `FirstObject` with `NetworkedCharacter`).
- Applies ownership repair if `FirstObject` owner is wrong.
- Chooses a `SpawnPoint` (random or round-robin) and sets transform pose.

This service is the first place where object ownership and initial world state are normalized.

---

## 3) Per-player object architecture

A spawned player object is effectively a composition of systems:

- `NetworkedCharacter` (network authority + RPC bridge + telemetry + look replication)
- `RollbackManager` (buffered rollback/prediction)
- `SurfCharacter` (state container + simulation entry point + rendering bridge)
- `SurfController` (core locomotion physics rules)
- `PlayerAiming` (owner-side aim input)
- `CharacterRenderer` + `PlayerVFXController` (animation and effects)
- `Hitbox` / `Hurtbox` (melee hit detection)

Support systems:
- `SingleEventSystemGuard` and `NetworkedCharacter` event-system cleanup avoid duplicate UI event systems.
- `RotateWithCamera` is disabled in networked authority path to prevent yaw authority conflicts.

---

## 4) Core data structures and how they flow

## 4.1 Input packet (`InputFrame`)

`InputFrame` is the canonical input payload:
- `frame` (simulation tick id)
- `buttons` bitmask (`JUMP`, `DASH`, `MELEE`, `CROUCH`, ...)
- `stickX`, `stickY` in `sbyte` range `[-127, 127]`
- `_justPressed` pulse bits

Flow:
- Produced by owner only (`LocalInputCollector.GatherInput(frame)`).
- Sent to host via `SendInputServerRpc`.
- Replicated to non-owners via `BroadcastInputObserversRpc`.

## 4.2 Mutable simulation state (`MoveData`)

`MoveData` is the complete mutable simulation state for a character:

- Kinematics: `origin`, `velocity`, `grounded`, `moveType`, `frame`
- Input-derived intents: `wishJump`, `wishDash`, `wishMelee`, axes
- Resources: `stamina`, cooldown timers
- Feature states: sliding, dash, jump counters, underwater/ladder flags
- Combat substates: `MeleeState`, timers, hit flags
- One-frame event pulses: `dashStartedThisFrame`, `doubleJumpedThisFrame`, `meleeHitThisFrame`

Flow:
- Mutated in `SurfCharacter.SimulationTick(...)`.
- Buffered/cloned in `RollbackManager` for historical correction.
- Read by `CharacterRenderer`, UI widgets, VFX, and transform interpolation.

---

## 5) Tick loop (authoritative simulation)

Every FishNet tick, `NetworkedCharacter.TimeManager_OnTick()` runs.

## 5.1 Owner path (local authority)

When `HasLocalAuthority()` is true:

1. Tick id = `TimeManager.Tick`.
2. Gather input -> `InputFrame input`.
3. Send input to host (`SendInputServerRpc(input)`).
4. Send look updates when yaw/pitch changed enough (`SendLookServerRpc`).
5. Locally predict/simulate immediately (`_rollback.Tick(input, dt)`).

Result:
- Owner has responsive local movement (prediction).
- Host later confirms and rebroadcasts canonical input.

## 5.2 Host/server input acceptance

On `SendInputServerRpc`:

1. Validate sender exists.
2. Validate `sender.ClientId == Owner.ClientId`.
3. Enforce monotonic input frames (`ValidateAndTrackServerInputFrame`).
4. Track lag = `serverTick - input.frame` telemetry.
5. Simulate server-side for remote owners (`ReceiveRemoteInput`) or skip duplicate host-owner simulation.
6. Optionally send owner reconciliation snapshot (`TargetRpc` with origin/velocity).
7. Broadcast input to observers (`ObserversRpc`, owner excluded).

This stage is the authoritative gate that prevents wrong-owner control.

## 5.3 Observer/proxy path

Non-owner clients do not gather control input for that player object.
They consume replicated `InputFrame` and call `RollbackManager.ReceiveRemoteInput(...)` to advance/predict/correct remote state.

---

## 6) Rollback and prediction loop

`RollbackManager` maintains ring buffers (size 128) for:
- saved `MoveData` states
- local inputs
- remote inputs
- remote confirmation flags
- per-slot frame stamps

Core behavior:

1. `Tick(input, dt)`:
   - stores pre-sim state clone for frame slot
   - stores local input
   - advances simulation through `SurfCharacter.SimulationTick`

2. `ReceiveRemoteInput(input, dt)`:
   - checks prediction lead (caps by `_maxPredictionLead`)
   - records confirmed remote input
   - if frame is in the past and differs from prediction -> rollback
   - if frame is ahead -> fill missing frames using prediction, then tick forward

3. `Rollback(toFrame, dt)`:
   - restore exact historical frame state if available
   - otherwise fallback and count slot mismatch
   - re-simulate from corrected frame up to current tick

Prediction safety:
- Excessive lead frames are dropped (cap violations tracked) to avoid huge correction spikes.

---

## 7) Simulation internals (`SurfCharacter` + `SurfController`)

## 7.1 `SurfCharacter.SimulationTick(...)`

Per tick, in order:

1. Ensure runtime dependencies exist (`IsSimulationReady`).
2. Set `state.frame` and clone previous state for interpolation.
3. Clear one-frame event flags.
4. `ApplyInputToState` (axes/buttons -> intent fields).
5. `UpdateMeleeState` (heavy melee state machine and turn clamp).
6. Update trigger-derived environmental flags (`underwater`, camera underwater check).
7. Run crouch logic (`SurfController.Crouch`) if enabled.
8. Run locomotion physics (`SurfController.ProcessMovement`).
9. Resolve hitbox/hurtbox combat (`ProcessHitboxes`).
10. Return mutated state.

## 7.2 Movement processing (`SurfController`)

`ProcessMovement` controls mode-specific physics:
- Walk/air movement
- Dash (ground + air variants)
- Jump/double jump
- Sliding and slide steering
- Ladder movement
- Underwater behavior
- Friction/acceleration/gravity/stamina/cooldowns
- Collision/penetration resolution through `SurfPhysics`

`SurfPhysics` supplies low-level collision math (`ResolveCollisions`, `Reflect`, `ClipVelocity`, step offset checks).

---

## 8) Visual frame loop (non-authoritative rendering)

`SurfCharacter.Update()` is visual/post-sim:

1. Applies slide tilt + dash roll visual rotations.
2. Interpolates `transform.position` between `_prevState.origin` and `_moveData.origin` using tick percent.
3. Calls `CharacterRenderer.ApplyState(...)` once per rendered tick boundary.

`CharacterRenderer` then:
- writes animator params (`Speed`, `Grounded`, `Dash`, `DoubleJump`, etc.)
- emits VFX events through `PlayerVFXController`

`PlayerVFXController` handles:
- slide trail alignment to ground
- dash burst particles
- double-jump particles
- melee fire particles

Important:
- These systems **read state only**; they do not author gameplay state.

---

## 9) Combat state flow

Heavy melee is controlled through `MoveData.moveType == HeavyMelee` with substate machine:
- `Charging` -> `Lunging` -> `Recovery` -> `Walk`

During lunge:
- `Hitbox` is activated.
- `ProcessHitboxes` checks overlap each tick.
- On first `Hurtbox` hit, attacker state is updated (`hasHitTarget`, halt horizontal velocity), and target `moveData.velocity` receives knockback impulse.

Networking note:
- Combat effects are deterministic state mutations executed in simulation path and therefore participate in rollback/correction behavior.

---

## 10) UI and read-only gameplay consumers

`VelocityDisplay` (`Speedometer.cs`):
- Reads `player.moveData.velocity` each `Update` and writes TMP text.

`StaminaUI`:
- Reads `player.moveData.stamina` and `movementConfig.maxStamina`.
- Updates segmented fill bars.

These components do not influence simulation authority; they are state observers.

---

## 11) Networking systems and authority guardrails

## 11.1 FishNet roles in this loop

- `NetworkBehaviour` (`NetworkedCharacter`) for ownership-aware behavior.
- `ServerRpc` for owner -> host input/look.
- `ObserversRpc` for host -> non-owner state-driving input/look replication.
- `TargetRpc` for host -> owner reconciliation snapshots.
- `TimeManager.OnTick` for deterministic tick cadence.
- `SceneManager.LoadGlobalScenes` for synchronized map state.

## 11.2 Ownership enforcement

- Input collector and aiming are enabled only for local owner (`ApplyOwnershipState`).
- Mismatch corrections are checked each tick (`EnsureOwnershipStateConsistency`).
- RPC sender/owner mismatches are rejected server-side.
- Spawn service repairs ownership mismatch in first-player-object edge cases.

## 11.3 UI authority hygiene

- `NetworkedCharacter` optionally removes event systems from player hierarchy.
- `SingleEventSystemGuard` enforces one active scene-level event system after scene transitions.

---

## 12) End-to-end timeline example (one owner input frame)

1. Player presses dash + forward.
2. Owner tick captures `InputFrame(frame=F, buttons, stick)`.
3. Owner sends RPC to host and predicts locally at once (`RollbackManager.Tick`).
4. `SurfCharacter.SimulationTick` mutates `MoveData` (dash state, velocity impulse, stamina cost).
5. Host receives same frame, validates ownership/frame order, applies authoritative sim.
6. Host broadcasts input to observers.
7. Observers simulate frame F (and predicted fills if needed).
8. If observer/host predicted differently for past frames, rollback/resim occurs.
9. Render `Update` interpolates transform and updates animator/VFX/UI from resulting state.

---

## 13) What state influences which objects

- `MoveData.origin` -> player transform position interpolation.
- `MoveData.velocity` -> physics movement, speed UI, animation speed, dash VFX direction.
- `MoveData.crouching/crouchLerp` -> collider size, child transform heights, camera vertical offset.
- `MoveData.grounded/jumping/sliding` -> movement branch, animation flags, slide trail.
- `MoveData.stamina` and cooldown timers -> dash/double-jump availability and stamina UI bars.
- `MoveData.meleeState/hasHitTarget` -> melee hitbox active state, turn clamp, lunge termination.
- Replicated look (`yaw/pitch`) -> non-owner body + camera orientation smoothing.

---

## 14) Practical debugging checkpoints

When diagnosing loop issues, check in this order:

1. Ownership state (`ownerId`, local owner gating logs in `NetworkedCharacter`).
2. Input acceptance (`SendInputServerRpc` mismatch/stale/out-of-order logs).
3. Prediction pressure (`maxRemoteLead`, cap violations in rollback telemetry).
4. Simulation readiness (`SurfCharacter.IsSimulationReady`).
5. Visual-only confusion (animation/VFX/UI are observers, not authority).

---

## 15) Current design contract (as implemented)

For multiplayer movement, the authoritative path is:

`LocalInputCollector` -> `NetworkedCharacter` -> `RollbackManager` -> `SurfCharacter.SimulationTick`

Any competing movement/rotation authority path is considered invalid for networked play and should be removed or disabled.
