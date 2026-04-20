# GAUNTLET Codebase Reading Guide

This guide is for someone who does not know the project yet and wants to build a high-level understanding of the runtime loop without getting lost in math-heavy movement code too early.

The shortest possible summary is:

1. `MenuController` starts networking and loads the gameplay scene.
2. `PlayerSpawnService` finds the spawned player object and gives it a valid spawn pose.
3. `NetworkedCharacter` is the runtime traffic cop.
4. `LocalInputCollector` packages local input into `InputFrame`.
5. `RollbackManager` stores history and replays simulation when authoritative input/state arrives.
6. `SurfCharacter.SimulationTick(...)` is the main gameplay simulation entry point.
7. `SurfController.ProcessMovement(...)` contains most movement rules.
8. `SurfCharacter.Update()` and `CharacterRenderer.ApplyState(...)` turn state into visuals.

If you keep that mental model in your head, the code becomes much easier to navigate.

## What To Ignore At First

On your first pass, treat these as black boxes:

- `Assets/FishNet/...`
  This is the networking library, not your game logic.
- `Assets/Plugins/...`
  Third-party/editor/plugin code.
- `Assets/Scripts/SourcePhysics/Movement/SurfPhysics.cs`
  This is the low-level collision and surf math helper layer.
- `Assets/Scripts/SourcePhysics/TraceUtil/...`
  These are tracing helpers used by movement and collision checks.
- `NetcodeDebugEngine`, `NetcodeDiagnosticsHUD`
  Useful for debugging, but not required to understand the game loop.

You do not need to understand every vector math formula in order to understand the architecture.

## The Best Reading Order

Read in this order.

### 1. Start With Startup

Read these files first:

- `ProjectSettings/EditorBuildSettings.asset`
- `Assets/Scripts/MenuController.cs`

What to look for:

- The enabled scenes are `MenuScreen` and `Map1`.
- `MenuController` is the human-readable boot path.

Read these methods:

- `Start()`
- `OnHostClicked()`
- `LoadSceneWhenStarted()`
- `OnJoinClicked()`
- `OnTutorialClicked()`
- `LoadTutorialWhenStarted()`

What you should learn:

- The game begins in a menu scene.
- Hosting starts both server and local client.
- Joining starts only the client.
- Scene loading is done globally through FishNet so clients end up in the same gameplay map.

Do not overthink the FishNet API calls here. Just note: menu input leads to a network session plus scene load.

### 2. Understand How Players Get Into The World

Read:

- `Assets/Scripts/Networking/SpawnPoint.cs`
- `Assets/Scripts/Networking/PlayerSpawnService.cs`

Read these methods:

- `AutoCreate()`
- `BindNetworkManagerWhenReady()`
- `SceneManager_OnClientLoadedStartScenes(...)`
- `RepositionWhenReady(...)`
- `ResolveConnectionPlayerObject(...)`
- `GetSpawnPose(...)`

Then jump to:

- `Assets/Scripts/SourcePhysics/Movement/NetworkedCharacter.cs`
- `ApplyAuthoritativeSpawnPoseServer(...)`

What you should learn:

- Spawn handling is a real runtime system, not just scene setup.
- The server waits until FishNet has created the player's `NetworkObject`.
- It resolves the correct owned player object.
- It chooses a `SpawnPoint`.
- It applies spawn through `NetworkedCharacter`, not just by moving the transform directly.

This is important because a lot of later code is about authority and synchronization. Spawn is where that contract starts.

### 3. Learn The Smallest Important Data Structure

Read:

- `Assets/Scripts/SourcePhysics/Movement/InputFrame.cs`
- `Assets/Scripts/SourcePhysics/Movement/MoveData.cs`

Read fully. They are short and important.

What to learn from `InputFrame`:

- It is the canonical per-tick input packet.
- It contains buttons, stick axes, and quantized look angles.

What to learn from `MoveData`:

- It is the mutable simulation state for a player.
- It stores position, velocity, grounded state, crouch/slide/dash state, melee state, stamina, ladder state, underwater state, and one-frame event flags.

