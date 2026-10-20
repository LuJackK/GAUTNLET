# Advanced human tests and input-runner plan

Agreed sequence: analyze the first pilot; improve capture; complete advanced
human tests; finalize the input runner; automate the remaining targeted tests.
This document specifies the staged work. The current recording has been finalized.
Update, 18 October: after-frame movement/resource observations and game-clock
marker acknowledgements are now implemented and smoke-tested; see
[the recorder guide](README.md#frame-state-and-trial-markers-18-october-upgrade).
Human active-state calibration, richer trial-condition tooling, terrain normals,
timeline exports and the input runner remain pending.

## Goal and stopping rule

Reconstruct a small set of movement equations and state transitions that predict
unseen actions on the same documented game build, then transfer them into an
isolated GAUNTLET comparison harness. We want trajectories, retained momentum,
eligibility windows and cancellation outcomes, not just similar peak speeds.

Advance one module at a time. Fit on designated trials, freeze its parameters,
then predict withheld timing, speed, direction or slope conditions. Record
position/velocity error and transition success separately. Set numerical
acceptance thresholds after the next stationary/repeatability calibration and
before evaluating the holdout. A wrong action outcome cannot pass on average
trajectory error alone. Keep uncertain rules explicit; do not claim exact code recovery.

## Capture improvements before advanced human testing

1. **A trial ledger with game-clock markers.** Give every trial a stable ID,
   intended action, map/location, surface, hero, items/modifiers, input protocol
   and expected outcome. Record arm/start/end/abort events, actual pawn handle,
   world, engine tick and nearby sequence numbers. Chat notes remain annotations
   with receipt times. Separate intended action from observed outcome.
2. **Direct movement and ability states.** Add available schema-checked grounded,
   sliding, dashing, climbing/mantling and relevant jump/ability state. Capture
   stamina and cooldown/charge availability where supported. Begin with
   Modlock's existing optional PawnObserver/ability fields; missing observations
   remain null. Verify every new field with an isolated visible action.
3. **Explicit observation phase.** Preserve existing pre/post movement records.
   Record frame-level state as a separate stream with phase and tick labels;
   never backfill a later frame's slide flag into an earlier movement sample.
   Compare previous post-state to next pre-state to identify changes made outside
   the hooked function. Resolve this before claiming a complete integration model.
4. **Input semantics.** Retain raw masks, axes, angles, command/tick/fraction data.
   Verify decoded press/hold/release edges through isolated human inputs and
   pinned native definitions. Keep empirical and confirmed mappings distinct.
   Do not assume current fields contain every sub-tick event.
5. **Terrain and session conditions.** Record a named flat strip and repeatable
   ramps. Add contact/surface normals only if the supporting native query is
   understood; otherwise measure map geometry separately. Label ramps by location
   and direction until their angles are known. Record build, hero, items, health,
   movement modifiers, stamina and start pose for each trial.
6. **Review aids and export.** Produce per-trial timelines of buttons, speed,
   vertical position and state, plus a short outcome summary. Optional video
   should have a shared visible marker to align it with game-clock events.
   Flag terrain changes, input deviations, low resources, collisions and respawns.
   Preserve rejected trials with reasons rather than deleting them.

Before inviting the next human session, verify new fields on a short local smoke
test and synthetic schema tests. Start Steam and finish updates before starting
the local capture host to avoid locking files needed by Steam.

## Human test protocol

Use the same documented hero without items/modifiers for the baseline. Work on
one named surface at a time. Wait until stationary and resources recovered before
each trial. Turn around/reposition outside marked trials. Start with three
repetitions per condition, including failures; add repeats only when outcomes
vary. Human delays may be approximate: record the actual delay instead of
rejecting every imperfect attempt. Reserve an entire condition or session for
validation rather than random neighboring samples.

Run these in short blocks with a review between blocks, not one uninterrupted
marathon. Check that state and input channels are working after the first example
of each new action. We will cue one condition at a time in chat.

| Order / ID | Inputs and variants | Question answered |
| --- | --- | --- |
| 0 / CAL | Stand; isolated forward, crouch, jump and dash presses; one simple run/release and jump | Verify mappings, state phases, resource fields and new-session baseline |
| 1 / DJ | W + dash, then jump early, middle and late in the dash; no crouch; compare tap versus holding jump before eligibility | Dash-jump success window, momentum retention, press/hold/buffer behavior |
| 2 / AD | Standing jump then forward air dash during ascent, near apex and descent; repeat from running jump | Air-dash velocity replacement/addition, vertical effect and timing restrictions |
| 3 / AD-DIR | Forward, backward and sideways air dash with fixed camera; separate camera rotation from direction-key changes | Direction selection, speed preservation and view-relative rules |
| 4 / AIR-DRAG | Running jump, then release all movement; compare W held and backward input; repeat with higher entry speed from dash-jump | Passive horizontal decay versus active acceleration/braking; dependence on entry speed |
| 5 / AIR-STEER | Running jump then A or D with fixed camera; separately rotate camera with no movement key and with W held | Input steering versus view-driven changes; turn rate versus lateral acceleration |
| 6 / SL-ENTRY | Run then crouch; dash then crouch early/middle/late; no jump | Minimum entry speed, eligibility, dash interruption and the approximately 445/643 speed cases |
| 7 / SL-DECAY | After a confirmed slide, keep crouch held and compare W held versus released; separately release crouch early | Slide friction, effect of forward input, natural end versus manual cancellation |
| 8 / SL-JUMP | Jump early/middle/late in confirmed slide; no second dash; compare W held versus released in air | Momentum transfer, jump availability and slide termination |
| 9 / DSJ | Dash → slide → jump with early/late crouch and early/late jump; add attempted simultaneous inputs in a separate block | Combined action ordering; test whether isolated models compose correctly |
| 10 / SLOPE-IDLE | Stand still on the chosen ramp, facing uphill/downhill/across; no crouch | Ground attachment, passive drift and whether facing matters |
| 11 / SLOPE-SL | Enter slide uphill and downhill at matched observed entry speed; repeat on a second ramp | Slope acceleration, speed caps and stopping/exit thresholds |
| 12 / SLOPE-X | Slide across ramp, then steer uphill/downhill; keep camera-only variants separate | Whether slope effect follows surface gradient or travel direction |
| 13 / SLOPE-EDGE | Flat → ramp, ramp → flat, and ramp crest transitions; repeat walking, dashing and sliding | Projection, speed preservation, ground loss and state transitions |
| 14 / LAND | Run off a low ledge without jumping; compare ordinary landing, crouch held before landing and crouch pressed after | Gravity independent of jump, landing friction, buffered slide and timing order |
| 15 / CANCEL | While sliding, release crouch, reverse input, jump or dash in separate trials; attempt actions with insufficient stamina separately | Priority, cancellation and failed-action outcomes |
| 16 / COYOTE | Walk/run off the same low ledge without jumping first, then tap jump just after leaving; compare earlier/later taps, a held jump and a no-jump control | Whether a post-ground-loss jump window exists; distinguish coyote eligibility from input buffering, air jump or wall contact. Preserve failures and measure actual ground-loss-to-input delay rather than assuming a window. |
| 17 / BHOP-AIR | On flat ground compare repeated landing-time jump taps with jump held through landing; repeat with fixed-camera strafing, camera-only steering, then coordinated A/D plus camera turns | Momentum retained at each landing/takeoff, air steering contribution, and whether combined bunny hopping preserves more speed than matched entry-speed controls. Include ordinary running-jump and no-strafe hop controls. |

Blocks 0–8 are the first priority. Then cover combinations and slopes. Wall
jumps, mantle/ledge clearance, zipline attachment/release and hero-specific
movement form a later expansion after the core model and recording phases are
validated. They should not contaminate the baseline trial pool.

For each trial record: intended inputs, actual input times, start/end state,
entry speed, exit/landing speed, path, vertical motion, state transitions,
resource use and success/failure. A failed action is evidence, not a bad file.

## Input runner: design now, finalize after human tests

The runner is a controlled experiment tool, not a movement implementation or a
bot AI. It must send normal inputs through a verified native command path and
let Deadlock compute all motion. It must not replay recorded trajectories or
write position/velocity during the measured interval.

Choose the injection seam after the advanced human traces establish input
semantics. Prefer a normal local client's command path if it preserves the same
movement/ability behavior; accept a server-native command path only after parity
tests. Current autonomous bots moved with zero captured axes, so that bot path
is not accepted as an input equivalent. Do not use OS/chat timing as simulation
timing. Do not replace the read-only observer with Modlock's movement replacement
handler to manufacture expected results.

### Minimal runner contract

- Declarative trials: initial conditions, ordered press/release/view events,
  target engine ticks, resource preconditions, maximum duration and assertions.
  Store intended schedule and the inputs actually observed by the game separately.
- Lifecycle: prepare → wait for valid pawn/resources/rest → arm → execute →
  settle → validate → save. Abort on pawn/world change, respawn, timeout, lost
  samples, missing state or an unintended human interruption.
- Resets are allowed only before a marked settling period. Log teleports,
  velocity resets and resource resets as interventions. Reobserve actual pose,
  ground and resources; do not assume a reset succeeded.
- Initially support forward/back/strafe axes, crouch, jump, dash and fixed view
  angles. Every press must have a release; abort must release all held inputs.
  Add camera paths and other actions only when a specific experiment needs them.
- Begin with whole-tick scheduling. At the observed 64 Hz this gives 15.625 ms
  increments; do not claim sub-tick precision without verifying a finer input
  path. Record requested and observed timing so missed deadlines fail explicitly.
- Keep runs on the private local test server. Never attach to an unrelated game
  session or run a trial against another player's pawn.

### Runner acceptance gates

1. Verify pulse lengths and release edges in the recorder, including abort and
   timeout. Every scheduled event must be accounted for; no stuck buttons.
2. Reproduce simple human walk/release, jump and dash trials from matching actual
   conditions. Compare trajectory and state changes, not only peak speed.
3. Reproduce selected advanced human trials and their failures. If automatic
   input bypasses cooldown, stamina, prediction or action eligibility, reject that
   path rather than tuning the model to its output.
4. Repeat a fixed trial at least five times; report initial-state differences,
   actual timing and output spread. Compare independent sessions, not just copies
   inside one run. Confirm no unexplained state writes during measurement.
5. Freeze the runner version, source/build hashes, protocol and model tolerances
   before collecting the withheld parameter sweeps.

## What the runner should capture after human testing

Prioritize experiments that are tedious or too precisely timed for manual play:

- Sweep dash→jump and dash→crouch delays one engine tick at a time near observed
  success boundaries. Include early taps, early holds, same-tick inputs, late
  presses and failures. Test action ordering separately from eligibility.
- Sweep crouch→jump delay and selected pairs of dash/crouch/jump delays. Use a
  coarse grid first, refine only around boundaries; reserve whole delay pairs.
- Repeat air-dash at controlled vertical phases and entry speeds. Separate
  forward/reverse/side input from view rotation.
- Compare no-input, forward-held, reverse and lateral air motion over matched
  entry speeds; fit and test candidate drag/acceleration rules.
- Reproduce slide entry/exit at speeds just above and below observed thresholds;
  vary input holds without mixing terrain changes.
- Match entry speed and direction on measured ramps, then test unfit slopes and
  ramp transitions. Avoid a full Cartesian product until simpler hypotheses fail.
- Probe landing input windows and cancellations with adequate versus insufficient
  resources. Preserve both successful and rejected actions.

The runner is successful when it reduces human repetition and produces trials
with trustworthy initial conditions, observed inputs and explicit outcomes.
Its value is not the number of recordings: it is resolving a named uncertainty
and validating a model on new conditions.

## Deliverables and execution order

1. **Done:** finalized first human recording, empirical jump holdout analysis,
   walking/dash landmarks, and rejected simple whole-interval slide-decay model.
2. **Next:** improved capture schema/state/markers and a short calibration test.
3. **Human session:** blocks 0–8, review, then combinations/slopes/landings; export
   trial ledger and select withheld conditions before fitting.
4. **Then:** finalize the minimal runner against those human benchmarks and pass
   its parity/abort/repetition gates.
5. **Then:** targeted automatic sweeps, module fits and independent validation.
6. **Finally:** isolated Unity replay comparison; implement only supported rules,
   with rollback-compatible state and explicit remaining approximations.
