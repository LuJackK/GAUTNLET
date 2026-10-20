# Rollback movement overhaul

The movement motor now uses one tick flow for grounded movement, air movement, slides, dashes, and melee. It keeps the existing SurfCharacter / MoveData / FishNet interfaces so existing player prefabs continue to load.

## Behavior

- Ground jumps run before slide movement and friction, at every slide speed. They clear slide state immediately and retain horizontal velocity.
- Dash and jump can compose on the same tick. Jump ends the dash gravity lock. Ground dash now establishes at least the configured dash speed instead of adding another full impulse to existing speed.
- Slide entry preserves velocity. It does not multiply speed or truncate momentum to the old maximum slide speed. Slide speed and direction are derived from collision-adjusted velocity.
- Holding crouch through an airborne movement chain allows sliding again on landing. Holding jump with autoBhop enabled gives immediate grounded takeoffs without a friction tick.
- Air and slide steering turn existing momentum at a bounded rate. Airborne movement has no ground drag. Input does not instantly rotate the entire velocity toward the requested direction.
- Melee charge counts the initial held time toward its charge requirement, retains horizontal momentum, and suspends vertical motion/gravity while charging. Gravity resumes immediately on lunge, recovery, or cancellation. Crouch cancels lunge consistently on every peer.
- Stamina, dash, jump, and slide timers advance regardless of the active movement mode.
- Water and ladder movement branches, camera-water objects, and asynchronous collision impulses have been removed from the player simulation.

## Simulation and collision contract

SimulationTick binds its supplied MoveData snapshot before updating movement and restores collider geometry before queries. All persistent movement state remains in MoveData and the reconcile payload. The coyote-jump experiment adds a synchronized grace timer and advances the explicit world movement wire format to version 2.

Crouch input requests a stance rather than overwriting the actual collider stance. The motor retains the current crouch state when a ceiling prevents standing. Resizing keeps feet fixed. Collider stance disagreement now requires reconciliation, and the owner/server no longer strip different crouch fields.

Movement uses bounded collider sweeps with collision clipping and optional step traversal. It recomputes the remaining displacement after each collision. Penetration recovery uses deepest-contact-first ordering and does not push rigidbodies. Capsule sweeps use the full capsule segment; box sweeps use shape dimensions instead of cached bounds. The sweep inset is 0.01 m because smaller box insets missed touching-floor contacts in the project's PhysX backend.

Only fresh authoritative ticks resolve melee hits against live targets. Prediction and replay use reconciled hit state; they do not stop a lunge or apply damage based on a target's present-day collider. This means hit confirmation remains subject to server latency.

This is a floating-point Unity/PhysX motor with prediction and authoritative correction, not a cross-platform bit-exact physics engine. Replay tests cover a static world. Moving platforms, dynamic obstacles, and historical target hitboxes still require world-state history for fully historical collision queries. Characters use the existing upright, axis-aligned collider setup. The networking transport and health/damage systems were retained.

## Tuning

New fields have defaults that apply without rewriting the player prefab:

| Field | Default | Effect |
| --- | ---: | --- |
| airControlAcceleration | 18 | Turn carried horizontal air momentum; m/s² |
| slideControlAcceleration | 10 | Turn slide momentum; m/s² |
| slideDrag | 3 | Flat-ground slide speed loss; m/s² |
| slideSlopeAcceleration | 1 | Multiplier on gravity projected onto the slope |
| meleeChargeMomentumRetention | 0.8 | Horizontal speed retained on charge entry |

Existing walk speed, ground acceleration/friction, gravity, jump force, dash speed/duration, stamina, and melee timing remain configurable. airAcceleration now specifies added air speed per second. maxVelocity remains the global horizontal speed limit. Legacy swim, air-cap, slide-multiplier, and charge-retention fields are hidden and ignored for compatibility with serialized assets.

### Adjustable Source-style air strafing

On the player prefab's SurfCharacter component, expand Movement Config > Air movement:

- `sourceAirStrafeStrength` (0–1, default **0**): 0 runs the original air movement operations unchanged. Raising it reduces direct momentum steering and lowers the directional acceleration target. At 1, direct air steering is disabled and acceleration uses a Source-style velocity projection cap. Try 0.5 for a middle setting and 1 for the strongest directional restriction.
- `sourceAirSpeedCap` (m/s, default **0.4**): the full-strength cap on speed built along the requested input direction. This is not a cap on carried horizontal speed. Holding a fixed strafe direction reaches this cap; coordinated camera rotation changes the direction in which acceleration is available. Lower caps restrict ordinary sideways corrections more strongly.

Intermediate strengths interpolate the uncapped input speed toward the capped input speed and scale direct steering by `1 - strength`. `airAcceleration` retains its existing m/s² units; this is Source-style directional acceleration, not an exact reproduction of Valve's acceleration coefficients. The new fields do not reuse the ignored legacy `airCap`/`clampAirSpeed` fields. Existing serialized assets retain their current behavior because strength defaults to zero.

