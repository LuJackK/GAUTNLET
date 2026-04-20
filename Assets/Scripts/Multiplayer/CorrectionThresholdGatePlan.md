# Correction Threshold Gate Plan

## Goal

Reduce unnecessary authoritative corrections for the locally owned player by introducing a divergence gate between incoming authoritative reconcile state and the locally predicted state for the same frame.

The current flow always applies authoritative reconcile state when it arrives. That is simple and safe, but it is also aggressive:

- tiny float drift can cause repeated corrections
- harmless timer differences can trigger full correction indirectly
- the local owner can feel over-corrected even when the simulation is effectively close enough

This plan proposes a threshold-based correction gate that decides when a reconcile packet should:

- be ignored
- be counted as drift for diagnostics only
- trigger a hard correction and rollback replay
- trigger a forced correction immediately for critical state mismatches

## Current Architecture Summary

The current owner path already resembles a form of state synchronization:

- owner sends input through FishNet `Replicate`
- server simulates from input
- server sends authoritative state back through FishNet `Reconcile`
- client reconstructs authoritative `MoveData`
- rollback manager applies the authoritative correction immediately

This means we do **not** need to invent a new synchronization architecture to solve the immediate issue. The right intervention point is the reconcile application path:

- build authoritative state
- compare it against predicted state at the same frame
- decide whether correction is necessary
- only then apply rollback correction

## High-Level Design

### New Decision Step

Insert a divergence-evaluation step before calling rollback correction.

Proposed flow:

1. Receive authoritative `NetworkedCharacterReconcileData`.
2. Reconstruct authoritative `MoveData`.
3. Query `RollbackManager` for predicted state at `reconcileData.Frame`.
4. Compare predicted vs authoritative state field-by-field.
5. Build a divergence report and correction decision.
6. Only call `ApplyAuthoritativeCorrection(...)` if the decision says correction is required.

### Decision Outcomes

Suggested initial decision enum:

```csharp
internal enum CorrectionDecision {
    Ignore,
    ObserveOnly,
    HardCorrect,
    ForceCorrect
}
```

Meaning:

- `Ignore`: state is close enough; do nothing
- `ObserveOnly`: not corrected yet, but track this drift for diagnostics and consecutive-frame escalation
- `HardCorrect`: apply authoritative correction and replay
- `ForceCorrect`: immediate correction because the mismatch is discrete, dangerous, or likely to cascade

## Configuration Strategy

### Short-Term

Keep thresholds in code for the first implementation. This keeps the first pass simple, reviewable, and deterministic.

Recommended shape:

```csharp
internal sealed class CorrectionGateConfig {
    public float PositionHardThreshold = 0.05f;
    public float VelocityHardThreshold = 0.20f;
    public float AngleHardThreshold = 1.5f;
    public float ScalarTolerance = 0.05f;
    public float TimerTolerance = 0.05f;
    public int ConsecutiveObserveFramesBeforeHardCorrect = 3;
    public float WeightedScoreHardThreshold = 10f;
}
```

This can initially live in code near the reconcile service or in a small dedicated file in the multiplayer/player folder.

### Medium-Term

Move the thresholds into a serialized configuration surface suitable for tuning in editor.

Possible next step options:

- serialized config class on `NetworkedCharacter`
- `ScriptableObject` policy asset
- multiplayer tuning profile exposed through your future API/editor pipeline

Recommendation:

- first implementation: plain code config
- second implementation: serialized inspector-facing config
- third implementation: project-wide policy assets and tooling

## Proposed Types

Suggested new types:

### `CorrectionGateConfig`

Holds thresholds, score weights, and escalation settings.

### `StateDivergenceReport`

Contains the result of comparing predicted and authoritative states.

Suggested contents:

```csharp
internal sealed class StateDivergenceReport {
    public bool HasPredictedState;
    public CorrectionDecision Decision;
    public float WeightedScore;
    public bool HasFatalMismatch;
    public bool HasKinematicMismatch;
    public string PrimaryReason;
    public string Summary;
}
```

### `StateDivergenceEvaluator`

Pure comparison logic:

- takes predicted `MoveData`
- takes authoritative `MoveData`
- applies field-level policy
- returns `StateDivergenceReport`

This should stay as stateless and testable as possible.

## Field Policy

This section is the most important part of the plan. It describes which fields should be strict and why.

### Bucket 1: Force-Correct Fields

These fields should trigger immediate correction when mismatched because they represent branch-driving simulation truth or combat truth.

Fields:

- `frame`
- `moveType`
- `meleeState`
- `isDashing`
- `canAirDash`
- `jumpCount`
- `grounded`
- `jumping`
- `sliding`
- `wasSliding`
- `hasHitTarget`
- `meleeHitResolved`
- `meleeHitTargetObjectId`
- `meleeHitResolveTick`
- `lastConsumedJumpPressFrame`
- `lastConsumedDashPressFrame`

Why:

