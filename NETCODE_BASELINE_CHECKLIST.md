# Netcode Baseline Checklist

## Purpose
Use this checklist before and after each networking refactor PR so behavior changes are measured the same way.

Use the in-game diagnostics HUD from:

- `Assets/Scripts/SourcePhysics/Movement/NetcodeDiagnosticsHUD.cs`

Recommended setup:

1. Add `NetcodeDiagnosticsHUD` to the local player prefab or a temporary debug object.
2. Point it at:
   - `NetworkedCharacter`
   - `RollbackManager`
   - `SurfCharacter`
3. Record counts before playtest and after playtest.

---

## Feature Flag Snapshot

Record the active values before each run:

- `UseFishNetPredictionPipeline`
- `UseLegacyAuthoritativeBroadcast`
- `UseLegacyInputObserverBroadcast`
- `UseLegacyFrameValidation`

---

## Required Scenarios

### Scenario 1 - Host + 1 Client
- Spawn both players
- Move, jump, crouch, dash, slide, melee
- Run for at least 2 minutes

Record:
- corrections
- rollbacks
- checksum mismatches
- visible snaps
- owner mismatch RPCs

### Scenario 2 - Host + 2 Clients
- Repeat the same movement stress
- Confirm proxies remain stable while owner performs bursts

Record:
- corrections per player
- visible snaps per player
- duplicate inputs
- late inputs

### Scenario 3 - Simulated Latency
- 80-120ms RTT
- Repeat movement and melee chains

Record:
- max correction distance
- rollback count
- predicted fill count
- checksum mismatches
- subjective control feel

### Scenario 4 - Simulated Packet Loss
- 1-3% packet loss
- Repeat dash, slide, jump, melee transitions

Record:
- redundant packets sent
- duplicate/late/too-old inputs
- prediction cap violations
- visible snap count

### Scenario 5 - Late Join
- Join during active movement
- Join during active melee/dash sequence

Record:
- convergence time
- initial correction burst
- visible proxy jitter

### Scenario 6 - Endurance
- 10 minutes repeated dash/slide/melee loops

Record:
- total rollbacks
- checksum mismatches
- slot mismatch fallbacks
- any drift that accumulates over time

---

## Minimum Metrics To Copy Into Each PR

- authority mode used
- corrections per minute
- rollback count
- max remote frame lead
- checksum mismatch count
- duplicate input count
- late input count
- too-old input count
- visible snap tally

---

## Notes

- A checksum mismatch does not automatically prove the exact root cause, but it is a strong signal that predicted state and authoritative state diverged.
- Visible snap count should be a manual tally from the playtester.
- If counts improve but feel gets worse, note that explicitly in the PR.

