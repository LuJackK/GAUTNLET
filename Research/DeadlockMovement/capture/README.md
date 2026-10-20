# Native movement capture MVP

This recorder observes a local Deadlock server through a pinned, patched Modlock.
It saves the native movement inputs and paired state immediately before and after
each movement call. The observer does not replace movement or write inputs.
The optional solo-drop experiment changes the bot's initial state once, as
described below. LockTimer is
not required: race timing is separate from measuring individual movement updates.

The first human capture is a compatibility pilot, not yet evidence of correct
units, timing semantics, or recovered movement equations. A successful build or
synthetic check cannot verify native offsets against an installed game update.

## Local verification, 17 October 2026

The Windows SDK, host and plugin built successfully with the recipe below.
Native tests passed, including paired observation, concurrent queue transfer,
overflow accounting, serialization and failed-stream handling. All 58 Python
tests passed. The actual host loaded the plugin and completed `synthetic-01`:
one synthetic sample, zero drops, valid format and complete footer. Artifacts are
under `local/captures/synthetic-01/`.

The upstream read-only signature checker resolved 61 of 61 entries against the
installed game, reporting build `6793`; output is preserved in
`local/capture-signature-check.log`. This checks address signatures, not field
layouts or timing semantics. Subsequent autonomous native bot captures are
documented in [the solo pilot report](SOLO_PILOT_REPORT.md). No human-controlled
player capture has been performed yet.

## Build on Windows x64

Run these commands from `Research/DeadlockMovement` with Python 3.10+ and Git.
Portable tools, dependencies, binaries, and recordings stay in ignored `local/`.
The build does not modify the installed game or Unity project.

```powershell
python -m pip install --target local/cmake-tools cmake==3.31.6 ninja==1.11.1.4
python -m pip install --target local/build-tools-016 ziglang==0.16.0
python capture_build.py
```

Already-installed portable tools can be reused. The build driver pins Modlock to
`67478b9bbc54ff1f94ccfdb5f9ce0e08dc6f6418`, applies `modlock-observer.patch`,
copies `native/observe_step.h` into its include tree, and pins its protobuf and
Abseil forks. Optional script runtimes are disabled. It builds the SDK, host,
plugin and native tests together using the same compiler and ABI. Diagnostics go
to `local/capture-build.log`; the runtime goes to `local/capture-sdk/bin`.

Preparation is repeatable but rejects unexpected edits in the pinned source;
it never resets them. `python capture_session.py prepare` performs preparation
alone. Do not combine an upstream release DLL with this patched plugin.
`capture-build.json` records source and runtime hashes only after a successful
build and native test. Source or binary changes require rebuilding the stamp.

## Check the installed plugin without starting the game

```powershell
python capture_session.py doctor --sdk-bin local/capture-sdk/bin
python capture_session.py run --sdk-bin local/capture-sdk/bin --run synthetic-01 --check-only
python -m unittest discover -s tests -v
```

Use a new run name each time. The host loads the real plugin and exercises its
lifecycle, writing one explicitly synthetic sample. This checks loading, file
output, shutdown, and offline validation; it never counts as a game capture.

## First human pilot

Start Steam, finish updates and launch the game **before** starting the capture
server: the host loads installed DLLs and can otherwise block Steam updates.

For a basic unattended native probe, append `--solo-probe` to the live run
command below, using a new run name. This precaches Wraith, requests one native
bot on team 2 at raw position `[0,0,2048]`, and requests shutdown after 20 seconds
from its first simulated frame. It does not prescribe movement inputs or reset
the bot's state each tick. The resulting spawn/idle/AI behavior is exploratory,
not a controlled walk or jump experiment. `capture-probe` logs the setup and the
manifest records `solo_probe: true`. No trace means the probe failed, not that
the character was stationary. Empty-server hibernation is disabled so frame
callbacks and stop requests continue to run without a connected human.

Add `--solo-drop` alongside `--solo-probe` to request a one-time 256-unit raise
and zero velocity after five seconds, once the bot has a nonzero hero ID. This
uses native origin/velocity setters on that bot only and logs a
`capture-intervention` entry. The observer remains read-only. Bot AI and ability
motion are not disabled, so this is **not a controlled gravity test**. Check the
intervention log: a requested drop may not execute if hero initialization never
finishes. Do not infer a movement constant from these traces without isolating
other movement writers first.

Finish any Steam update first. Use the actual game directory if it differs from
this example. Start a local server in one terminal:

