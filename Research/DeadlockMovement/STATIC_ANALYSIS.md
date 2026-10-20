# Static analysis of Deadlock movement code

Started 2026-10-20. This replaces the deferred "executable analysis" stage in the
[reconstruction plan](RECONSTRUCTION_PLAN.md). The method: read the server's movement
and traversal-ability code in Ghidra to recover **structure** (states, branches,
thresholds, timers, operation order, which configured value feeds which formula),
then confirm each reading against Modlock captures. Captures stay the acceptance test.

## Rules

- Work only on hashed copies under `local/binaries/<steam build>/`; never on the install.
- Keep the binaries, Ghidra projects, dumps and decompiled output under ignored `local/`.
- Write findings as equations and plain pseudocode in research notes, in our own words.
  Do not copy decompiled code into GAUNTLET or commit it.
- Every finding names its Steam build, the function RVA it came from, and the capture
  that confirms or contradicts it. An unconfirmed reading is a hypothesis.
- Steam's subscriber agreement prohibits reverse engineering. Keep this local and private.

## Setup

From `Research/DeadlockMovement`:

```powershell
python bootstrap.py --ghidra
python static_analysis.py snapshot --game-dir "C:\Program Files (x86)\Steam\steamapps\common\Deadlock"
python static_analysis.py dump --game-dir "C:\Program Files (x86)\Steam\steamapps\common\Deadlock"
python static_analysis.py types --build BUILD
python static_analysis.py analyze --build BUILD
python static_analysis.py gui
```

| Step | What it does | Output under `local/binaries/BUILD/` |
| --- | --- | --- |
| `bootstrap --ghidra` | Installs Ghidra 12.1.4 and Temurin JDK 21 (Windows x64), SHA-256 pinned in `ghidra.lock.json` | `.tools/ghidra`, `.tools/jdk` |
| `snapshot` | Copies `server.dll`/`client.dll`, hashes them, runs `modlock-sigcheck`, turns the 61 resolved signatures into function labels | `server.dll`, `client.dll`, `manifest.json`, `labels.json`, `sigcheck.log` |
| `dump` | Starts a local server via `modlock-host --dump`, which quits by itself; refuses if the installed DLLs differ from the snapshot | `dump/schemas.json`, `console.json`, `entities.json` |
| `types` | Flattens selected schema classes (with base classes) into struct definitions at exact field offsets, plus a partial `CMoveData` from Modlock | `types.json` |
| `analyze` | Headless import and auto-analysis (8 GB heap; can take over an hour), then the two scripts below | `local/ghidra/BUILD/deadlock-BUILD.gpr`, `probe-report.json` |
| `annotate` | Reapplies both scripts without reanalysing, e.g. after `types --extra-class` | same |
| `gui` | Opens Ghidra with the pinned JDK; open the project above | |

Scripts in `ghidra_scripts/`:

- `ApplyResearchAnnotations.java` names each Modlock-resolved function `ml_<id>` with its
  native shape as a comment, creates the schema structs under `/DeadlockSchema/BUILD`,
  and types `ml_movement_process(services, CMoveData* mv)`.
- `ProbeFieldUses.java` ranks functions by how many distinct fields of the slide, dash,
  jump, mantle and power-jump VData classes they touch (memory-operand displacement equal
  to a field offset of 0x100 or more, ignoring stack accesses). It marks whether a function
  is a direct callee of `ml_movement_process` within four levels. Virtual calls are
  invisible to that check, so `false` does not rule a function out.
- Read-only exploration, run with `python static_analysis.py script --build BUILD NAME.java ARGS...`:
  `FindSymbols.java` (symbols/strings containing text, with referencing functions),
  `XrefsTo.java` (functions referencing given RVAs), `DecompileFunctions.java` (decompile
  RVAs to `.c` text files; keep them under `local/`), `ExportClassMethods.java` (RTTI
  vftable slots; finds nothing on builds without RTTI). Only one Ghidra process can open
  the project at a time, so close the GUI before running these.

## How to read a function

1. Start from the probe report: high-scoring functions touching many `CAbilitySlideVData`
   fields are slide logic candidates. Also walk outward from `ml_movement_process`.
