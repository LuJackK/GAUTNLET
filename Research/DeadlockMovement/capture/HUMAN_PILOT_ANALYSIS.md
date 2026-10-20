# First human pilot: analysis and evidence

Analyzed 18 October 2026. Session: `human-20261018-02`. The server has been
stopped and the capture finalized successfully. No Unity movement was changed.

## Evidence and scope

- Steam build ID: **26020302**. Source, runtime and game DLL hashes are preserved
  in `local/captures/human-20261018-02/manifest.json`.
- **113,392 samples**, one world, one pawn handle, zero missing/dropped samples,
  no callback-clock regressions or duplicate times. Header and footer validate.
- Callback clock span: **1,771.734375 seconds** (about 29.5 minutes). Most of this
  is waiting or repositioning; it is not 29.5 minutes of labeled experiments.
- Trace SHA-256: `cd2348c8d0ec68e8f8dd42d0d2719d8fcd754b8bc9d27479efda43cef753f747`.
- Hero selection changed before the test blocks; the last logged hero ID was 63.
  The user subsequently confirmed **Mina with Superior Stamina** for both the
  original and advanced human sessions. This is a user-reported configuration,
  preserved in each session's `configuration-notes.jsonl`; the item's effective
  modifiers were not independently resolved. These measurements are not an
  item-free baseline. Do not generalize these constants to other configurations.
- Activity messages establish broad trial blocks. Actual sample intervals are
  derived from input changes, motion and ground handles, not chat arrival time.

The recorded masks empirically associate bit 3 with forward, bit 44 with dash,
and bit 2 with crouch in this session. Jump presses contained multiple bits;
their individual meaning is not yet resolved. These associations are not a
complete or permanent button specification. Crouch-held is not slide-active.

## What was captured

| Block | Trials | Treatment |
| --- | ---: | --- |
| Forward hold/release | 3 | Stable direction and same peak speed |
| Standing jump | 3 | Two fitted; third withheld |
| Running jump | 4 | All withheld from vertical-jump fit; differing W release times retained |
| Forward dash | 4 | First three flat; fourth uphill excluded from flat baseline |
| Dash then crouch | 16 | Keep each direction and timing; flag terrain changes and lower-speed cases |
| Dash then crouch then jump | 4 | All withheld from vertical-jump fit; only first held W through the whole airborne interval |

These are 34 labeled attempts, not 34 independent configurations. Exact sequence
bounds and raw measurements are in the six `*-analysis.json` files alongside
the trace. `model-analysis-01.json` records numerical fits and label hashes.

## Walking, braking and dash

All three forward trials peaked at **318.909 raw units/s**. From the first
observed movement sample, all reached 95% of that peak after **0.34375 s**.
After the first observed zero forward axis, horizontal speed fell below 1 raw
unit/s after **0.500–0.515625 s**. These are descriptive landmarks; they do not
yet distinguish proportional acceleration from constant acceleration with a cap
or establish whether diagonal/backward movement shares the same rules.

The three flat dash candidates peaked at **635.005 raw units/s**. Each remained
above the diagnostic 400-unit/s threshold for **0.71875 s**. That threshold
interval is **not a measured dash ability duration**. The fourth attempt gained
about 33.34 units of height and stays in a separate terrain group.

## Vertical jump: strong initial model

Fit only the first two standing jumps. Let `t=0` be the first airborne AFTER
sample, and let `z0` be the BEFORE height of that same callback. In raw units
and callback-clock seconds, the fitted empirical curves are:

```text
z(t) - z0 ≈ 4.491335 + 280.992677*t - 399.990133*t²
vz(t)    ≈ 287.485469 - 800.003048*t
```

This supports an effective constant downward acceleration of about **800 raw
units/s²** over these airborne intervals. The first recorded upward velocity is
about 287.485; it is already after movement processing and is **not** a recovered
jump impulse. The position polynomial's linear coefficient and velocity
intercept differ; do not infer the exact integration order or replace these with
an assumed continuous-time initial condition.

The model was evaluated without retuning on the third standing jump, four
running jumps and four dash/crouch/jump attempts: **nine withheld jumps**.
Each has vertical-position RMSE about **0.00590 raw units**, maximum error about
**0.01003**, and vertical-velocity RMSE about **0.0000043 raw units/s**.
The aligned vertical traces are effectively identical at the recorded precision.
They each contain 46 airborne samples, a 0.71875-second takeoff-to-landing
callback span, and an observed rise of 53.8125 raw units.

This is strong evidence for the vertical free-flight portion in this session.
It does not validate horizontal air control, collision response, eligibility,
jump buffering, early/late jump variants, other heroes, or another game build.
The holdout is within one session and was selected after capture. A new-session
test on predeclared conditions is still needed before accepting a production model.

## Sliding: the present data does not justify one simple equation

We extracted the contiguous forward-plus-crouch interval following each dash,
then examined horizontal speed from its peak onward. This is an **input-defined
interval**, which can include slide termination and ordinary crouch movement.

Thirteen attempts reached approximately **643 raw units/s** during that interval;
three peaked around **445**. Some intervals cross elevation changes. Constant
height is only a flat-ground candidate filter, not proof of terrain geometry.
The lower peaks may reflect timing/eligibility or another state transition;
their cause is not established and they must not be silently averaged away.

A deliberately simple candidate was tested:

```text
speed(t) = observed_peak_speed * exp(-k*t)
```

Select intervals with crouch-held height span below 0.25 raw units, peak above
600, and at least 20 post-peak samples. Alternate whole eligible trials into fit
and holdout groups. Fit trials 2, 5, 8, 11, 13, 15 gave mean `k ≈ 0.90055 /s`.
On withheld trials 3, 6, 10, 12, 14, 16, speed RMSE was **37.5–40.3 raw units/s**;
maximum errors reached **66.5 raw units/s**. Each prediction uses its measured
peak, so this is already an easier test than predicting the whole action.

That candidate is insufficient as a faithful model of the entire interval.
It does not prove exponential friction is absent within a correctly segmented
slide. We need the actual slide state, input-held versus released controls, and
state-boundary timing before choosing friction and transition equations.

## Decision

The capture is good enough to start a vertical-jump approximation and establish
walking/dash targets. It is not yet enough to implement trustworthy combination
transitions. Improve state observation, collect the targeted human tests in
[the next-stage plan](ADVANCED_CAPTURE_PLAN.md), then finalize and validate the
input runner. Do not spend more human time repeating the same unsegmented slide
condition, and do not tune Unity constants to conceal state or collision errors.

## Reproduction

From `Research/DeadlockMovement`, choose a new output filename:

```powershell
python analyze_human_pilot.py local/captures/human-20261018-02 --output local/captures/human-20261018-02/model-analysis-repeat.json
python -m unittest discover -s tests -v
```

The analysis refuses incomplete or synthetic traces and will not overwrite its
output. It consumes the reviewed interval labels, hashes those labels and the
trace, and uses standard-library least squares. This is a pilot-specific analysis,
not a general action parser. Ground/action semantics remain hypotheses until
checked against direct state and visible behavior.
