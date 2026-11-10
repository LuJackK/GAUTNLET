# Movement determinism on LAN: plan

Date: 2026-11-09. Status: proposed; nothing in this plan has been implemented or run yet.

## Why this first

The host player has a large advantage even on a LAN. With about 1 ms of latency, a joinee correction during plain movement (no hits, no pickups) cannot be blamed on the network: the host and the joinee either ran different inputs, or ran the same inputs from the same state and got different results. Removing each source of drift removes joinee rollbacks under the current host-authority design, without changing the architecture.

It is also the go/no-go test for symmetric rollback (GGPO-style) later. If two PCs cannot stay bit-identical on movement alone, symmetric rollback needs a resync path from day one. If they can, it gets much simpler.

The paired LAN recorder (F9 panel, `SessionCaptureCoordinator` and `NetcodeTraceRecorder`) already logs every tick's input and state as raw float bits on both machines, so this is mostly measure, classify, fix, with little new code. No LAN capture bundles exist in the repo yet, so step 1 is the first real data.

## The premise to test before fixing anything

"Both machines ran the same inputs" is an assumption, not a given. When the joinee's input is not there on a server tick, `TryResolveInput` in `Assets/Scripts/Multiplayer/Player/NetworkedCharacterPredictionReconcileService.cs` substitutes the last held input and the server simulates anyway. From the joinee's seat that looks identical to nondeterminism. The plan separates the two before any fix.

## Step 1: Baseline capture (no code)

- Run the same development build on both PCs, pair with F9, and record movement only: no melee, no pickups.
- Record about two minutes each of idle, walk/strafe, jump and air-strafe, dash and dash-jump, slope slides, wall jump and mantle, and crouch transitions.
- Repeat with the host swapped, to separate role effects from hardware effects.

## Step 2: Extend the analyzer (offline Python only)

`Tools/NetcodeDiagnostics/trace_report.py` currently only compares the owner's prediction to each reconcile and labels the result "cause unproven". Add a per-tick join of the server's `postTick` with the joinee's first-pass `postTick` for the joinee's entity, then classify the first divergent tick of each episode:

| Class | Signature | Meaning |
|---|---|---|
| A: input mismatch | Consumed input differs, or the server ran a substituted or extra tick | Tick or input pipeline bug |
| B: sim nondeterminism | Same pre-tick bits, same input, different post-tick bits | True nondeterminism |
| C: restore gap | Divergence first appears right after a reconcile restore | State missing from the reconcile payload |

Output: counts per class and scenario, plus a histogram of which fields diverge first. Add cases to `Tools/NetcodeDiagnostics/test_trace_report.py`.

Open point: confirm that the server and the owner stamp the same tick number on an input. If they do not, the join goes through the existing `snapshotBuilt` / `snapshotArrived` links.

## Step 3: Fix by class

Suspects from a read of the code, to confirm or clear with the data:

- **A:** Server-side held-input substitution, as above. The FishNet tick rate and prediction settings were not found serialized outside the demo scenes, so they need reading off the NetworkManager.
- **C:** `BuildAuthoritativeMoveData` clones the joinee's own predicted state and overwrites only the wire fields. Any state not on the wire silently survives from the mispredicted timeline.
- **B:** Trig and quaternion calls in `SurfController.cs` (air-strafe heading, around line 176) and `SurfController.Deadlock.cs` (slide turn, around line 240), plus PhysX sweeps and `ComputePenetration`. These are usually bit-identical on the same binary but can differ across CPU vendors. The capture manifest records the CPU.
- **Low risk, noted:** `m_AutoSyncTransforms` is off, so world queries see collider poses as of the last sync. This only matters once world geometry moves.
- **Cleared:** Players do not collide with each other in movement (the player layer is outside the ground mask), and look angles are quantized identically on both sides.

## Step 4: Go/no-go gate for symmetric rollback

- **Live:** ten minutes of movement-only LAN play with zero class B and C divergences.
- **Offline:** replay one captured input trace on both PCs and compare per-tick hashes. The replay harness only runs as editor play-mode tests today, so this needs either the editor on both PCs or a small `-replayTrace` mode in the development build.

## Step 5: Regression guard

Make the analyzer exit nonzero on any class B or C finding, and keep the movement soak as a pre-merge check.

## What this will not fix

Determinism removes the joinee's rollbacks during plain movement. It does not change the joinee seeing the host two ticks behind through interpolation, or melee hits resolving only on the host. Those remain host advantages until the architecture changes.

## Related documents

- `LAN_NETCODE_DIAGNOSTICS_PLAN.md`: the capture tool this plan relies on; step 4's offline gate is its replay lane C.
- `DETERMINISTIC_REPLAY_HARNESS_PLAN.md`: the same-machine replay harness.
- `NETWORK_IMPLEMENTATION_OPTIONS.md`: the architecture options this evidence feeds.
