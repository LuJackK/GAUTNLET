# Native input seam audit — 19 October 2026

## Decision

Build the initial scheduler, trial ledger and requested-versus-observed validator
with an explicitly unavailable live backend. The pinned local Modlock code does
not yet provide a verified full input command path for axes, view, crouch, jump
and dash together. Do not describe a dry run or a button-memory write as a
successful automated movement trial.

The preferred production seam is a local client's command construction path,
before prediction and transmission. An earlier server usercommand-processing
seam is the fallback, subject to human parity tests. Neither is currently
implemented or signature-resolved in the inspected Modlock source. The separate
`AbilityInputHook` is useful for a narrowly scoped, instrumented button pilot;
it is insufficient to approve the full runner.

Scope: inspected `local/modlock`, the existing native recorder, first human trace,
and `ADVANCED_CAPTURE_PLAN.md`. No game was launched, no input was injected, no
external repository was fetched, and no live viability was established.

## What the available hooks actually do

| Surface | Source evidence | Consequence |
| --- | --- | --- |
| `MovementHook::Observe(kServer, ...)` | `capture/native/observer_plugin.cc:115`; `local/modlock/src/gameinterop/movement_hook.cc:141` | Calls native movement once and records pre/post state without writes. Keep this observer for acceptance evidence. |
| `MovementHook::Install` | `local/modlock/src/gameinterop/movement_hook.cc:158`–170 | Rewrites buttons, CMoveData angles and axes before native `ProcessMovement`. Returning false prevents its post-native origin/velocity/ground overwrite. However, it cannot coexist with the observer in the same module (`:221`) and does not prove inputs reached earlier ability processing. |
| `AbilityInputHook` | `local/modlock/src/gameinterop/ability_input_hook.cc:42`–45, `:50`–78 | Reads the pawn's movement-service held/changed/scroll masks, calls a mutating handler, writes changed masks, then calls native `AbilityThink`. It exposes no axes, view, command number, tick or fraction. It does not itself create a usercommand or run the normal command pipeline. |
| `GameServices::Filter` / `player.press` | `local/modlock/src/wasm/game.cc:688`–725 | Adds a one-use held+changed bit and clears its scroll bit. This is a button pulse helper, with no explicit held-duration/release scheduler or per-command acknowledgement. Bots are explicitly included here; the hook's default excludes them. |
| `Game::Steer` | `local/modlock/src/wasm/game.cc:843`–881; `proto/modlock/wasm.proto:186`–192 | Copies requested command input, then overwrites recorded position, velocity and ground. This replay path violates the measurement contract and must not serve as the runner backend. |
| `ClientCommandHook` | `local/modlock/src/gameinterop/client_command_hook.cc:35`–76, `:91`–96 | Reads console command text at server `Source2GameClients001::ClientCommand`, slot 17. It is authenticated string-command ingress, not a CUserCmd/CreateMove hook and not client-side key-state dispatch. |
| `EngineServer::ServerCommand` | `local/modlock/include/modlock/gameinterop/engine_server.h:40` | Runs the server console parser. There is no evidence that sending `+forward` through this interface creates the intended human client's movement command. |

The detours establish local ordering only: filter → native AbilityThink, and
movement pre-copy → native ProcessMovement → post-copy. Their relative order,
number of calls per tick and relation to command loading are not established.
The movement observer can therefore miss inputs consumed earlier, and an
AbilityThink write may be replaced before movement. Do not bridge this gap with
assumptions about a single callback per simulation tick.

There is also an ABI documentation discrepancy to resolve before using the
ability hook as an injector: `data/game_signatures.txtpb:71` labels the target
`CCitadelBaseAbility::AbilityThink`, while `AbilityThinkThunk(void* pawn)` treats
its argument as a player pawn. The existing memory-layout unit test does not
prove the live target argument type or call order. Verify the target and
controller/pawn identity without writes first.

## Jump and other button mappings

`local/modlock/data/dump/schemas.json:262279` supplies pinned server
`InputBitMask_t` definitions. These define names, not the whole binding behavior:

| Bit | Decimal mask | Pinned name | Human evidence |
| --- | ---: | --- | --- |
| 1 | `2` | `IN_JUMP` | Zero held occurrences in the first human trace. |
| 2 | `4` | `IN_DUCK` | Empirically crouch in the human pilot. |
| 3 | `8` | `IN_FORWARD` | Empirically forward in the human pilot; captured axis also responds. |
| 44 | `17592186044416` | `IN_INNATE_1` | Empirically dash in the human pilot. |
| 48 | `281474976710656` | `IN_MANTLE` | Present in the human jump-key composite. |
| 56 | `72057594037927936` | `IN_ZIPLINE` | Present in the same human jump-key composite. |

Read-only recount of
`local/captures/human-20261018-02/trace.jsonl` found 427 held bit-48 samples,
35 rising bit-48 edges, and no held bit-1 samples. Every rising bit-48 pattern
also contained bit 56. The first isolated edge, sequence 515 / clock tick 4163,
has held=changed=`72339069014638592` (`2^48 + 2^56`), scroll=0 and axes=[0,0,0].
Other edges also include concurrent movement/crouch/attack/dash bits.