If you understand `InputFrame` and `MoveData`, you understand the input/output contract of the whole movement loop.

### 4. Understand Where Local Input Comes From

Read:

- `Assets/Scripts/SourcePhysics/Movement/LocalInputCollector.cs`
- `Assets/Scripts/SourcePhysics/PlayerAiming.cs`

Read these methods in `LocalInputCollector`:

- `Awake()`
- `Update()`
- `SampleBufferedState()`
- `GatherInput(int frame)`
- `ReadCurrentLook(...)`

Read this method in `PlayerAiming`:

- `Update()`

What you should learn:

- `PlayerAiming` owns the local look direction.
- `LocalInputCollector` samples buttons/axes/look every frame.
- `GatherInput(...)` packages that sampled state into an `InputFrame` for a simulation tick.

Important mental model:

- `PlayerAiming` is frame-based local presentation/input.
- `LocalInputCollector` turns that into tick-friendly data.

### 5. Read The Main Orchestrator

Read:

- `Assets/Scripts/SourcePhysics/Movement/NetworkedCharacter.cs`

This is the most important file for high-level understanding.

Do not try to read it top-to-bottom in one pass. Read it in this order instead.

First pass methods:

- `Awake()`
- `OnStartClient()`
- `OnOwnershipClient(...)`
- `OnStartNetwork()`
- `TimeManager_OnTick()`
- `SendInputServerRpc(...)`
- `BroadcastInputObserversRpc(...)`
- `ApplyAuthoritativeSnapshot(...)`
- `ApplyAuthoritativeSpawnPoseServer(...)`
- `HasLocalAuthority()`

Second pass only if you want prediction details:

- `PerformPredictionReplicate(...)`
- `BuildPredictionReconcileData()`
- `PerformPredictionReconcile(...)`
- `CreateReconcile()`

What this file is doing conceptually:

- It decides whether this instance is owner, proxy, server, or host.
- It decides whether local input is allowed yet.
- On each FishNet tick, it gathers local input if this player has authority.
- It sends input to the server.
- It runs local prediction through `RollbackManager`.
- It receives/broadcasts authoritative state and input.
- It reconciles prediction with server truth.

If you only remember one sentence from this file, make it this:

`NetworkedCharacter` is the layer that connects FishNet's tick/authority world to your own movement simulation world.

### 6. Read Rollback As A History Buffer, Not As Magic

Read:

- `Assets/Scripts/SourcePhysics/Movement/RollbackManager.cs`

Read these methods:

- `Initialize(...)`
- `ReceiveRemoteInput(...)`
- `Tick(...)`
- `ObserveAuthoritativeFrame(...)`
- `ReconcileAuthoritativeFrame(...)`
- `Rollback(...)`

What you should learn:

- It stores predicted states and authoritative states in ring buffers.
- It can replay the simulation from an older frame when remote truth arrives.
- It does not invent gameplay rules.
- It re-runs `SurfCharacter.SimulationTick(...)`.

Very important mindset:

- `RollbackManager` is not the movement system.
- It is a time machine wrapped around the movement system.

### 7. Read The Real Simulation Entry Point

Read:

- `Assets/Scripts/SourcePhysics/Movement/SurfCharacter.cs`

This is the best file for understanding the game loop itself.

Read these methods in order:

- `Start()`
- `IsSimulationReady`
- `SimulationTick(...)`
- `ApplyInputToState(...)`
- `UpdateMeleeState(...)`
- `ProcessHitboxes(...)`
- `LoadState(...)`
- `SyncRuntimeStateFromMoveData()`
- `Update()`
- `RunStandaloneSimulationIfNeeded()`

What `SurfCharacter` is responsible for:

- Holding the current `MoveData`
- Owning the top-level simulation tick
- Calling into movement code
- Calling into combat hit detection
- Syncing runtime collider/melee state from `MoveData`
- Driving visual interpolation and renderer updates

Read `SimulationTick(...)` slowly. That method is the cleanest summary of the gameplay loop:

