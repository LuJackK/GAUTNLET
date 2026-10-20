# Autonomous capture pilot — 17 October 2026

Three real local-server bot recordings completed and passed transport validation.
They contain 3,872 movement callback samples with zero reported losses. These are
native game observations, not synthetic fixtures. They establish that the capture
hook, writer and automatic shutdown work on the installed build; they do not
establish complete input coverage or correct movement equations.

## Saved runs

All paths below are relative to `Research/DeadlockMovement`. Each run preserves
the exact source/runtime/game hashes in its manifest, trace, validation report
and host logs. The source changed between some runs; compare their manifests.

| Run under `local/captures/` | Samples | Callback clock span | Observed result |
| --- | ---: | ---: | --- |
| `solo-probe-02` | 1,291 | 20.15625 s | One small initial settling update, then stationary |
| `solo-probe-03` | 1,291 | 20.15625 s | Bot traversed the map, then stopped |
| `solo-drop-01` | 1,290 | 20.140625 s | Bot traversed the map; one logged raise/zero-velocity intervention |

All three had exactly 0.015625 seconds between adjacent callback clock samples
(64 observations per engine-clock second), one observed pawn handle per run,
and zero reported drops. Duration is the first-to-last callback clock span,
not a proven per-command integration interval. Shutdown is polled every 250 ms,
so a nominal 20-second probe can run slightly longer.

The two earlier failed attempts remain preserved: `solo-smoke-01` had no player,
hibernated and required termination; `solo-probe-01` shut down normally but
created no usable movement samples because bot creation was too early.

## What we learned

The empty dedicated server hibernates and then does not service the frame-based
stop request. Explicitly disabling empty-server hibernation restored frame
callbacks and normal shutdown. Bot creation must wait until the first simulated
frame rather than the world-load notification.

The host supplies an empty generic tick context. The recorder now resolves pawn
identity directly through Modlock's shared connection tracker and PawnObserver.
In `solo-drop-01`, the log records the pawn's hero ID changing from 0 to 7 after
asynchronous initialization; the requested hero was Wraith. Hero-ID changes now
produce a fresh identity record even when the pawn handle is unchanged.

The stationary run is a useful basic sanity check: its final position remains
`[0, 0, 1535.84375]` and velocity is zero after the first sample. It is not a
free-fall capture: the first observed position was already near the surface,
below the originally requested spawn position `[0, 0, 2048]`.

The repeat did **not** reproduce stationary behavior. The bot traversed the map
after initialization. In the moving runs, all captured axes and button masks
were zero despite substantial motion. In 611 adjacent pairs in `solo-probe-03`
and 626 in `solo-drop-01`, at least one component of the next before-velocity
differed from the previous after-velocity by more than 1 raw unit. This indicates
that the observed call boundary alone does not account for the whole motion
history; bot/ability updates or an observation/layout problem must be separated.
The traces alone do not identify which explanation is responsible.

The drop intervention was executed once in `solo-drop-01` after hero readiness:
it requested position approximately `[-1280, -4966.78, 1520.66]`, 256 raw units
above the observed position, and zero velocity. Other bot motion continued, so
this run must not be used as an isolated gravity measurement. Intervention log
coordinates use console formatting and have less precision than trace values.

No gravity, friction, acceleration, jump strength, or unit conversion is claimed
from these runs. Passing validation checks format, ordering and completeness;
it does not prove native-field semantics.

## Next experiment

Use a human-controlled local pawn on a known flat surface: stand, walk straight,
release, then jump. Check that input fields respond to actual controls and that
the observed before/after motion agrees with video. Alternatively, first disable
and verify all bot AI/ability motion before attempting another autonomous drop.
Keep hero initialization, teleports, respawns and traversal abilities out of the
fitting interval. Resolve the between-call state changes before treating this
hook as the complete integration boundary.

The raw summary is saved at `local/captures/solo-pilot-analysis.json`. Native
tests and all 59 Python tests passed after the changes. The host is stopped;
there is no background recording left running.
