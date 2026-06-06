# UI Folder Orientation

This folder contains small Unity UI helpers for menus, player HUD readouts, world-space health labels, and EventSystem cleanup. Most scripts are MonoBehaviours wired through the Inspector; there is no central UI framework here.

## Key Scripts

- `MenuController.cs` drives the main menu host/join/tutorial buttons. It uses FishNet `NetworkManager`, the Tugboat transport, and FishNet scene loading to start a host/client and load either `TutorialScene` or `Map1`.
- `PlayerHealthBillboard.cs` creates and updates a world-space `TextMesh` health label above non-owned players. It subscribes to `NetworkedCharacter.HealthChanged`, hides the label for the owning player, and faces the active camera in `LateUpdate`.
- `SingleEventSystemGuard.cs` is a static runtime guard that runs after scene loads. It keeps one active `EventSystem`, preferring a scene-level one over any attached under a FishNet `NetworkObject`.
- `Speedometer.cs` defines `VelocityDisplay` and shows `SurfCharacter.moveData.velocity` in a TMP text field. The class/file name mismatch is intentional in the current codebase but can be surprising.
- `StaminaUI.cs` builds stamina bar instances from a prefab and fills them from `SurfCharacter.moveData.stamina` and `movementConfig.maxStamina`.

## Dependencies And Flows

- Networking UI depends on FishNet (`InstanceFinder`, `NetworkManager`, scene management) and Tugboat for client address selection.
- HUD readouts depend on `Fragsurf.Movement.SurfCharacter`, especially `moveData` and `movementConfig`.
- Text UI uses TextMeshPro for menu/speed labels, while health billboards use Unity `TextMesh` in world space.
- Scene transitions can create duplicate EventSystems, so `SingleEventSystemGuard` enforces one active system globally after each scene load.

## Gotchas

- Many references are public or serialized fields expected to be assigned in Unity. Null checks generally fail quietly, so broken Inspector wiring can look like "nothing happens."
- `MenuController` hides `menuCanvas` immediately after starting host/join flows; scene loading is only done by host/tutorial paths after the server reports started.
- `PlayerHealthBillboard.Initialize(...)` must be called by another gameplay script; this component does not discover its character on its own.
- `StaminaUI` assumes `barContainer` is assigned and that `barPrefab` has either a child named `Fill` with an `Image` or an `Image` on the root.
- Namespace usage is mixed: `PlayerHealthBillboard` and `SingleEventSystemGuard` are in `GAUNTLET.UI`, while `MenuController`, `VelocityDisplay`, and `StaminaUI` are global namespace classes.

## Where To Start

- Menu or connection button behavior: start with `MenuController.cs`.
- Duplicate EventSystem warnings or UI input focus problems after loading scenes: start with `SingleEventSystemGuard.cs`.
- Speed or stamina HUD changes: start with `Speedometer.cs` and `StaminaUI.cs`, then inspect the assigned prefabs/scene objects in Unity.
- Overhead health label changes: start with `PlayerHealthBillboard.cs` and find the gameplay code that calls `Initialize`.
