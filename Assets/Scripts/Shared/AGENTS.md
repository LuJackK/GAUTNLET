# Shared Scripts Orientation

This folder contains small cross-cutting helpers used by gameplay code elsewhere in the project. It is not a feature area by itself; treat it as shared utility surface and keep changes narrow.

## Contents

- `Extensions/IntExtensions.cs` adds bit-flag helpers for `int` values:
  - `HasFlag(a, b)` checks whether all bits in `b` are present in `a`.
  - `AddFlag(a, b)` returns `a` with bits from `b` set.
  - `RemoveFlag(a, b)` returns `a` with bits from `b` cleared.
- `Extensions/VectorExtensions.cs` contains `VectorMa(start, scale, direction)`, a Quake-style vector multiply-add helper equivalent to `start + direction * scale`.
- `Physics/Trace.cs` defines `Fragsurf.TraceUtil.Trace`, the lightweight result struct returned by tracing helpers.
- `Physics/Tracer.cs` defines `Fragsurf.TraceUtil.Tracer`, static helpers for swept collider checks using Unity physics casts.

## Physics Trace Flow

`Tracer.TraceCollider` is the main entry point when the caller has a Unity `Collider`. It dispatches by collider type:

- `BoxCollider` -> `TraceBox(...)`, using `collider.bounds.extents`.
- `CapsuleCollider` -> gets capsule endpoints through `Movement.SurfPhysics.GetCapsulePoints(...)`, then calls `TraceCapsule(...)`.
- Any other collider type throws `NotImplementedException`.

Both `TraceBox` and `TraceCapsule`:

- create a `Trace` with `startPos` and `endPos`;
- calculate sweep direction and max distance from `start` to destination, with a small contact-offset allowance;
- perform a Unity `Physics.BoxCast` or `Physics.CapsuleCast`;
- ignore triggers via `QueryTriggerInteraction.Ignore`;
- set `fraction = 1` when nothing is hit;
- on hit, fill `fraction`, `hitCollider`, `hitPoint`, `planeNormal`, and `distance`;
- do a tiny follow-up collider raycast near the hit point to refine the surface normal.

## Dependencies And Gotchas

- `Fragsurf.TraceUtil.Tracer` depends on Unity physics APIs and on `Movement.SurfPhysics.GetCapsulePoints`, which is outside this folder.
- `Trace.startSolid` exists on the result struct but is not currently populated by these helpers.
- `TraceCollider` only supports `BoxCollider` and `CapsuleCollider`; add support deliberately if a new controller collider type is introduced.
- `TraceBox` uses `Quaternion.identity`, so it assumes an axis-aligned box cast rather than preserving collider rotation.
- `colliderScale` is applied to cast extents/radius and capsule endpoint offsets, but not to every input value. Check callers before changing this math.
- The extension classes are in the global namespace, while trace code is in `Fragsurf.TraceUtil`.

## Where To Start

- For movement collision or surf-controller sweep behavior, start with `Physics/Tracer.cs`.
- For trace result consumers, inspect fields in `Physics/Trace.cs` first, then follow callers outside this folder.
- For flag or vector helper changes, keep APIs backward-compatible because these are global static helpers and may be used broadly.
