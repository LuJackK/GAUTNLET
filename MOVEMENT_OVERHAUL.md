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

SimulationTick binds its supplied MoveData snapshot before updating movement and restores collider geometry before queries. All persistent movement state remains in MoveData and the existing reconcile payload; no new wire fields were added.

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
