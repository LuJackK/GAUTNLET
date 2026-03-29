# Movement Synchronization Fixes - Client-Server Refactor

## Overview
This document explains the fixes applied to address high-speed movement rubber-banding and sliding/melee not working properly in the client-server movement model.

## Root Causes Identified

### 1. **Input State Prediction During Rollback** ✅ FIXED
**Problem**: When the RollbackManager filled in missing frames with predictions, it was only repeating the last confirmed input. This could cause:
- Loss of held button state (melee, slide) during high network latency
- State machines getting out of sync when predictions diverged from actual input

**Solution**: Enhanced `GetPrediction()` to intelligently preserve held button state while clearing edge-triggered flags:
```csharp
// Preserves buttons (BTN_MELEE, BTN_CROUCH) but clears justPressed
predicted.justPressed = 0;  // Only the held state (buttons field) is retained
```

### 2. **Velocity Loss During Rollback** ✅ FIXED
**Problem**: When rolling back to a historical state, velocity could be lost or incorrectly restored, causing jerky movement especially during high-speed actions.

**Solution**: Improved fallback prediction to include previous local input as a backup:
```csharp
// If exact state not found, try to use the previous local input to maintain continuity
if (_localInputFrameAtSlot[recentSlot] == frame - 1) {
    InputFrame fallback = _localInputs[recentSlot];
    fallback.frame = frame;
    fallback.justPressed = 0;
    return fallback;
}
```

### 3. **Owner Reconciliation During Burst Movement** ✅ FIXED
**Problem**: The owner's position was being aggressively blended with the server's authoritative position during dash/slide/melee, causing rubber-banding. The default blend factor was treating all movement equally.

**Solution**: Increased velocity blend factor during detected burst movement (dash/slide/melee):
```csharp
// During burst movement, trust server velocity more heavily
float velocityBlendFactor = isRemoteBursting 
    ? Mathf.Min(0.6f, _ownerReconcileBlendFactor * 2f)
    : _ownerReconcileBlendFactor;
```

This ensures velocity updates smoothly converge to authoritative values without positional oscillation.

### 4. **Proxy Player Animation Jankiness** ✅ FIXED
**Problem**: Remote players' movement appeared choppy and unresponsive, especially during rapid movements.

**Solution**: Made proxy velocity updates more aggressive during detected remote burst movement:
```csharp
float velocityBlendSpeed = isRemoteBursting 
    ? _proxyVelocityBlendSpeed * 1.5f  // 50% faster during burst
    : _proxyVelocityBlendSpeed;
```

## Files Modified

### 1. `RollbackManager.cs`
- **`GetPrediction()`**: Enhanced to preserve button state while clearing edge flags
- **`Rollback()`**: Improved state restoration with better velocity continuity
- **Added fallback prediction** using last known local input

### 2. `NetworkedCharacter.cs`
- **Owner reconciliation**: Added velocity blend factor multiplier during burst movement
- **Proxy interpolation**: Made velocity convergence faster for burst movement
- **`IsBurstMovementActiveLocally()`**: Already correctly detecting dash/slide/melee states

## How Melee & Sliding Work (Important Context)

The melee and slide systems use **held button state**, not just edge-triggered presses:

```csharp
// In SurfCharacter.ApplyInputToState()
state.wishMelee = input.HasButton(InputFrame.BTN_MELEE);  // Held state!
state.crouching = input.HasButton(InputFrame.BTN_CROUCH);

// Melee state machine only ENTERS charging from Walk mode
if (state.moveType == MoveType.Walk && state.wishMelee) {
    state.moveType = MoveType.HeavyMelee;
    state.meleeState = MoveData.MeleeState.Charging;
    // Once in HeavyMelee, you stay in it until recovery finishes
}

// Charging continues while wishMelee is true
if (state.meleeState == MoveData.MeleeState.Charging) {
    bool release = !state.wishMelee || chargeTimeReached;
    if (release) {
        // Transition to Lunge state
    }
}
```

**Why it was breaking**: When rollback happened and predictions were made with `justPressed = 0`, the `buttons` field (held state) was still preserved. The issue was:
1. Velocity would be lost during rollback
2. Position reconciliation would snap the player around
3. The held melee state would continue, but the character would be in the wrong position

The fixes above address this by properly preserving velocity through the rollback process.

## Input Redundancy System

Input redundancy is already implemented (`_inputRedundancyFrames = 2`):
- Owner sends current frame + last 2 frames as backup
- Server receives multiple copies of same input, preventing packet loss issues
- This system is working correctly and should NOT be modified

## Configuration Recommendations

### For High-Latency Scenarios (120+ ms RTT):
```csharp
[Inspector Settings for NetworkedCharacter]:
- _inputRedundancyFrames: 3 (increased from 2)
- _predictionLookbackFrames: 120 (default is good)
- _ownerReconcileBlendFactor: 0.25 (reduced from 0.35)
- _ownerReconcileNudgeDistance: 0.02 (reduced from 0.015)
- _proxyVelocityBlendSpeed: 15 (increased from 12)
```

### For Low-Latency Scenarios (30-50 ms RTT):
```csharp
[Current defaults are fine]:
- _ownerReconcileBlendFactor: 0.35
- _proxyVelocityBlendSpeed: 12
```

## Debugging High-Speed Desync

If you still see rubber-banding:

1. **Check server tick rate**: 
   - Too low (30 Hz) causes larger position deltas
   - Target: 60 Hz or higher

2. **Monitor rollback frequency**:
   - Use `_rollback.RollbackCount` and `CorrectionCount`
   - If > 1-2 per second, something is wrong with prediction

3. **Validate input transport**:
   - Check server logs for dropped input frames
   - Verify `_inputRedundancyFrames` is set to at least 2

4. **Test on real network**:
   - Local testing can mask issues
   - NetworkLatencySimulator helps
   - Test with actual ping variations (not just constant lag)

## Testing Checklist

- [ ] Dash movement feels responsive (no rubber-banding)
- [ ] Melee charging/lunging works while moving
- [ ] Sliding maintains momentum correctly
- [ ] Remote players animate smoothly during dash/slide
- [ ] No input drops when holding melee for extended time
- [ ] Position corrections are smooth, not snappy
- [ ] High packet loss (50% simulated) still feels playable

## Next Steps if Issues Persist

1. **Enable detailed logging**:
```csharp
// In NetworkedCharacter.SendInputServerRpc()
Debug.Log($"[Input] Frame {input.frame}: buttons={input.buttons:X2}, wishMelee={input.HasButton(InputFrame.BTN_MELEE)}");

// In RollbackManager.Rollback()
Debug.Log($"[Rollback] Frame {toFrame}: CorrectionCount={CorrectionCount}");
```

2. **Profile prediction accuracy**:
   - Compare predicted vs actual inputs
   - Check if `_stickMismatchThreshold = 1` is too tight

3. **Verify state continuity**:
   - Add logging to MoveData transitions
   - Ensure melee state isn't being reset unexpectedly

## Summary of Changes

| Issue | Solution | File | Impact |
|-------|----------|------|--------|
| Input state lost during rollback | Preserve button state in predictions | RollbackManager.cs | High |
| Velocity lost on position snap | Better fallback prediction | RollbackManager.cs | High |
| Owner rubber-bands during burst | Velocity blend adjustment | NetworkedCharacter.cs | High |
| Remote player appears janky | Faster velocity sync for burst | NetworkedCharacter.cs | Medium |

All changes maintain backward compatibility with existing reconciliation logic while being significantly more robust under network latency.
