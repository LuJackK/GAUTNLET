# Input runner: first implementation

Status: **offline compiler and verifier, plus a user-command runner in the native
plugin that has not yet run against the game** (see the last section). No claim
of native parity exists until the acceptance steps there are done.
See [the goals and capture specification](AUTOMATED_TESTING_SPEC.md) and
[the native seam audit](INPUT_SEAM_AUDIT.md) for the next integration gate.

Run these commands from `Research/DeadlockMovement`:

```powershell
python input_runner.py validate examples/runner_jump.json
python input_runner.py compile examples/runner_jump.json --output local/requested-jump-01.json
python input_runner.py compile examples/runner_walk_release.json --output local/requested-walk-01.json
```

Outputs refuse overwrite. Examples use an intended Mina/Superior Stamina
configuration; that declaration is not evidence the actual pawn has those items.
Initial-state requirements and abort policy are exported for the future executor
to enforce. The compiler does not observe the game or execute assertions.

`input_runner.py sweep --help` describes shifting selected event indices together
to produce bounded timing sweeps. Keep a press and its release together when
varying delay without changing pulse duration. Logical axes are normalized,
positive forward and positive right. The native mapping must be calibrated.

## Independent input verification

`input_runner_verify.py` compares the compiled request with movement samples.
Supply a binding file with these exact keys:

- `start_tick`: actual execution arm tick, not the chat marker's tick.
- `world`, `pawn_handle`: the armed target's observed identity.
- `button_masks`: calibrated integer masks for `jump`, `dash`, `crouch`.
- `strafe_to_left`: calibrated mapping from positive right to native left, +1 or -1.

Do not guess masks. The current human jump trace does not match the SDK's generic
bit-1 jump constant; see the audit. The verifier deliberately has no default map.

```powershell
python input_runner_verify.py local/requested-jump-01.json local/captures/RUN/trace.jsonl --binding local/arm-binding.json --output local/input-check-01.json
```

It checks each target callback at every requested tick, including multiple
callbacks in one tick, and refuses gaps, mismatches, nonfinite input or missing
target observations. It checks the controlled held masks, axes and view angles;
changed/scroll semantics still require native calibration. A final neutral input
must be observed. The report explicitly leaves action success, initial conditions
and native compatibility unassessed. Run existing full-stream validators too.

## Next implementation gate

Add read-only phase instrumentation before selecting an execution adapter. Record
the sequence of native command receipt, ability input consumption and movement,
including raw inputs and identity at each available phase. Demonstrate that the
adapter can request an ordinary jump and dash before the relevant consumer, and
that its release reaches all consumers. Do not install a second exclusive
movement hook alongside the recorder or switch to a motion replacement handler.

Native console/usercommand and bot replay facilities found in the pinned dump
are investigation leads, not verified APIs. If an adapter relies on development
commands, resolve and test their availability on this exact build before use.
An input probe must not write origin, velocity or ground state while executing.

Acceptance order: no-input -> walk/release -> jump -> dash -> abort/release ->
resource rejection -> repeated trials -> human parity -> parameter sweeps.
Geometry normals and wall eligibility are a later recorder extension, required
before explaining corner/edge boosts from collision rules.

## Native user-command runner (written 21 October 2026, not yet run live)

The plugin can execute a compiled schedule by rewriting the target player's
**user commands** in the server's `ProcessUsercmds` ingress, before AbilityThink
or ProcessMovement see them, so abilities and movement read the same input.
Nothing here has been built against, or run on, the game yet; the build,
synthetic check and smoke capture are still to do.

- `usercmd_hook.h`: inline detour of `ProcessUsercmds`. The pattern
  (`48 8B C4 44 88 48 ?? 44 89 40 ?? 48 89 50 ?? 53`) must match exactly once and
  the 15 stolen prologue bytes must equal the bytes seen in the installed
  `server.dll`, otherwise nothing is patched and the run fails.
- `usercmd_runner.h`: protobuf wire editing of `CCitadelUserCmdPB` (base = 1;
  buttons = 3, view angles = 4, forward/left/up = 5/6/7, subtick moves = 18,
  field numbers from the pinned Deadworks protos), the schedule parser and the
  per-command state machine. One schedule tick is consumed per received command
  of the target slot, so it is command-exact; the engine clock tick is logged
  beside it. Unknown fields are preserved. Subtick moves are dropped because they
  carry the human's edges. `changed` is derived as `held ^ previous applied held`,
  matching the human captures (held/changed/scroll = `buttons[0..2]`).