- these values change how future simulation branches
- if they differ, further replay will often diverge more instead of less
- some of them affect combat outcomes and cannot be treated loosely

Initial decision:

- any mismatch in this bucket => `ForceCorrect`

### Bucket 2: Hard-Correct Kinematic Fields

These fields are not necessarily discrete branch flags, but visible or physical divergence here strongly affects gameplay feel and future collision results.

Fields:

- `origin`
- `velocity`
- `viewAngles.y` / yaw
- `viewAngles.x` / pitch
- `fallingVelocity`
- `slideDirection`
- `slideSpeedCurrent`

Recommended initial thresholds:

- position distance: `0.05m`
- velocity distance: `0.20 m/s`
- yaw delta: `1.5 degrees`
- pitch delta: `1.5 degrees`
- falling velocity delta: `0.20`
- slide direction angular delta: `5 degrees`
- slide speed delta: `0.20`

Initial decision:

- if any value exceeds its hard threshold => `HardCorrect`

### Bucket 3: Weighted Medium-Importance Fields

These matter, but small differences are often harmless in isolation.

Fields:

- `stamina`
- `staminaRegenTimer`
- `dashTimer`
- `currentDashDuration`
- `dashCooldownTimer`
- `jumpTimer`
- `surfaceFriction`
- `gravityFactor`
- `walkFactor`
- `crouching`
- `crouchLerp`
- `renderCrouchLerp`
- `uncrouchDown`
- `slideDelay`

Why:

- these are meaningful simulation values
- they can contribute to future drift
- they are often noisy enough that tiny differences should not cause immediate hard correction

Recommended initial thresholds:

- scalar gameplay values: `0.05`
- timers: `0.05 seconds`
- lerps: `0.08`
- factors like gravity/walk/friction: `0.03 to 0.05`

Initial decision:

- do not hard-correct from one mismatch alone
- contribute weighted points into a divergence score
- escalate to `HardCorrect` only if score is high enough or persists across multiple frames

### Bucket 4: Diagnostics-Only or Low-Weight Fields

These are useful for debugging drift but should not usually drive correction on their own.

Fields:

- `forwardMove`
- `sideMove`
- `verticalAxis`
- `horizontalAxis`

Why:

- these are closer to echoed input than authoritative outcome
- if the resulting kinematic and discrete simulation state still agrees, correcting from these values is usually not worth it

Initial decision:

- diagnostics only, or extremely low score weight

### Special Note on Crouch Fields

`crouching`, `crouchLerp`, `renderCrouchLerp`, and `uncrouchDown` need care.

Why:

- crouch can affect collider size, movement branch selection, and visuals
- your code already treats crouch specially for foreign simulation, which suggests this area is sensitive

Recommendation:

- treat `crouching` as medium importance initially, not force-correct
- treat crouch lerp values as low or medium importance
- revisit after live testing to see whether crouch divergence creates collision issues

If crouch affects gameplay-critical collision in owner simulation more than expected, promote `crouching` to force-correct later.

## Escalation Rules

The gate should not rely only on single-frame thresholds. Add escalation for persistent drift.

### Rule 1: Immediate Force Correction

If any force-correct field differs, correct immediately.

### Rule 2: Immediate Hard Correction

If any hard kinematic threshold is exceeded, correct immediately.

### Rule 3: Weighted Persistence Escalation

For medium-importance state:

- accumulate a weighted score from mismatches
- if the score exceeds a threshold, mark `ObserveOnly`
- if this repeats for N consecutive reconciles, escalate to `HardCorrect`

Initial recommendation:

- `ObserveOnly` at weighted score `>= 4`
- `HardCorrect` at weighted score `>= 10`
- or `HardCorrect` after `3` consecutive `ObserveOnly` frames

### Rule 4: Missing Predicted State

If predicted state for that authoritative frame is missing, prefer the safe path.

Initial recommendation:

- if predicted state is unavailable, apply correction

This can later be revisited if missing history becomes common and diagnostics show it is safe to do otherwise.

## Diagnostics and Tuning

The first implementation should be instrumentation-heavy.

Add counters and logs for:

- reconcile packets received
- reconcile packets ignored
- observe-only decisions
- hard corrections
- force corrections
- primary field/reason for each correction
- number of consecutive observe frames before escalation

Recommended HUD/debug additions:

- last decision
- last primary reason
- current consecutive drift count
- weighted score
- top mismatched fields

This is important because threshold tuning without visibility will be frustrating.

## Testing Strategy

### Unit / Pure Comparison Tests

Add tests around the divergence evaluator:

- strict field mismatch should `ForceCorrect`
- position over threshold should `HardCorrect`
- tiny timer mismatch should `Ignore` or `ObserveOnly`
- repeated medium drift should escalate
- diagnostics-only fields should not force correction

### Replay Harness / Regression Tests

Use the existing replay harness where possible to validate:

- correction count goes down when drift is tiny
- significant movement divergence still corrects quickly
- melee and dash branch mismatches still correct immediately