Applies to ordinary airborne walking, jumping, falling, and air-dash steering. Ground movement, slide steering, and melee charge/recovery/lunge behavior are unchanged. No takeoff heading lock or general air drag is added. Existing `maxVelocity` and collision responses still apply.

These are base simulation settings: use identical values on client and server and keep them fixed during a replay session. No new per-tick state or network payload fields are needed. To revert the movement behavior immediately, set strength back to 0.

Validated September 16, 2026 with Unity 6000.3.2f1 in an isolated project copy: **80 PlayMode tests passed, 0 failed**, including nine new air-strafing cases. Coverage includes exact zero-strength compatibility over 120 changing-input ticks, progressive restriction, coordinated/wrong-way camera turns, configurable caps, carried momentum, analog input, no-input coasting, and snapshot replay with a recreated controller. Results: `TestResults/air-strafe-final.xml`. Final movement feel still requires playtesting.

### Experimental coyote jumping

On `Assets/Prefabs/Player 1.prefab`, expand **SurfCharacter > Movement Config > Jumping and gravity > Coyote Time**. The default is **0.1 seconds**. Set **0** to disable the grace window. Use identical settings on client and server, fixed during replay; both peers must use the updated network payload.

Walkable ground detected by the existing collision queries refreshes grace. After losing that ground, a fresh jump press during the remaining window uses `jumpForce`, preserves horizontal momentum, consumes no stamina, and leaves the usual double jump available after `doubleJumpDelay`. Ground snapping and slope limits still determine when ground is lost. The timer advances in simulation time in all modes, including melee hover; coyote time does not enable jumping during melee.

Every successful jump consumes grace. Holding jump only retains the existing grounded auto-bhop behavior; airborne grace requires a new press. Spawning airborne grants no grace. After expiry, the existing stamina-based air-jump rules apply. Collision clipping, dash/jump ordering, and gravity still run normally; the character is not marked grounded during a coyote jump.

The timer is cloned, reconciled, and serialized with movement state; respawn resets it. Pre-experiment Git checkpoint: `0a31ea3`. Setting Coyote Time to 0 disables the behavior without reverting code. Subjective feel, low ceilings, slopes, and live server/client latency should still be playtested.

Validated September 16, 2026 with Unity 6000.3.2f1 in the isolated project copy: **92 PlayMode tests passed, 0 failed**. The 12 added cases cover box/capsule ledges, expiry and disabling, extended grace, held input and airborne spawn, takeoff consumption, ceiling clipping, melee timer progression, snapshot replay, and reconcile/respawn state. Existing world serialization tests verify the added field too. Results: `TestResults/coyote-jump-final.xml` (local, ignored by Git).

## Verification

Verified with Unity 6000.3.2f1 on September 12, 2026: **37 PlayMode tests passed, 0 failed**. Results: TestResults/movement-overhaul.xml. The changed code and tests also pass git diff --check.

The ReplayHarness assembly is now discoverable as PlayMode tests. The fixtures configure character references before Awake and initialize FishNet objects before activation.

Coverage includes slide-jump momentum, dash-jump composition, repeated bunny hops, landing into slides, air steering, cooldown progression, thin walls, tangential wall movement, steps, crouch clearance, capsule dimensions, rigidbody side-effect suppression, state restore, reconcile round trips, real-prefab repeatability, delayed authoritative correction, and melee replay against live targets.

Run with the project's Unity version:

    Unity.exe -batchmode -nographics -projectPath "<project>" -runTests -testPlatform PlayMode -testFilter Fragsurf.ReplayHarness -testResults "<project>/TestResults/movement-overhaul.xml" -logFile "<project>/TestResults/movement-overhaul.log"

Automated tests establish movement/replay behavior, not final subjective feel or live multiplayer latency performance. Playtest the player prefab on Map1/Map2 with a separate server/client, especially diagonal air turns, slide-hop landings, low ceilings, and melee hit confirmation.

## References