```powershell
python capture_session.py run --sdk-bin local/capture-sdk/bin --run pilot-01 --game-dir "C:\Program Files (x86)\Steam\steamapps\common\Deadlock"
```

The default map is `dl_midtown`, port `27067`, and the server is bound to
`127.0.0.1`. The inherited host uses insecure local play. Connect the game client
to `127.0.0.1:27067` using its developer console. Client launch/join requirements
may depend on the installed build; a failed join is not a successful pilot.
Choose one hero, avoid modifiers, record the hero/map/build and test conditions,
and perform: a short stationary interval, straight run, release to stop, and one
jump. Capture about 20–30 seconds before attempting complex combinations.

From a second terminal, request a normal server shutdown:

```powershell
python capture_session.py stop --run pilot-01
```

The plugin checks the stop request on engine ticks and submits `quit`. Its hook
is removed before the writer drains and closes. Wait for the first terminal to
return. This requires a responsive engine; a crash or forced termination may
leave an incomplete trace, which must not be used as a complete trial.

Each run creates a new `local/captures/<run>/` containing:

- `manifest.json`: exact source/runtime/signature and game DLL hashes, Steam
  build ID where found, arguments, result, and explicit unverified compatibility.
- `trace.jsonl`: session header, movement samples, and reconciled end counters.
- `frames.jsonl`: separate after-frame pawn/state/resource observations and
  engine-acknowledged trial markers, with its own sequence and loss counters.
- `stdout.log` and `stderr.log`: host diagnostics and pawn identity changes.
- `validation.json`: format, completeness, losses, observed fields and clocks.
- `frame-validation.json`: frame format/completeness, marker acknowledgements
  and bounds checks against the movement trace. New runs require both reports
  to pass; old movement-only captures remain readable with `capture_trace.py`.

Read the logs even if validation passes. Match `capture-pawn` entries to the
controlled player's pawn handle and world. They contain slot, hero, serial-fenced
handle and frame observation label, not account identifiers. Identity observations occur
after a frame and are separate from movement command timestamps.

Accept the pilot only after checking that stationary/run/jump observations match
the visible actions; positions and velocities are plausible; the selected pawn
persists correctly; grounded/airborne transitions agree; and there are no dropped
samples, clock regressions, or incomplete footer. Check coordinate directions,
position/velocity units and the relation of raw ticks/fractions to displacement
before calculating acceleration. Repeat the baseline to estimate noise. Preserve
an independently recorded video if useful for checking event correspondence.

## Data contract (schema 1)

| Field | Meaning and limits |
| --- | --- |
| `sequence` | Global attempted movement callback order, starting at zero; losses leave gaps. |
| `world`, `pawn_handle` | World generation plus serial-fenced pawn handle; null handle means unavailable. |
| `before`, `after` | Native position and velocity vectors, ground entity and move type around one native call. Raw engine units; no conversion to metres. |
| `command_raw` | Raw upstream command field; on the server this may be the previous command, not current identity. |
| `ticks`, `fractions` | Raw native timing fields preserved without assuming their integration semantics. |
| `buttons` | Three raw 64-bit masks as decimal strings to preserve all bits. No action decoding is assumed. |
| `angles`, `axes` | Raw view angles and forward/left/up input values. |
| `clock_tick`, `clock_time_s` | Nullable callback engine clock observations, not proof of a per-command time interval. |
| `wall_ns` | Monotonic process clock at observation, for ordering/diagnostics; not simulation time. |

The observer calls the original movement function exactly once, reads its result,
then sends immutable snapshots to the recorder. A bounded single-producer ring
passes samples to a background writer without file I/O in the movement hook.
Full queues count losses; the validator rejects lossy runs. This assumes Modlock's
engine-thread dispatch. Nonfinite observations become JSON null and fail numeric
validation. The footer reconciles attempted, written and dropped counts.

The recorder does not yet capture collision normals, all sub-tick input
transitions, client prediction, or a validated action/event model.
It does not fit equations or plot trajectories. These follow a successful pilot
and a decision about which observed fields are trustworthy. Native game crashes
remain possible if a game update invalidates upstream layouts or signatures.

Revalidate any saved trace independently:

```powershell
python capture_trace.py validate local/captures/pilot-01/trace.jsonl
```

## Frame state and trial markers (18 October upgrade)