### Manual Multiplayer Validation

Manual cases to validate:

- join in progress and remain idle
- walk normally with stable ping
- repeated jumping and landing transitions
- dash chains
- slide entry/exit
- melee hit resolution
- induced packet loss / latency / jitter if available

Success criteria:

- fewer corrections while standing still or moving normally
- no visible accumulation of serious drift
- no delayed correction of gameplay-critical state
- no noticeable increase in desync bugs

## Scope Recommendation on Smoothing

Smoothing should **not** be part of the first implementation of the correction threshold gate.

Reason:

- the immediate problem is over-eager correction, not lack of visual smoothing
- if we add smoothing at the same time, it becomes harder to tell whether a bad result is caused by correction policy or presentation blending
- smoothing is a separate concern with separate tuning and failure modes

Recommendation:

- this task: build the correction gate only
- future task: consider visual-only smoothing for accepted corrections that still produce noticeable pops

Important constraint:

- do not smooth the simulation state itself
- if smoothing is later added, it should be visual/presentation-level error smoothing only

## Phased Implementation Plan

### Phase 1: Infrastructure

- add `CorrectionGateConfig`
- add `CorrectionDecision`
- add `StateDivergenceReport`
- add `StateDivergenceEvaluator`
- wire evaluation into reconcile path before correction

Deliverable:

- corrections only apply when the gate allows them

### Phase 2: Diagnostics

- add counters and reason strings
- expose gate behavior in existing debug tools/HUD
- add temporary verbose logging if needed

Deliverable:

- developers can see why corrections are or are not happening

### Phase 3: Threshold Tuning

- tune thresholds during real multiplayer sessions
- adjust bucket membership for crouch and other sensitive fields
- refine score weights and escalation counts

Deliverable:

- stable baseline policy with reduced unnecessary owner corrections

### Phase 4: Editor/API Exposure

- move policy into serialized config
- expose inspector tuning fields
- optionally support policy presets

Deliverable:

- designers/programmers can tune without code edits

### Phase 5: Optional Visual Smoothing Follow-Up

- only after threshold behavior is trustworthy
- apply visual error offsets for render presentation if needed
- keep simulation authoritative and unsmoothed

Deliverable:

- reduced visible pops without corrupting simulation state

## Recommended First-Pass Defaults

These are intentionally conservative starting values.

- position hard threshold: `0.05m`
- velocity hard threshold: `0.20 m/s`
- yaw hard threshold: `1.5 deg`
- pitch hard threshold: `1.5 deg`
- timer tolerance: `0.05s`
- generic scalar tolerance: `0.05`
- consecutive observe frames before hard correction: `3`
- hard weighted score threshold: `10`

These values should be treated as initial tuning values, not final truth.

## Implementation Notes for the Agent

- keep the comparison logic as pure as possible
- avoid embedding threshold math directly inside the reconcile method
- make field-specific reasons visible in diagnostics
- prefer correctness and observability over cleverness
- resist adding smoothing in the same task unless absolutely required

## Suggested Agent Task Prompt

```text
Implement a threshold-based authoritative correction gate for the owner prediction/reconcile flow in the multiplayer player code.

Goals:
- Compare the locally predicted MoveData against the incoming authoritative reconcile MoveData for the same frame before applying rollback correction.
- Only apply correction when divergence crosses configured thresholds.
- Immediately force correction for discrete gameplay-critical state mismatches.
- Use weighted or persistent drift escalation for medium-importance scalar mismatches.
- Keep thresholds/configuration in code for now, but structure it so we can expose it in the editor/API later.
- Add diagnostics so we can see why a correction was ignored, observed, hard-corrected, or force-corrected.

Implementation guidance:
- Add dedicated types for config, divergence report, and divergence evaluator.
- Keep the evaluator mostly pure/testable.
- Wire it into the existing reconcile path with minimal disruption.
- Do not add simulation-level smoothing in this task.
- If you add any smoothing-related hooks, keep them clearly separated and disabled by default.

Field policy:
- Force-correct on mismatches in moveType, meleeState, isDashing, canAirDash, jumpCount, grounded, jumping, sliding, wasSliding, hasHitTarget, meleeHitResolved, meleeHitTargetObjectId, meleeHitResolveTick, lastConsumedJumpPressFrame, and lastConsumedDashPressFrame.
- Hard-correct when position, velocity, yaw, pitch, fallingVelocity, slideDirection, or slideSpeedCurrent exceed thresholds.
- Treat stamina/timers/factors/crouch state as medium-importance and use weighted score plus consecutive-frame escalation.
- Treat forwardMove/sideMove/verticalAxis/horizontalAxis as diagnostics-only or very low weight.

Also:
- update or extend debug output so the latest correction decision and primary reason are visible
- add tests for the evaluator if there is an obvious place in the existing test setup
- keep the initial thresholds conservative and easy to tweak
```
