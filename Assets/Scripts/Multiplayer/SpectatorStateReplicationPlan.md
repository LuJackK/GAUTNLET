# Spectator State Replication Plan

## Goal

Refine multiplayer state replication so spectator clients see the remote player with the same authoritative movement state the host sees where it matters for presentation, while keeping the current foreign-simulation restrictions in place.

The clarified target for this pass is:

- use the spectator path for remote presentation
- sync yaw only for remote facing
- preserve stripped foreign simulation behavior

This is not a plan to send full gameplay authority from both ends. The intended model remains:

- owner sends input upstream
- server simulates authoritative state
- spectators consume forwarded authoritative movement state for presentation

## Current Findings

### What Is Already Implemented

The current pipeline already sends more than raw transforms:

- owner input includes movement buttons, movement axes, and look angles through `InputFrame`
- the server builds a rich authoritative reconcile payload in `NetworkedCharacterPredictionReconcileService`
- the player prefab has FishNet prediction and state forwarding enabled

That means the problem is not simply "spectators only receive transforms."

### Where Parity Breaks Down

The biggest mismatch is in how remote presentation applies state after it arrives.

- authoritative yaw and pitch are restored into `MoveData.viewAngles`
- local owners apply body rotation through `PlayerAiming`
- remote spectators do not have an equivalent steady-state yaw application path
- `NetworkedCharacter.ApplyLookRotation(...)` exists, but it is currently only used for spawn pose application

This explains the reported symptom that both sides fail to see correct rotation.

### Animation/VFX Trigger Gap

There is also a likely parity gap for one-frame presentation pulses:

- `dashStartedThisFrame`
- `doubleJumpedThisFrame`
- `meleeHitThisFrame`

Those flags exist on `MoveData`, but they are not part of the current authoritative reconcile payload. The host may still appear richer because it runs authoritative/local simulation directly, while the spectator path may miss some pulse-style presentation events.

## What "Stripped Foreign Simulation" Means

In the current code, non-local simulation intentionally strips some state when simulating foreign characters.

Today that especially includes crouch-related state:

- `crouching`
- `crouchLerp`
- `renderCrouchLerp`
- `uncrouchDown`

This behavior is applied through `ShouldIgnoreCrouchForForeignSimulation` and related stripping logic.

For this plan, that behavior should remain in place. In other words:

- do not make spectators fully simulate every local-only or presentation-sensitive field
- do not remove the current crouch-stripping behavior unless a later test pass shows it is required

## Plan

### 1. Verify Spectator Consumption of Forwarded State

Add temporary diagnostics around the spectator path to confirm:

- forwarded replicate ticks are arriving on non-owners
- reconcile ticks are arriving on non-owners
- the non-owner is applying refreshed `MoveData` every tick

This step is meant to separate transport issues from presentation issues before changing behavior.

### 2. Add a Dedicated Spectator Yaw Application Path

Implement a remote-presentation path that applies authoritative yaw for non-owned characters after state is updated.

Requirements:

- apply yaw only
- do not apply pitch to the remote view hierarchy for this pass
- do not rely on `PlayerAiming`, since that is owner-driven
- keep spawn-time yaw application and steady-state yaw application consistent

Recommended shape:

- add a small helper for spectator-facing application in `NetworkedCharacter` or `SurfCharacter`
- drive body yaw from authoritative `MoveData.viewAngles.y`
- call it whenever authoritative state is refreshed, not only on spawn

### 3. Keep Foreign-Simulation Stripping Intact

Preserve the current stripped foreign-simulation behavior while adding yaw sync.

Specifically:

- leave crouch stripping unchanged
- avoid broadening the spectator path into full unrestricted remote simulation
- keep the new yaw path presentation-focused rather than authority-changing

### 4. Audit Spectator Presentation Triggers

Review whether the current spectator path can reliably reproduce:

- dash start
- dash end
- double jump
- melee start/end

If existing forwarded simulation already produces those transitions deterministically, keep the system simple.

If not, add the smallest possible spectator-facing pulse support rather than broad new state mirroring.

Recommendation:

- only serialize extra pulse flags if testing proves the current state reconstruction is insufficient

### 5. Unify Host and Spectator Presentation Expectations

After yaw sync is corrected, compare host and spectator presentation for:

- body facing direction
- slide direction readability
- dash readability
- melee readability

The goal is not pixel-perfect parity in every internal field. The goal is that spectators see the same gameplay-relevant orientation and major motion beats.

## Implementation Notes

### Preferred Scope for First Pass

Keep the first implementation narrow:

- fix remote yaw presentation
- preserve crouch stripping
- inspect animation pulse parity
- only extend payloads if proven necessary

This reduces the risk of destabilizing rollback, owner feel, or the existing authority contract.

### Avoid for This Pass

Do not do the following unless testing shows a real need:

- send full `MoveData` from client to server
- make spectators apply remote pitch
- remove foreign-simulation stripping
- replace the prediction pipeline with a transform-only sync path

## Validation Checklist

After implementation, test these cases on host and spectator:

1. Remote player turns left and right while standing still.
2. Remote player turns while moving.
3. Remote player dashes, then exits dash.
4. Remote player double jumps.
5. Remote player enters and exits melee.
6. Remote player crouches/slides and still follows the preserved stripping rules.

Success criteria:

- spectator always sees correct body yaw
- host and spectator agree on the remote player facing direction
- major movement and combat animation beats are visible on spectators
- preserved foreign-simulation stripping still behaves as before

## Expected First Deliverable

The first code pass should produce:

- a reliable spectator yaw sync path
- no regression to owner control or rollback behavior
- no removal of current foreign-simulation stripping
- a clear answer on whether extra spectator pulse replication is still needed
