# Input runner: first implementation

Status: **offline compiler and input-fidelity verifier**. This version does not
drive the game. There is no live execute command and no claim of native parity.
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