The existing Fragsurf naming and helper structure are consistent with the [UnitySourceMovement / Fragsurf lineage](https://github.com/Olezen/UnitySourceMovement). This overhaul is original Unity movement code; it does not import Source 2.

The requested movement direction is consistent with Deadlock's documented slide-jump and air-control changes in the [March 6, 2026 update](https://www.deadlock.io/en/news/updates/gameplay-update-03-06-2026-2026-03-06-1826362059925616). The tuning here is game-specific, not a claim of exact Deadlock movement values.

## Presentation compatibility follow-up

Melee charge now hovers using the existing reconciled melee phase, with no new network fields or Rigidbody gravity toggles. The Animator retains its parameter names and types. The previously missing MeleeLunge trigger is now declared; the charge-to-lunge transition continues to use MeleeCharging=false and MeleeLunging=true. Its 0.25-second blend is unchanged. A same-tick dash-jump now emits DashDone as well as Dash. VFX and audio retain their existing state/event inputs.

Follow-up validation: **44 PlayMode tests passed, 0 failed** in an isolated project copy using Unity 6000.3.2f1. Results: TestResults/movement-compatibility.xml. Added coverage for charge hover while rising/falling, gravity resumption, snapshot replay, all 17 player Animator parameter names/types, and melee/slide/dash presentation state.

## Deadlock movement model (October 2026)

A second movement model reproduces Deadlock's movement rules as recovered in
`Research/DeadlockMovement/` (static analysis of the game plus capture checks; see `BEHAVIOURS.md` there
for each rule by ID). Legacy remains the code default, so existing assets and tests are unchanged.

**To try it:** on `Assets/Prefabs/Player 1.prefab`, SurfCharacter > Movement Config > Movement model,
set **Movement Model = Deadlock**. Values are under **Deadlock**, in metres and seconds (1 Deadlock
unit = 0.0254 m). Defaults are Deadlock's base hero; tooltips give Mina's values where they differ. Use
identical settings on client and server.

Included, each confirmed against Deadlock captures unless marked:

- Ground friction (Source law, stop speed 2.54 m/s, friction 4) and acceleration (hero acceleration 4,
  using at least 10.16 m/s as wish speed), friction before acceleration. Walk 7.2 m/s, crouch 4.75 m/s.
- Gravity 20.32 m/s², ground jump 7.62 m/s, late jump within 0.15 s of walking off (falling only, free).
- Air jump: vertical speed set to 13.335 m/s, plus up to 5.08 m/s added along the input direction;
  1 stamina.
- Ground dash: discards existing velocity, holds distance / duration (10 m / 0.68 s) along the input
  direction relative to the view, no friction or input during it; ground friction afterwards.
- Air dash: air acceleration toward the dash direction with a target speed of distance / duration
  (8 m / 0.47 s) shrinking to 20% over the dash, at 60% of air acceleration; existing momentum is kept;
  vertical speed held near zero and gravity off for the dash.
- Air drag 0.2 (same law as friction).
- Slides: start at 8.89 m/s on flat ground (6.35 m/s while ground dashing, or on a downhill slope of 8°
  or more), end below 4.32 m/s on flat ground or when crouch is released. Deceleration is ground
  friction with a slide coefficient: 0.175 for 1 s after a dash-slide, otherwise 0.05 for the first
  0.6 s (0.75 s when landing into the slide; grace is decoded, not yet capture-tested), then 0.8.
  Crouched wish speed still accelerates, so a slide held with forward settles at crouch speed.
- Dash-slide: crouching at least 0.3 s into a ground dash sets speed to 16.51 m/s, earlier to 11.43 m/s.
- Stamina: 3 bars, 1/4.5 bars per second; dash, air dash and air jump cost 1.
- Decoded but not yet capture-tested: air strafing (Source air acceleration with a wish-speed cap of
  2.03 m/s, easing to 1.27 m/s above 15–20 m/s carried speed) and slide slope acceleration/friction.

Not included yet: dash-jump (deprioritised), down dash, sprint, landing and sprint slide boosts,
slide steering, slide jump air drag, wall jump, wall slide and mantle. Heavy melee and parry use the
legacy rules in both models. A ground dash carried off a ledge keeps its speed with gravity on
(not capture-tested). Legacy fields such as `walkSpeed`, `friction`, `jumpForce`, `dashImpulse`,
`slideDrag` and `airControlAcceleration` are ignored in the Deadlock model.

New rollback state: `MoveData.dashDirection`, `airDashing`, `dashSlideTimer`, `slideGraceTimer`. They are
cloned, reconciled, serialized (world movement wire version 3) and traced. Respawn resets them.

Tests: `Assets/Tests/ReplayHarness/DeadlockMovementTests.cs` runs at Deadlock's 64 Hz with Mina's values
and asserts the measured capture numbers (for example 25 u/s per walk tick, 23 ticks to 95% walk
speed, 34 ticks to stop, 287.5 u/s after a jump tick, a 635 u/s dash, a 68.66 u/s first air-dash tick,
642.89 / 445.08 u/s dash-slide entry).

Validated October 21, 2026 with Unity 6000.3.2f1 in batch mode on the project: **107 PlayMode tests
passed, 0 failed** (92 existing, unchanged, plus 15 Deadlock-model tests). Results:
`TestResults/deadlock-port-03.xml` (local). Feel, multiplayer latency and the decoded-only parts (air
strafing, slide slopes, slide grace) still need playtesting and capture checks.

Found while testing: the ground sweep does not detect a 400 m floor box (the character oscillates a
few millimetres above it) in both models, while a 200 m box works. Large single floor colliders in
levels may break grounding; worth checking on the real maps.