2. In the decompiler, retype the parameter or local that holds the VData pointer as
   `CAbilitySlideVData *` (Ctrl+L). Field reads then show as `vdata->m_flFrictionFlatGround`.
   Do the same for `this` as `CCitadel_Ability_Slide *` and for movement services.
3. Rename helpers as their purpose becomes clear, with a `?` suffix while uncertain.
4. Write the logic up in `local/notes/` first, then promote confirmed findings to a
   committed note: inputs, state read/written, equation, order, conditions, build, RVA.
5. Predict an existing capture with the written model. A mismatch means the reading is
   wrong or incomplete; do not tune constants to hide it. If a branch stays ambiguous,
   log the value at that address through Modlock on a local server.

## Build 26059233 status

- Steam build **26059233** (engine build 6796). Note the human pilots were recorded on
  26020302; behaviour may differ between builds, so recapture any check that disagrees.
- `server.dll` SHA-256 `deba4a4f7742839763b2877ee8d490a0cadb4b626e4af7c85c3ac7ba4bc61a66`;
  `client.dll` `8dff04c139d20b78d8d2c7a3039b691cfc221b8460438ec6fc6107c315741a5c`.
- 61 of 61 Modlock signatures resolved; `movement.process` at server RVA `0xb2f7b0`,
  client RVA `0x887f40`.
- Schema dump taken on this build; 22 structs generated. Probes: CAbilityDashVData,
  CAbilityJumpVData, CAbilityMantleVData, CAbilityPowerJumpVData, CAbilitySlideVData.
- Headless analysis took about 30 minutes. 58 server labels and 22 structs applied.

### What did not work

- **Ghidra does not create `vftable` symbols here**, so `ExportClassMethods.java` finds nothing.
  The RTTI itself is present: about 6,000 type descriptors in each DLL. Use
  `python static_analysis.py vtables --build BUILD CLASS... --base CCitadelBaseAbility`, which
  walks type descriptor → complete object locator → vtable straight from the PE and lists the slots
  each class overrides. Abilities also have secondary vtables. The interface at +0x4c0 (1216)
  carries the movement callbacks: slide overrides slots 1, 4 and 7, and slot 4 is its per-tick
  movement function.
- **The field-offset probe is confounded.** Every ability VData subclass starts its own
  fields at +5096, right after `CitadelAbilityVData`, so any ability's config code matches
  slide offsets. The top hits (`0x287cb0`, `0x28e430`) are schema registration functions
  that reference every field-name string. Keep the probe only as a secondary filter.

### Anchors that work

Console variables are registered in small static initializers that store a 16-byte handle
in a global; runtime code reads the value at `handle + 8 → +0x58`. Cross-referencing those
globals (`XrefsTo.java`) lands directly in movement code. Schema field-name strings and
entity-factory names (`citadel_ability_slide`) are further anchors.

| RVA | Proposed role | Evidence |
| --- | --- | --- |
| `0xb2f7b0` | ProcessMovement(services, CMoveData*) | Modlock signature |
| `0xb06ec0` | Accelerate(services, mv, dt) | reads `citadel_min_accel_speed`; vector add to mv velocity (+0x38) |
| `0xb139f0` | accel-value selector | airborne → `sv_airaccelerate`; grounded → hero stat |
| `0xb08540` | ground Friction(services, mv, dt) | reads `sv_friction`, `sv_stopspeed` |
| `0xb07140`, `0xb076c0` | air movement / air drag (unread) | read `sv_airaccelerate`, `citadel_air_drag_min` |
| `0xb09ab0` | slip acceleration (unread) | reads `citadel_movement_slip_accel` |
| `0x265660` | slide-related (unread) | reads `citadel_player_slide_min_percent` |

### Findings, build 26059233

Source-unit values; `dt` is the tick interval (1/64 s observed). Written from the
decompiled logic in our own words. Status column: *confirmed* means a capture agrees.

