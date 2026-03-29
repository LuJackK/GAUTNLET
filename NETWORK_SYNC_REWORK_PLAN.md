# Network Sync Rework Plan

Goal: move to a single authoritative timeline and reduce host/client desync.

## Milestones

- [x] Step 1: Single input packet per tick (movement + look + edge flags)
- [ ] Step 2: Server-authoritative prediction/reconciliation with acked replay
- [ ] Step 3: One smoothing layer only (visual-only interpolation)
- [ ] Step 4: Explicit correction bands and consistent application
- [ ] Step 5: Telemetry/checksum expansion
- [ ] Step 6: Lag/loss/reorder test matrix and pass criteria
- [ ] Step 7: Edge action reliability (consume-once semantics)

---

## Step 1 Breakdown (In Progress)

### Scope
Unify per-tick input so movement and look are processed together on the same tick path.

### Tasks
- [x] Add look fields to `InputFrame` payload.
- [x] Capture look in `LocalInputCollector` and include it in the same packet.
- [x] Remove separate look RPC send path from `NetworkedCharacter`.
- [x] Apply look from input packet on server before simulation.
- [x] Keep remote look stream aligned from authoritative snapshots.
- [x] Include look in rollback mismatch detection.
- [x] Consume input look in `SurfCharacter` simulation path.

### Notes
- `InputFrame` now carries quantized yaw/pitch (`lookYaw100`, `lookPitch100`).
- Movement and look now share one per-tick RPC path (`SendInputServerRpc`).
- Separate look RPC methods were removed to avoid split timelines.

### Validation Checklist
- [ ] Build compile check in Unity editor.
- [ ] Host + 1 client: verify look and movement stay aligned under normal latency.
- [ ] RTT 100ms simulation: verify reduced snap/correction bursts.
- [ ] Confirm no duplicate edge-action triggers during reconciliation.

---

## Change Log

### 2026-03-24
- Implemented Step 1 code path unification.
- Added this tracking document.