1. Prepare state for this tick.
2. Clear one-frame event flags.
3. Convert `InputFrame` into gameplay intent.
4. Update melee state machine.
5. Update environment flags like water.
6. Run crouch and movement.
7. Run hitbox/hurtbox combat.
8. Store diagnostics and return the new state.

If you only have time to read one gameplay method in the whole repo, read `SurfCharacter.SimulationTick(...)`.

### 8. Read Movement Rules Without Getting Lost In Math

Read:

- `Assets/Scripts/SourcePhysics/Movement/SurfController.cs`

Read these methods in this order:

- `ProcessMovement(...)`
- `CalculateMovementVelocity()`
- `CheckGrounded()`
- `GetWishValues(...)`
- `Jump()`
- `DoubleJump()`
- `SlideMovement()`
- `UnderwaterPhysics()`
- `LadderCheck(...)`
- `LadderPhysics()`
- `Crouch(...)`

What `SurfController` is:

- The rules engine for locomotion modes.

What it is not:

- It is not the owner of networking.
- It is not the owner of history/rollback.
- It is not the owner of rendering.

How to read it:

- First understand `ProcessMovement(...)` as the dispatcher.
- Then understand `CalculateMovementVelocity()` as the main movement mode switch.
- Then read the smaller helpers only when a branch inside `CalculateMovementVelocity()` points you there.

What to treat as semi-black-box on first pass:

- The exact vector math in `Accelerate(...)`
- The exact air acceleration behavior in `AirInputMovement()`
- The exact slope and reflection math

Those are important for tuning, but not for architectural understanding.

### 9. Treat `SurfPhysics` As A Deferred Layer

Read only enough to know what it provides:

- `ResolveCollisions(...)`
- `AirAccelerate(...)`
- `Reflect(...)`
- `ClipVelocity(...)`

Files:

- `Assets/Scripts/SourcePhysics/Movement/SurfPhysics.cs`
- `Assets/Scripts/SourcePhysics/TraceUtil/Tracer.cs`
- `Assets/Scripts/SourcePhysics/TraceUtil/Trace.cs`

What to learn:

- `SurfController` asks `SurfPhysics` to do collision resolution and surf-style velocity adjustment.
- `Tracer` is the low-level collision query helper.

What not to do on a first pass:

- Do not stop here and try to prove every equation correct.
- Do not block your whole understanding of the codebase on this layer.

For a newcomer, it is enough to know:

- `SurfCharacter` decides when to simulate.
- `SurfController` decides what kind of movement should happen.
- `SurfPhysics` performs the hard geometry work.

### 10. Read Combat Only After Movement Makes Sense

Read:

- `Assets/Scripts/SourcePhysics/Combat/Hitbox.cs`
- `Assets/Scripts/SourcePhysics/Combat/Hurtbox.cs`

Read these methods:

- `Hitbox.CheckHit(...)`
- `Hitbox.BuildQueryPose(...)`
- `Hitbox.GetOverlaps(...)`
- `Hurtbox.TakeHit(...)`

Then revisit:

- `SurfCharacter.UpdateMeleeState(...)`
- `SurfCharacter.ProcessHitboxes(...)`

What you should learn:

- Melee is just another simulation state machine stored in `MoveData`.
- The lunge opens a hit window.
- `ProcessHitboxes(...)` asks `Hitbox` to query for a `Hurtbox`.
- A hit changes state and optionally triggers side effects.

### 11. Read Rendering Last

Read:

- `Assets/Scripts/SourcePhysics/Movement/CharacterRenderer.cs`
- `Assets/Scripts/PlayerVFXController.cs`
- `Assets/Scripts/Speedometer.cs`
- `Assets/Scripts/StaminaUI.cs`

Read these methods:

- `CharacterRenderer.ApplyState(...)`
- `PlayerVFXController.ApplyState(...)`
- `PlayerVFXController.OnDash(...)`
- `PlayerVFXController.OnDoubleJump()`
- `PlayerVFXController.OnMeleeStart()`
- `PlayerVFXController.OnMeleeEnd()`