`frames.jsonl` is schema 1 with phase `after_game_frame`. Each pawn observation
contains world, slot, serial-fenced pawn handle, session generation, hero ID,
position, optional velocity/ground handle, grounded/sliding/dashing/climbing/
mantling flags, optional current/max stamina, and the observed ability inventory
(handles, slots, charges, cooldown and recharge timestamps). Unavailable values
are null. Slide/mantle booleans require the schema field and a unique owned
ability; modifier-state flags require schema offsets and a non-null property.
We do not export upstream's default false as proof that an unavailable state is
inactive. Modifier enum values are resolved by name from the loaded game's
schema; both ordinary and predicted masks are checked with schema bounds and
guarded reads. Missing enums, masks, pointers or failed reads produce null.

Dash-reader correction, 18 October: the initial upgraded recorder used stale
hard-coded state IDs and only the ordinary mask. Human dash calibration failed;
`human-advanced-20261018-01/quality-notes.jsonl` marks its dash/climbing fields
unreliable. Those original observations are preserved, not rewritten. Reader
revision 2 resolves ground dash 41, air dash 46 and climbing 197 on this loaded
build and checks both masks (44 and 52 bytes). Unit tests cover predicted-only
activation, alternate enum IDs, bounds, missing masks and failed reads. The
native build and synthetic lifecycle pass. Human ground-dash verification passed
in `human-dashfix-20261018-01`: the latest isolated flat dash at tick 24085
activated the flag on that tick and cleared it at tick 24126 (0.640625 seconds),
with peak speed about 635 raw units/s and no crouch input. The broader marker
window contains unrelated movement, so only that isolated candidate supports
this calibration. Air-dash and climbing still need their own action checks.
The slide reader is independent and the
completed human slide calibration recorded a 1.203125-second active interval.

Charges and cooldowns are observations, **not a derived action-ready verdict**.
Eligibility may also depend on stamina, state, action priority and unrecorded
rules. Ability slots are raw identifiers; hero inventory changes remain visible.
Accounts are not included in frame observations.

The frame clock and `movement_sequence` (latest preceding movement callback
globally, possibly null) make the observation phase explicit. A frame can contain
zero or multiple movement callbacks and multiple pawns. Never attach a frame's
state to every earlier movement sample as though it was simultaneous. Select the
same world and pawn, then compare times and before/after boundaries explicitly.
The frame writer runs asynchronously with a bounded queue; losses or disk errors
fail the run rather than silently dropping state data.

While a live run is active, queue a trial marker:

```powershell
python capture_session.py mark --run pilot-01 --trial DJ-early-01 --event arm
python capture_session.py mark --run pilot-01 --trial DJ-early-01 --event start
# Perform the trial, then:
python capture_session.py mark --run pilot-01 --trial DJ-early-01 --event end
```

Events are `arm`, `start`, `end` or `abort`. IDs accept 1–64 ASCII letters,
digits, underscores or hyphens. Requests publish atomically in `marker-requests/`
and remain preserved. The CLI reports **queued**, not recorded. Match its
`request_id` to a `marker` row in `frames.jsonl` to confirm acknowledgement. The
native consumer checks approximately every 100 ms of wall time on game frames;
timestamps identify consumption, not the exact human keypress. Wait for start
acknowledgement before cueing the player. Multiple requests queued between
frames may share a tick; actual action edges still come from the input trace.
Trial names alone do not encode conditions: retain a separate protocol ledger
for hero/items, surface, intended inputs and observed failures.

```powershell
python capture_frames.py validate local/captures/pilot-01/frames.jsonl --movement-trace local/captures/pilot-01/trace.jsonl
```

Upgrade verification: native build/tests and **90 Python tests** passed. The real
plugin's synthetic lifecycle check passed. Live `frames-probe-01` recorded 1,290
movement samples, 1,294 frame snapshots and two acknowledged start/end markers,
with zero losses in either stream. Stamina was available on 1,290 frames;
slide/mantle were null during four initialization frames, then resolved false.
Grounded/airborne transitions were observed. The bot did not exercise active
sliding/dashing: a human dash/slide plus stamina-use calibration remains required
before advanced experiments. The probe stopped normally; no server was left running.

## Source and license

[Modlock pinned source](https://github.com/paralin/modlock/tree/67478b9bbc54ff1f94ccfdb5f9ce0e08dc6f6418)
provides native interop and hosting. Its MIT license is preserved in
`MODLOCK-LICENSE.txt`. The patch is deliberately separate and reviewable. The
upstream signatures and offsets must be rechecked for each game build; pinning
the code preserves provenance, not compatibility with future games.
