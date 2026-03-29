# Server-Client Refactor Plan (FishNet)

## Objective
Move to a clear server-authoritative model with client prediction + reconciliation for smooth high-speed movement, while preserving current gameplay feel.

## Status Update (2026-03-24)
- ✅ Legacy network telemetry/logging cleanup started.
- ✅ Removed old sync telemetry fields and log spam from movement sync path.
- ✅ Removed old rollback telemetry toggles and periodic correction logs.
- ✅ Phase 1.1 pass: melee lunge direction now uses input-derived `MoveData.viewAngles`.
- ✅ Phase 1.2 pass: rollback/reconcile replays now run without external hit side effects.
- ✅ Phase 1.3 pass: networked mode no longer applies `OnCollisionStay` velocity mutations.
- ✅ Phase 2.1 pass: owner reconcile now uses nudge/snap bands with soft blend before replay.
- ✅ Phase 2.2 pass: far-ahead remote input no longer dropped; rollback uses bounded catch-up.
- ✅ Phase 2.3 pass: input redundancy added (resend recent input frames with server-side dedupe).
- ✅ Phase 3.1 kickoff: `UseFishNetPredictionPipeline` feature flag + non-breaking routing scaffold added.
- ✅ Phase 3.2 pass: FishNet prediction replicate/reconcile contracts + runtime hooks added behind feature flag.
- ✅ Phase 3.3 pass: prediction pipeline is now default and legacy broadcast/input validation flags auto-disable when enabled.
- ✅ Phase 4.1 pass: edge-action reliability hardened (consume-once frame gating + predicted `justPressed` suppression).
- ✅ Phase 4.2 pass: owner burst reconcile deferral now has bounded skip debt (prevents indefinite defer).
- ✅ Phase 4.3 pass: look authority unified to `MoveData.viewAngles` for state generation, with client proxy look smoothing as presentation writer.

## Current State (Good News)
This project already behaves mostly server-authoritative:
- Client sends input to server (`SendInputServerRpc`)
- Server simulates and broadcasts state (`ObserversRpc`)
- Clients reconcile/interpolate

So this is a refactor and hardening effort, not a full rewrite.

---

## Target Architecture

### Authority
- **Server**: source of truth for movement, combat outcomes, stamina, dash/melee state.
- **Owning client**: predicts locally for responsiveness.
- **Non-owners**: render interpolated authoritative snapshots.

### Data Flow per Tick
1. Owner samples input once per tick and sends input packet to server.
2. Owner immediately predicts movement locally.
3. Server validates input, simulates authoritative tick, stores history.
4. Server sends reconcile snapshot to owner and snapshots to observers.
5. Owner rewinds/replays only when correction threshold exceeded.
6. Observers interpolate visual state with bounded error correction.

---

## Phase Plan

## Phase 0 — Baseline + Safety Nets (1-2 days)
- Freeze current behavior metrics:
  - correction count/tick
  - max correction distance
  - rollback count
  - packet loss/reorder impact (simulated)
- Add log counters for:
  - dropped inputs
  - duplicate inputs
  - too-old inputs
  - burst-skip reconciles

### Exit Criteria
- Baseline data saved from Host+1 and Host+2 test runs.

---

## Phase 1 — Determinism Hardening (3-5 days)

### 1.1 Remove non-deterministic simulation reads
- In simulation path, use only replicated/input-derived values from `MoveData`.
- Avoid transform-driven reads in tick simulation.

### 1.2 Separate gameplay side effects from rollback sim
- Keep simulation state deterministic.
- Fire VFX/audio/damage from authoritative events (idempotent event IDs), not inside replay loops.

### 1.3 Remove out-of-band state mutation
- Prevent collision/event callbacks from mutating movement state outside tick authority path.

### Exit Criteria
- Same input sequence produces matching state hash on server vs owner replay over 300+ ticks.

---

## Phase 2 — Prediction/Reconcile Cleanup (4-6 days)

### 2.1 Unify one correction policy
- Owner: deadzone + soft correction for small errors, hard snap for severe divergence.
- Proxy: interpolation buffer + velocity blending, snap only above high threshold.

### 2.2 Improve rollback input timeline
- Replace "drop when too far ahead" with bounded catch-up policy.
- Keep small jitter window for out-of-order packets.

### 2.3 Input redundancy
- Send current input + last N inputs (e.g., 2-4) to reduce packet-loss spikes.

### Exit Criteria
- Under 100ms RTT / 2% packet loss, movement remains responsive with no frequent visible snaps.

---

## Phase 3 — FishNet-Native Pipeline Migration (optional but recommended, 4-7 days)

### 3.1 Introduce feature flag
- `UseFishNetPredictionPipeline` (off by default).

### 3.2 Port movement replication/reconcile to FishNet prediction hooks
- Keep current path behind legacy flags.
- A/B compare latency feel, correction frequency, and bandwidth.

### 3.3 Decommission redundant custom legacy paths
- Remove duplicate observer broadcast/correction logic after parity is proven.

### Exit Criteria
- FishNet path equals or beats legacy feel and correction metrics.

---

## Phase 4 — Combat/High-Action Feel Tuning (3-5 days)

### 4.1 Edge-action reliability
- Confirm `justPressed` actions (dash/jump/melee) are consume-once and replay-safe.

### 4.2 Burst movement protection
- Keep burst skip logic but add timeout and bounded reconcile debt.

### 4.3 Camera/aim smoothing consistency
- Ensure one writer for authoritative look; smoothing only in presentation layer.

### Exit Criteria
- Dash/melee/jump chains remain responsive during packet jitter and reconciles.

---

## Key Files to Refactor First
- `Assets/Scripts/SourcePhysics/Movement/NetworkedCharacter.cs`
- `Assets/Scripts/SourcePhysics/Movement/RollbackManager.cs`
- `Assets/Scripts/SourcePhysics/Movement/SurfCharacter.cs`
- `Assets/Scripts/SourcePhysics/Movement/LocalInputCollector.cs`
- `Assets/Scripts/SourcePhysics/Movement/InputFrame.cs`

---

## Testing Matrix (Required Each Phase)
- Host + 1 client (LAN)
- Host + 2 clients
- 80-120ms RTT simulation
- 1-3% packet loss simulation
- late join during active combat
- 10-minute endurance with repeated dashes/slides/melee

Metrics to track:
- average correction distance
- corrections per minute
- rollback count
- dropped input count
- visible snap count (manual tally)

---

## Rollout Strategy
- Keep legacy flags enabled by default.
- Merge each phase in small PRs.
- If regression occurs: toggle legacy path back on, preserve instrumentation, and isolate by phase.

---

## Recommended First Sprint (Concrete)
1. Add baseline counters and capture logs.
2. Remove transform-dependent sim reads.
3. Make hit/damage events replay-safe and idempotent.
4. Add owner correction deadzone + proxy snap threshold tuning.
5. Playtest under injected latency/loss and record metrics.

This gives the biggest smoothness gain with the least risk.