What you should learn:

- These systems mostly read `MoveData`.
- They are presentation, not simulation.
- `SurfCharacter.Update()` feeds them current and previous state.

This is where the state becomes animation, particles, and HUD.

## The High-Level Game Loop

When the game is running, the useful mental loop is:

1. Menu/network startup creates a session.
2. Spawn service places the player.
3. Each local frame, aiming/input components sample controls.
4. Each network tick, `NetworkedCharacter` gathers and sends input.
5. `RollbackManager` advances or replays simulation.
6. `SurfCharacter.SimulationTick(...)` mutates `MoveData`.
7. `SurfController` applies the movement rules.
8. Combat queries run if melee is active.
9. `SurfCharacter.Update()` interpolates and renders state.
10. UI and VFX read that state.

That is the codebase in one loop.

## A Practical Three-Pass Strategy

If you want to learn this codebase efficiently, use this strategy.

### Pass 1: Learn The Skeleton

Read only:

- `MenuController`
- `PlayerSpawnService`
- `InputFrame`
- `MoveData`
- `NetworkedCharacter.TimeManager_OnTick(...)`
- `RollbackManager.Tick(...)`
- `SurfCharacter.SimulationTick(...)`
- `SurfCharacter.Update()`

Goal:

- Be able to explain the runtime flow in plain English.

### Pass 2: Learn The Movement Modes

Read:

- `SurfController.ProcessMovement(...)`
- `CalculateMovementVelocity()`
- `CheckGrounded()`
- `Jump()`
- `DoubleJump()`
- `SlideMovement()`
- `UnderwaterPhysics()`
- `LadderPhysics()`

Goal:

- Be able to explain how walk, air, dash, slide, ladder, and water states differ.

### Pass 3: Learn The Netcode Details

Read:

- `SendInputServerRpc(...)`
- `BroadcastInputObserversRpc(...)`
- `ReceiveRemoteInput(...)`
- `Rollback(...)`
- `ApplyAuthoritativeSnapshot(...)`
- prediction replicate/reconcile methods in `NetworkedCharacter`

Goal:

- Be able to explain prediction, correction, and authority without needing every FishNet detail.

## Files That Matter Most

If someone asked "which files actually define the game loop?", I would name these first:

- `Assets/Scripts/MenuController.cs`
- `Assets/Scripts/Networking/PlayerSpawnService.cs`
- `Assets/Scripts/SourcePhysics/Movement/InputFrame.cs`
- `Assets/Scripts/SourcePhysics/Movement/MoveData.cs`
- `Assets/Scripts/SourcePhysics/Movement/LocalInputCollector.cs`
- `Assets/Scripts/SourcePhysics/Movement/NetworkedCharacter.cs`
- `Assets/Scripts/SourcePhysics/Movement/RollbackManager.cs`
- `Assets/Scripts/SourcePhysics/Movement/SurfCharacter.cs`
- `Assets/Scripts/SourcePhysics/Movement/SurfController.cs`
- `Assets/Scripts/SourcePhysics/Movement/CharacterRenderer.cs`

If you understand those, you understand the codebase at a high level.

## Files You Can Safely Defer

You can leave these until later:

- `Assets/Scripts/SourcePhysics/Movement/SurfPhysics.cs`
- `Assets/Scripts/SourcePhysics/TraceUtil/Trace.cs`
- `Assets/Scripts/SourcePhysics/TraceUtil/Tracer.cs`
- `Assets/Scripts/SourcePhysics/Movement/MovementConfig.cs`
  Read this later when you want to tune gameplay numbers.
- `Assets/Scripts/SourcePhysics/Movement/NetcodeDebugEngine.cs`
- `Assets/Scripts/SourcePhysics/Movement/NetcodeDiagnosticsHUD.cs`

## Final Mental Model

If you are ever lost, come back to this sentence:

The codebase revolves around one mutable player state object (`MoveData`) that is produced from input (`InputFrame`) on network ticks, corrected by rollback, and then rendered every frame.

That is the center of gravity of the whole project.
