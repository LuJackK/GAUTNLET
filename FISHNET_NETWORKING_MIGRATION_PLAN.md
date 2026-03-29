# FishNet Networking Migration Plan (Step-by-Step)

## Purpose
Use FishNet built-in networking features where possible, reduce custom sync complexity, and keep project-specific gameplay/UI ownership logic intact.

---

## Scope

### Keep (project-specific)
- Camera and `AudioListener` ownership control.
- Virtual camera ownership behavior.
- EventSystem cleanup from player hierarchy.
- Any anti-cheat checks tied directly to game rules (`characterObjectId`, movement sanity limits, etc.).

### Migrate to FishNet built-ins (target)
- Owner input RPC security flow where FishNet can enforce ownership.
- Manual input packet de-dup/out-of-order logic where transport already guarantees behavior needed.
- Manual authoritative snapshot broadcast/reconcile loop, if replacing with FishNet prediction/reconciliation pipeline.

---

## Phase 0 — Baseline and Safety Net

### 0.1 Build a repeatable test matrix
- Host + 1 client.
- Host + 2 clients.
- Late join client.
- Spawn/despawn stress.
- Ownership change (if applicable).

### 0.2 Add temporary instrumentation
- Input accepted/rejected counts.
- Reconciliation correction count and max correction distance.
- Camera ownership invariant warnings.

### 0.3 Capture baseline metrics
- Subjective smoothness.
- Correction frequency.
- Bandwidth snapshot from FishNet/LiteNetLib stats.

**Exit criteria**
- Baseline logs and observations documented.

---

## Phase 1 — Low-Risk Cleanup (No Architecture Change)

### 1.1 Simplify owner checks in input path
- Keep explicit checks only where game-specific security is needed.
- Avoid duplicated checks that FishNet already enforces.

### 1.2 Review frame filtering logic
- Keep manual stale/out-of-order rejection only if required for anti-cheat policy.
- Otherwise remove/reduce custom frame tracking.

### 1.3 Keep current movement flow intact
- Do **not** change rollback architecture yet.

**Exit criteria**
- No functional regression in baseline matrix.
- Reduced branching/validation code in input RPC path.

---

## Phase 2 — Introduce FishNet Prediction/Reconcile Path

### 2.1 Add prediction data contracts
- Define replicate and reconcile data structs aligned with current `InputFrame`/state requirements.

### 2.2 Wire prediction lifecycle
- Owner sends replicate data.
- Server simulates authoritative state.
- Reconcile to owner using FishNet flow.

### 2.3 Gradually disable custom loop
Disable in steps behind feature flags:
1. Custom input observer broadcast.
2. Custom authoritative snapshot broadcast.
3. Manual owner/proxy correction code paths.

**Exit criteria**
- FishNet reconcile runs end-to-end.
- Existing gameplay feel remains acceptable.

---

## Phase 3 — Look/Rotation Stream Consolidation

### 3.1 Choose one authority path for look
- Either include look in prediction stream,
- or replicate look separately (single path only).

### 3.2 Remove competing look writers
- Ensure no duplicate remote/local look updates fight each other.
- Keep optional smoothing only for remote visual presentation.

**Exit criteria**
- Stable remote aim visuals.
- No jitter from multiple writers.

---

## Phase 4 — Cleanup and Hardening

### 4.1 Remove dead code
- Delete unused fields/methods tied to retired custom sync loops.

### 4.2 Keep focused telemetry
- Prefer FishNet/LiteNetLib network stats for transport health.
- Keep gameplay diagnostics only where useful.

### 4.3 Regression pass
- Full test matrix pass.
- Quick code review focused on ownership and authority boundaries.

**Exit criteria**
- No known regressions.
- Reduced complexity and clearer authority model.

---

## Suggested Feature Flags
Use booleans (or scriptable config) to migrate safely:
- `UseFishNetPredictionPipeline`
- `UseLegacyAuthoritativeBroadcast`
- `UseLegacyInputObserverBroadcast`
- `UseLegacyFrameValidation`

Start with legacy enabled, then flip one flag at a time per phase.

---

## Verification Checklist (Run each PR)
- [ ] Owner can move/aim immediately after spawn.
- [ ] Non-owner movement appears smooth.
- [ ] Unauthorized input is rejected.
- [ ] Camera and listener ownership are always correct.
- [ ] Late-join state converges quickly.
- [ ] No player-owned EventSystem remains.
- [ ] Correction count and bandwidth are not worse than baseline.

---

## Rollback Plan
If a phase regresses gameplay/network behavior:
1. Re-enable legacy flag(s).
2. Keep instrumentation on.
3. Reproduce with minimal test matrix.
4. Fix and retry phase in smaller increments.

---

## Work Order Recommendation
1. Phase 0 (baseline)
2. Phase 1 (low-risk cleanup)
3. Stabilize and playtest checkpoint
4. Phase 2 + Phase 3 in feature branch
5. Phase 4 cleanup
6. Final QA pass and merge