- `observer_plugin.cc`: `--input-schedule FILE --runner-mode apply|observe`.
  `observe` runs the identical state machine and logging but writes nothing:
  use it first to confirm the command layout and slot detection. `apply` also
  re-serializes the engine message after the rewrite and logs it as `readback`.
  The recorder's movement observer runs alongside, unchanged.
- Optional exact start state per trial: `trial ... start x y z vx vy vz` is applied
  with Modlock `ResolvePawnMotion` before the trial's first command (apply mode).
- Output `runner.jsonl`: `runner_session`, `runner_armed`, `trial_start`, `cmd`
  (observed, applied, readback), `trial_end`, `runner_done`, `runner_error`.
- Runs are labelled in `manifest.json`: `scripted: true`,
  `input_source: "scripted_usercmd"`, `runner_mode`, and the hash of the
  `input-schedule.grs` copy kept in the run directory. Ordinary runs say
  `scripted: false`, `input_source: "human"`.

Bits (from human captures; `buttons[1]` holds press/release edges):
forward 8, back 16, left 512, right 1024, crouch 4, dash `1<<44`, jump
`72339069014638592` (`1<<48 | 1<<56`). Native names in the pinned dump:
`IN_DUCK` 2, `IN_FORWARD` 3, `IN_INNATE_1` 44, `IN_MANTLE` 48, `IN_ZIPLINE` 56.
Axes are +1 forward and +1 *left*, so protocol strafe right is a negative left axis.

```powershell
python input_runner.py compile examples/parity_ground_dash.json --output local/req-ground-dash.json
python runner_schedule.py convert local/req-ground-dash.json --output local/ground-dash.grs
python capture_session.py run --sdk-bin local/capture-sdk/bin --run scripted-dash-01 --game-dir "<Deadlock>" --input-schedule local/ground-dash.grs --runner-mode observe
# join as the target slot, stand still, then from a second terminal:
python capture_session.py runner-start --run scripted-dash-01
python runner_schedule.py check local/ground-dash.grs local/captures/scripted-dash-01/runner.jsonl
```

Parity (scripted versus human, per movement step around the action press):

```powershell
python runner_parity.py --human-trace local/captures/human-tests-20261020-01/trace.jsonl --human-tick 48250 --action dash --scripted-trace local/captures/RUN/trace.jsonl --runner-log local/captures/RUN/runner.jsonl
```

Human references: ground dash `-01` tick 48250 (`parity_ground_dash`), dash-slide
`-01` dash 67255 / crouch 67282 (`parity_dash_slide`, crouch 27 ticks after the dash;
`-04` shows 25, 9697 / 9722; adjust the example for a 21-tick variant), ground jump
`-01` tick 104943 and air jump 104983 (`parity_ground_jump`, `parity_air_jump`, 40
ticks apart). The example protocols copy each reference's ticks between forward
press, dash, release and crouch, and its view angles.

Demo sweep, dash-jump at 15..40 ticks into a dash, 26 trials in one run:

```powershell
python input_runner.py sweep examples/runner_dash_jump.json --event-indices 3,4 --offset-ticks 11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,32,33,34,35,36 --output local/sweep-dashjump.json
python runner_schedule.py convert local/sweep-dashjump.json --output local/sweep-dashjump.grs --start-state X,Y,Z,0,0,0
python runner_sweep.py --trace local/captures/RUN/trace.jsonl --runner-log local/captures/RUN/runner.jsonl --sweep local/sweep-dashjump.json
```

Each trial is followed by 640 neutral ticks (10 s) so stamina recovers; raise
`--rest-ticks` if the trace shows partial stamina. The human reference
(`human-tests-20261020-01`, T07) had dash-jumps at 20-21 ticks and refusals at 9-11
and 34-37 ticks (ordinary jumps from 41), so the sweep should bracket the window between those.

Known unverified assumptions, to settle with `--runner-mode observe` first:
the `0xA8` command stride and embedded message at `+0x10`, the controller to slot
mapping (`m_pEntity +0x10`, `m_EHandle +0x10`), and that Modlock's protobuf is
ABI-compatible with the engine's for `ByteSizeLong`/`SerializeToArray`/
`ParseFromArray` on the engine-owned message. A wrong assumption should show as a
`runner_error` or a crash of the throwaway test server, not as silent input.
