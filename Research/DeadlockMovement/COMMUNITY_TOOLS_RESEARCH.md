# Deadlock movement community and instrumentation leads

Researched 2026-10-13. Web/source inspection only; nothing installed or tested against our game build. This supplements RECONSTRUCTION_PLAN.md.

## Most useful source projects

### Modlock: native movement instrumentation candidate

[Repository](https://github.com/paralin/modlock), MIT. Inspected revision `67478b9bbc54ff1f94ccfdb5f9ce0e08dc6f6418`.

- [Movement hook interface](https://github.com/paralin/modlock/blob/67478b9bbc54ff1f94ccfdb5f9ce0e08dc6f6418/include/modlock/gameinterop/movement_hook.h) describes native movement steps with position, velocity, view angles, movement axes, button masks, ground entity, ticks and fractional timing.
- [Implementation](https://github.com/paralin/modlock/blob/67478b9bbc54ff1f94ccfdb5f9ce0e08dc6f6418/src/gameinterop/movement_hook.cc) reads native movement data before calling the original function. Its fixed movement-data offsets are attributed to a September 18, 2026 build. This is source evidence for an instrumentation approach, not validation of those offsets on our installation.
- The callback is pre-step, includes client prediction replays, and is capable of rewriting movement. An observational recorder must leave inputs/results unchanged and add a distinct post-native sample if needed. The header warns that the server's stored command number is the previous processed number, not a valid identity for the current step.
- The repository also contains custom Quake movement and a checkpoint race example. Those are replacement gameplay, not recovered Deadlock equations.

Assessment: strongest low-level lead for complete input/state capture. Validate layout, timing semantics, prediction duplicates, and unchanged baseline behavior in a controlled local environment before adopting it.

### Deadworks and LockTimer: C# server capture candidate

[Deadworks](https://github.com/Deadworks-net/deadworks), MIT; inspected revision `18595a33b4d0b0a5ffc7d7f389fad501449d78ed`.

[ProcessUsercmdsEvent.cs](https://github.com/Deadworks-net/deadworks/blob/18595a33b4d0b0a5ffc7d7f389fad501449d78ed/managed/DeadworksManaged.Api/Events/ProcessUsercmdsEvent.cs) exposes parsed player command batches, player slot, pause state and network margin. The native hook runs before command processing and uses fixed native layout assumptions. Command arrival must not be equated with the resulting simulation-state timestamp.

[LockTimer](https://github.com/deadlock-api/deadlock-server-plugins/tree/212ff574f9e15a2d6045057061c33c3bbc476b90/gamemodes/LockTimer) supplies a concrete server plugin using pawn position for timed zones and absolute velocity for speed display. Its README declares MIT; the containing repository has no detected repository-wide license.

- [SpeedHud.cs](https://github.com/deadlock-api/deadlock-server-plugins/blob/212ff574f9e15a2d6045057061c33c3bbc476b90/gamemodes/LockTimer/Hud/SpeedHud.cs) computes horizontal speed from velocity X/Y and displays integer world units/second.
- [LockTimerPlugin.cs](https://github.com/deadlock-api/deadlock-server-plugins/blob/212ff574f9e15a2d6045057061c33c3bbc476b90/gamemodes/LockTimer/LockTimerPlugin.cs) polls every 100 ms and times runs with Environment.TickCount64. Its comment reports earlier per-frame interop problems. The README's SQLite description does not match this revision's YAML/metrics implementation; inspect code rather than assuming README accuracy.

Assessment: useful starting example for a standalone C# recorder, but the timer and display are not precise trajectory telemetry. A recorder needs simulation timestamps, full-precision vectors, input association and verified sampling phase. Test per-frame performance before choosing this route.

### DeadlockMovementCfg: repeatable practice setup

[Repository](https://github.com/BAMcSH/DeadlockMovementCfg), inspected revision `d788f2141cbc1d3f2aaaf4a45044bf803fa4b287`. No license detected.

The inspected `Deadlock/poswatch.ps1` watches console output for getpos_exact results and writes a setpos command to a restore config. `pos_tools.cfg` supplies save/restore and testing controls. Useful implementation evidence for a console-log bridge, but this script restores position only: it is not a complete velocity/stamina/timer snapshot or continuous trajectory recorder. README key bindings differ from the actual config.

## HUD mods

- [Strafer, by TharinEvra](https://gamebanana.com/mods/725040): air-strafe compass using minimap and speedometer data. Author reports zero-gravity calibration; credits Kered13 for the strafe-angle model. This is a promising source of candidate air-control hypotheses. No public source repository found. Listed license is CC BY-NC-ND, so do not assume reusable code rights. HUD update rate is not simulation cadence.
- [MapList](https://gamebanana.com/mods/667277): custom-map browser and speed display. Older versions documented position/speed saves and route timing, but the latest listed update removes practice tools because of a settings crash. Inspect exact version before relying on those features. No public source repository found.

## Community, maps, and server leads

- [JumpStart / Deadlock Movement Discord](https://discord.com/servers/deadlock-movement-1418134156705271820): public community page explicitly covers movement techniques, routes and custom movement maps. Good place to locate experiment authors, map sources and unpublished tools; private channels were not inspected and nobody was contacted.
- [Jump Control](https://gamebanana.com/mods/669521) and [AIO Movement Practice](https://gamebanana.com/mods/636058): concrete training-map leads. Author description for Jump Control, also [displayed by Mod Manager](https://deadlockmods.app/mod/669521), says it edits hero data. Check all overrides before treating any custom map as baseline Deadlock movement.
- [Lockify](https://lockify.gg/): encountered as a movement-training/ghost-server lead, but its official site returned a maintenance page. Public source and current service availability were not established. Keep as a follow-up lead, not the capture dependency.
- [Deadworks](https://deadworks.net/) provides a custom-server ecosystem; no specific currently running movement server was verified.

## Effect on the reconstruction plan

Add an instrumentation feasibility branch alongside the demo pilot. Try the same short baseline sequence through a server recorder, compare state to demo/video, and quantify input association and sampling phase. Prefer Deadworks for a C# prototype if its hooks are adequate; investigate Modlock when native movement-step access is needed. Retain native gameplay and verify the tool has not enabled replacement movement.

Use Strafer's calibrated model as a hypothesis to test, community maps as controlled geometry only after auditing overrides, and save/restore configs as setup helpers. None of these findings establish a complete reusable implementation of Deadlock movement or a verified turnkey trajectory exporter for our build.
