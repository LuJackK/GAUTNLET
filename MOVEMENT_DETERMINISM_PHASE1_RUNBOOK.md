# Phase 1 runbook: baseline movement capture on two PCs

Companion to `MOVEMENT_DETERMINISM_PLAN.md`, step 1. No code changes. Everything below is taken from `SessionCaptureCoordinator.cs` and `NetcodeDiagnosticsBuild.cs`.

## 0. Before building

- `Assets/Prefabs/Player 1.prefab` has uncommitted changes. The build stamp records `sourceDirty`. Commit or revert it first (or accept a dirty flag in every bundle).
- Do not change anything between building and the last capture. Pairing refuses to start if the two builds differ.

## 1. Build once, copy the whole folder

Editor menu: **Tools > Netcode Diagnostics > Build Windows Development Player**. Or from a shell:

```bash
Unity -batchmode -quit -executeMethod NetcodeDiagnosticsBuild.BuildBatch -netcodeBuildOutput Builds/LAN-Diagnostics
```

Copy the entire output folder to PC B. `netcode-build.json` (artifact hash) must travel with it, and the build must be a Development build or the capture panel doesn't exist.

## 2. Network setup

- Both PCs on the same LAN, ideally wired. Note both IPv4 addresses.
- Allow `GAUTNLET.exe` through Windows Firewall on private networks. Diagnostics pairing uses TCP **28741** and bundle transfer TCP **28742** (A listens, B connects on both); the game transport needs its own port open too.
- Note the CPU model of each PC. The manifest records it, but write it down: a CPU vendor difference matters for class B.

## 3. Per capture (repeat for each scenario below)

1. Start the host game on PC A, join from PC B, and spawn in. Pairing is refused until gameplay is running.
2. Press **F9** on both PCs to open the panel.
3. Enter the same pairing code (6-32 chars) on both. On B, enter A's IPv4 address.
4. A: **A: host diagnostics**. B: **B: join diagnostics**. Wait for "Paired".
5. A: **Arm both recorders**. Recording begins on an agreed tick about 5 s later. Wait for "Recording paired session ...".
6. Perform the scenario on **B (the joinee)**. A can stand still or shadow B; the plan's question is B's corrections.
7. Press **F10** on either PC at the start of each sub-move or whenever something feels off (the marker is sent to both).
8. A: **Stop and export both**.
9. B then sends its bundle to A automatically (gzipped, TCP **28742**, paired peer only). Keep both games open until A's panel says "Received peer bundle" and B's says "Bundle delivered". Both bundles end up on A in `%USERPROFILE%\AppData\LocalLow\DefaultCompany\GAUNTLET\NetcodeCaptures\<session>\`, one folder per PC. If the transfer fails, B's bundle is still on B at the same path and can be copied by hand.

The diagnostics roles are separate from the game roles: keep clicking "A: host diagnostics" on the PC you analyze on, even for the swapped-host captures, so every bundle lands there. Each bundle is capped at 4 GB (about 14 minutes of joinee data at the rate seen in the first idle capture).

A capture fails (partial bundle kept) on a scene change, a disconnect, a 3 s heartbeat gap, or a config mismatch. Re-pair after any failure.

## 4. Scenarios

Movement only: no melee, no pickups.

| # | Scenario | Length | Role |
|---|---|---|---|
| 1 | Idle | 2 min | A hosts |
| 2 | Walk and strafe | 2 min | A hosts |
| 3 | Jump and air-strafe | 2 min | A hosts |
| 4 | Dash and dash-jump | 2 min | A hosts |
| 5 | Slope slides | 2 min | A hosts |
| 6 | Crouch transitions | 2 min | A hosts |
| 7 | Wall jump and mantle | 5 min | A hosts |
| 8 | Repeat 1-7 with PC B hosting | same | B hosts |

Scenario 7 checklist (F10 at each item):
- jump along and past walls without wall jumping
- chained wall jumps
- wall jumps at corners and on angled walls
- mantles from the ground and from the air, at several ledge heights
- ledges near the minimum and maximum mantle height
- mantle into slide
- a jump buffered at the end of a mantle

Keep the same person on the same PC for each swap pair if possible, so input style isn't a variable.

## 5. Sanity-check the data

```bash
python Tools/NetcodeDiagnostics/trace_report.py <bundleA> <bundleB> --out Captures/report-<scenario>
```

Open `report.html`. Check first that the joined tick count is nonzero and the manifest shows matching build, tickRate and fixedDelta on both sides. Per the plan, classifying divergence by class A/B/C is step 2; the analyzer already prints episode counts by class, so read them as a first look, not a verdict.

## 6. Log sheet

Per capture, record: scenario, host PC, session id, CPU of each PC, ping to peer, number of F10 markers, anything the player felt.