**Movement console variables still carry Source 1 defaults**: `sv_gravity` 800,
`sv_friction` 4, `sv_stopspeed` 100, `sv_accelerate` 10, `sv_airaccelerate` 10,
`sv_maxspeed` 320. Deadlock adds `citadel_min_accel_speed` 400, `citadel_air_drag_min` 0.2,
`citadel_slide_delay` 0.15, `citadel_player_slide_min_percent` 0.8,
`citadel_player_ground_dash_min/max_percent` 0.5/2, dash stamina cost 1, wall-jump
stamina cost 0.5 with 1 free wall jump, and others in `dump/console.json`.

**Ground friction** (Source 1 shape, horizontal only):
- Applies when grounded (or a flag at mv+0x10e is set) and a vector at mv+0xf4 is either
  unset (FLT_MAX) or shorter than 1 unit horizontally.
- `speed = |v.xy|`. Below 0.1, horizontal velocity is zeroed.
- `friction = sv_friction × surfaceFriction (services+0x26c) × hero multiplier × modifier multiplier`,
  doubled when the mv+0xf4 vector is set. Multiplier sources not yet read.
- `newspeed = max(0, speed − max(speed, sv_stopspeed) × friction × dt)`; v.xy scaled to match.

**Accelerate**:
- `addspeed = wishspeed − dot(v, wishdir)`; nothing happens if it is not positive.
- Grounded: `accelspeed = max(wishspeed, citadel_min_accel_speed) × accel × surfaceFriction × dt`,
  where `accel` = hero stat **EMoveAcceleration** (stat 10; 4 in `heroes.vdata`) ×
  modifier `MODIFIER_VALUE_GROUND_ACCELERATION_PERCENTAGE` (default 1). `sv_accelerate`
  is not used here.
- Airborne: `accelspeed = wishspeed × sv_airaccelerate × 0.25 × dt`.
- `v += min(accelspeed, addspeed) × wishdir`.

| Check | Model | Capture (human-20261018-02, build 26020302) | Status |
| --- | --- | --- | --- |
| Walk to 95% of 318.909 | friction then accelerate: 23 ticks (0.359 s) | 0.34375 s from first moving sample (22 ticks after it) | **confirmed** to within the sample-origin tick |
| Accelerate before friction | never reaches 95% | — | ruled out |
| Release to < 1 u/s | 34 ticks (0.531 s) | 0.500–0.516 s | **1–2 ticks fast in capture**: read the friction multipliers |

**Friction multipliers, decoded:** `0xdf87e0` returns the pawn friction coefficient (pawn
+0x3fc, default 1) × `MODIFIER_VALUE_FRICTION_PERCENTAGE`; `0xaf8870` returns
`MODIFIER_VALUE_GROUND_FRICTION_PERCENTAGE`. The slide writes the pawn coefficient (setter
`0x1cf4de0`). With no slide or modifiers, both are 1, so the release gap is not explained by
them. **Unit scale:** `0x3c58a0` divides by 0.0254 (metres to units).

**Slide:** fully mapped in [BEHAVIOURS.md](BEHAVIOURS.md#slide) (B-SLD-01 to 10). Dash-slide
entry speeds predict the pilot's 643 and 445 u/s peaks to the unit.

### Next

1. Slide leftovers: air-drag provider `0x2632e0` (B-SLD-09), `Bias` `0x2550650`, steering turn
   function `0x254fc70`, how `m_flGroundDashSlideTime` is set, and the sprint-boost suspicion.
2. Air movement and air drag (`0xb07140`, `0xb076c0`). Wishspeed derivation (`0xb0b640`).
3. Dash: `CCitadel_Ability_Dash` overrides in the +0x4c0 interface (slots 2, 5, 8 and others).
4. Order of calls inside `ProcessMovement`, and the release-time gap (surfaceFriction, mv+0xf4).

## First targets

1. **Slide**: friction (flat, grace, slope, uphill), activation and sustain speeds,
   boosts, dash-slide window and friction, steering. This is where black-box fitting failed.
2. **Dash and dash transitions**: displacement curve use, end-velocity scaling,
   dash-jump and dash-slide windows, buffering and input priority.
3. **Jump and double jump**: impulse, stamina cost and regeneration rules.
4. **Base ground/air movement** in `ml_movement_process`: compare against Source 1
   `gamemovement.cpp` friction and acceleration.