`include/modlock/gameinterop/movement_hook.h:66` correctly defines the enum's
ordinary jump bit as 2; it does not identify the user's actual bound jump action.
`src/quake/player_movement.h:27`–31 already distinguishes `+in_mantle` from
console `+jump`. Do not silently reduce the measured human composite to 2 or
declare which member of the composite causes each native action. Verify isolated
bindings and press/hold/release behavior on the same build and configuration.
Use decimal strings or true uint64 storage for all masks.

## Existing engine paths worth probing

The pinned console dump has stronger leads than manufacturing a trajectory:

- `data/dump/console.json:34233`: `in_forcebuttonstate` says it forces a button
  state **when processing usercommands**. It is `developmentonly` and `gamedll`;
  argument syntax, target player, release semantics, current availability and
  scheduling phase remain unverified. It offers no evidenced axes/view support.
- `:31482`: `bot_command <bot name> <command string...>` dispatches a command
  string to selected bots. It is `developmentonly`; this is not proof that it
  supports arbitrary movement commands or deterministic engine-tick timing.
- `:2150`, `:3619`, `:31839`: `bot_mimic`, `citadel_bot_record`, and
  `citadel_bot_playrecording` describe mimicking and replaying player commands.
  Native recorded-command playback could be useful for parity experiments,
  but the recording format, edits, reset effects and command timing need inspection.
- `:3764` and `:3209`: `citadel_bot_test_mode` and
  `citadel_bot_brain_disable_movement` are candidates for suppressing independent
  bot motion. Their effectiveness, ability AI and navigation side effects must
  be observed; setting a convar is not proof of an idle command-equivalent bot.

The old solo pilots demonstrate the limitation directly: native bots traversed
the map with all captured axes/buttons zero, plus hundreds of between-call
velocity changes (`capture/SOLO_PILOT_REPORT.md`). They establish recorder
transport, not valid automated input equivalence.

`EngineHost::Open` maps client.dll for a client launch
(`src/engine_host.cc:142`), and `src/net/listen_boot.cc:43` constructs a normal
insecure connecting client. This makes a client-side investigation feasible in
principle; loading client.dll and its movement hook still does not expose an
upstream command-builder API. No local CreateMove, ProcessUsercmds or RunCommand
injector was found in `src/`, `include/` or `data/game_signatures.txtpb`.
`COMMUNITY_TOOLS_RESEARCH.md` records an older Deadworks ProcessUsercmds lead;
its source is not present locally and has not been validated as a writable seam.

## Concrete next implementation

1. Keep the scheduler/protocol independent of native dispatch. Backend capability
   must say whether it is unavailable, synthetic, experimental or parity-verified.
   Missing live support must fail before arm; fixtures can test scheduling only.
2. Add a **read-only phase diagnostic** before choosing the backend: stamp
   AbilityThink entry/exit and ProcessMovement entry/exit with one monotonic
   callback sequence, engine tick, world, slot/session generation and verified
   pawn handle. Record raw masks before each phase and original-call counts.
   Capture frame state separately. Do not use the server's prior-command field
   as a command identity (`movement_hook.h:28`).
3. Inspect and validate a local-client command-construction hook before prediction
   and network encoding. Resolve native layout/ABI against the pinned game DLLs;
   verify command numbering, button deltas, axes, view and duplicate prediction
   processing before enabling writes. Keep server read-only observation in the
   dedicated host. An early server command hook is an experimental fallback.
4. If testing the existing ability hook first, limit the claimed capability to
   button dispatch. Use one explicitly selected private local pawn, preserve
   identity, instrument every requested/actual mask, and verify ABI/order first.
   This cannot pass full forward/strafe/view requirements. Do not swap out the
   observer or combine two unverified late write points to imply full support.
5. For a verified backend, emit immutable request events plus separate backend
   application records and recorder observations. Include target and actual tick,
   event ID, input state, observation phase and identity. Whole-tick targets are
   earliest scheduling requests, not guaranteed receipt times. Repeated callbacks
   must not replay press edges. Missing callbacks/deadlines fail explicitly.
6. Verify releases using observed edges and subsequent idle masks, including
   abort, timeout and disconnect. A queued release is not a confirmed release;
   lost identity or an unavailable callback must produce an unconfirmed-release
   failure. Preparation interventions stay outside settling/measured intervals.
7. Pass the plan's pulse/release, simple human parity, advanced failure/parity and
   five-repeat gates before collecting automated parameter sweeps. Match Mina
   with Superior Stamina where comparing the saved human baseline; do not call
   those traces an item-free baseline.

The initial harness can be concrete and testable now. The live backend remains
blocked on input ABI/order verification and a command seam that reaches both
ordinary movement and native ability eligibility without any motion overwrite.
