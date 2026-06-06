# Gameplay Folder Orientation

This folder contains the player-facing gameplay stack: surf-style movement, local input capture, hitbox/hurtbox combat, simple environment markers, and presentation/VFX helpers. Most code is under the `Fragsurf.Movement` namespace, with combat under `Fragsurf.Combat`; a few small environment/presentation scripts are in the global namespace.

## Main Areas

- `Player/Movement/` is the core. `SurfCharacter` owns runtime setup, current `MoveData`, simulation ticks, melee state, transform interpolation, crouch/dash visual offsets, and standalone fallback simulation. `SurfController` applies the movement rules for walking, air movement, dashing, sliding, ladders, underwater movement, jumping, and crouching. `SurfPhysics` provides static collision, trace, friction, acceleration, reflection, and step-offset helpers.
- `Player/Input/` defines the deterministic input payload. `InputFrame` is the canonical tick input with button bit flags, quantized look angles, stick bytes, and edge flags. `LocalInputCollector` samples Unity input each `Update()` and emits one `InputFrame` per requested simulation frame. `InputButtons` is obsolete; prefer `InputFrame.BTN_*`.
- `Combat/` contains reusable `Hitbox` and `Hurtbox` components. `MovementConfig` defines the heavy melee hitbox and player hurtbox data, and `SurfCharacter` pushes those definitions into components at runtime unless the component is configured to keep its own definition.
- `Player/Presentation/` reads movement state and drives visuals only. `CharacterRenderer` updates Animator parameters and forwards one-frame events to `PlayerVFXController`. VFX helpers pool dash lines, cloud poofs, particles, and slide trails.
- `Player/EnvironmentInteraction/` has marker/detector scripts: `Ladder`, `Water`, and `CameraWaterCheck`. `SurfController` detects ladders via `Ladder`; `SurfCharacter` tracks water trigger overlaps and a generated camera water-check trigger.

## Important Flow

1. Input enters as an `InputFrame` from `LocalInputCollector` or the networking layer.
2. `SurfCharacter.SimulationTick(state, input, deltaTime, allowGameplaySideEffects)` applies input into `MoveData`, updates heavy melee state, refreshes water/crouch state, calls `SurfController.ProcessMovement()`, then processes melee hitboxes.
3. `SurfController` mutates the same `MoveData` via `ISurfControllable`, using `SurfPhysics` and `Fragsurf.TraceUtil.Tracer` for ground, ladder, wall, and collision checks.
4. `SurfCharacter.Update()` handles standalone local simulation when no network session is active, applies render interpolation when FishNet timing exists, updates view/render/collider presentation offsets, and calls `CharacterRenderer.ApplyState()`.
5. `CharacterRenderer` and `PlayerVFXController` consume `MoveData` pulse flags like `dashStartedThisFrame`, `doubleJumpedThisFrame`, and melee transitions; they should not write gameplay state.

## Dependencies To Respect

- FishNet is part of the simulation contract. `SurfCharacter`, `LocalInputCollector`, `Hitbox`, and aiming code reference `NetworkObject`, `NetworkedCharacter`, FishNet tick timing, ownership, and server-authoritative melee side effects.
- `PlayerAiming` uses Cinemachine 3 (`CinemachineCamera`, `CinemachineOrbitalFollow`) and disables conflicting `CinemachineInputAxisController` so its own turn clamp can work.
- Movement collision uses layer names through `SurfPhysics.groundLayerMask`: `Default`, `Ground`, and `Player clip`. Combat targeting uses `SurfCharacter.enemyLayerMask`.
- `MovementConfig` is a serialized class, not a `ScriptableObject`; it is expected to be assigned on `SurfCharacter` and copied into `MoveData`/combat components during runtime initialization.

## Gotchas

- `MoveData` is mutable class state and is cloned for rollback/interpolation snapshots. Be careful adding fields: initialize them in respawn/runtime paths and consider whether they need reset in `SimulationTick()`.
- One-frame event flags are cleared at the start of each simulation tick. Presentation should read them only on new rendered ticks to avoid duplicate effects during rollback/resimulation.
- `SimulationTick()` supports `allowGameplaySideEffects=false`; keep replay/prediction paths deterministic and avoid firing real gameplay side effects unless explicitly allowed.
- `Hitbox.CheckHit()` uses overlap queries and skips the owner hurtbox. Cone hitboxes are implemented as an overlap sphere filtered by dot product against the rotated forward vector.
- `SurfCharacter.EnsureRuntimeInitialized()` creates runtime child objects such as `PlayerCollider` and `Camera water check`, may auto-assign camera/view references, and may add `LocalInputCollector` only for legacy non-networked play.
- Several scripts are intentionally simple marker components (`Ladder`, `Water`) and some code still carries legacy comments/obsolete methods. Avoid replacing these unless the calling code is updated too.

## Where To Start

- Movement behavior or tuning: start with `MovementConfig`, then `SurfController`, then `SurfCharacter.SimulationTick()`.
- Network/prediction symptoms: start with `SurfCharacter.SimulationTick()`, `MoveData`, `InputFrame`, and the callers outside this folder that drive FishNet ticks.
- Input bugs: start with `LocalInputCollector` and verify button edge handling in `InputFrame.justPressed`.
- Heavy melee/combat: start with `SurfCharacter.UpdateMeleeState()`, `ProcessHitboxes()`, `MovementConfig.heavyMeleeHitbox`, `Hitbox`, and `Hurtbox`.
- Animation or effects: start with `CharacterRenderer` and `PlayerVFXController`; keep changes presentation-only unless the simulation layer explicitly needs new state.
