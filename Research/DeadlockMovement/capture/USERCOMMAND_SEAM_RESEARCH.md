# Upstream command seam research — 19 October 2026

## Immediate implementation choice

Use the local **client's normal console input commands through VConsole** for
the first stand/walk/release/jump/dash probes. This avoids rewriting native
movement or ability state and leaves the server's read-only recorder intact.
It is a coarse dispatch backend: wall time controls transmission, and recorder
ticks establish the actual input schedule. Do not label it deterministic or
engine-tick scheduled. Successful dispatch and successful native actions must
be observed separately.

The live smoke test belongs to the parent implementation. This investigation
did not launch a game or send any command. It establishes source-supported
transport and an upstream native candidate; neither is live validation.

## VConsole packet and endpoint

Two pinned primary implementations agree on command framing:

- [Python client, commit 1d74ddae6fb6ebf6d679374bdcc65aed6a3cf7f2](https://github.com/oxijoined/vconsole-python/blob/1d74ddae6fb6ebf6d679374bdcc65aed6a3cf7f2/vconsole_client.py)
- [C++ command builder, commit 260746d06e13cd7d4a92ad0bb6cfb8b703eb6884](https://github.com/theokyr/CS2RemoteConsole/blob/260746d06e13cd7d4a92ad0bb6cfb8b703eb6884/libvconsole/src/vconsole.cpp)

| Offset | Size | Command packet field |
| --- | --- | --- |
| 0 | 4 bytes | ASCII `CMND` |
| 4 | 4 bytes | Version bytes `00 D4 00 00` |
| 8 | uint16 big endian | Total packet length, including header and terminating NUL |
| 10 | uint16 big endian | Handle `0` |
| 12 | variable | UTF-8 command followed by NUL |

Equivalent encoding is
`struct.pack('>4sIHH', b'CMND', 0x00D40000, 13 + len(encoded), 0) + encoded + b'\0'`.
Use `sendall`, bounded commands and exact-length TCP reads. Incoming packets
use the same 12-byte header; `PRNT` text begins at body offset 28. Keep the raw
packet log. The Python client connects directly and can send a command without
an application handshake.

The Python repository's [README](https://github.com/oxijoined/vconsole-python/blob/1d74ddae6fb6ebf6d679374bdcc65aed6a3cf7f2/README.md)
describes Deadlock support, default loopback TCP port 29000, and `-vconsole`.
Use `-insecure` for this private test client. Its source comments conflict on
whether old `D2` is accepted; use the consistent `D4` builder and verify locally.
Socket send completion is not engine execution acknowledgement. Confirm an
`echo` nonce in returned output, then confirm movement inputs in the recorder.

Local evidence is stronger for endpoint selection than assuming port 29000:
`local/modlock/src/host_app/windows/vconsole_port.cc` retries an occupied bind
on an ephemeral port. Check listening socket ownership against the launched
client PID. The dedicated host's flags include `-novconsole`
(`src/net/listen_boot.cc:28`), so do not remove that flag just to find an input
endpoint. Send movement commands to the client, not the dedicated-server parser.

No primary evidence found here establishes that Source 1's plaintext
`-netconport` protocol works in this Deadlock build. Prefer the binary protocol
above and verify the `echo` probe before any held input.

## Tiny probe contract

The proposed command names must first be recognized by the installed client.
Use bounded isolated pulses, release all relevant commands at the beginning and
in cleanup, and preserve the captured masks rather than presuming the mapping.

| Probe | Candidate client command | Evidence required |
| --- | --- | --- |
| Stand | Release all runner-owned inputs, then settle | Stable pawn, observed zero axes/buttons, ground and resources available |
| Walk/release | `+forward`, then `-forward` | Forward axis/button appear, release observed, native acceleration/deceleration |
| Jump | `+in_mantle`, then `-in_mantle`; compare combined zipline input separately if needed | Actual press/release masks and normal airborne transition |
| Dash | `+in_innate1`, then `-in_innate1` | Command accepted, observed bit 44, native dash state and resource change |

The first human trace contains a bit-48 plus bit-56 jump-key composite; this
does not establish that both commands are needed for an ordinary jump. Read-only
local configuration inspection on 19 October found `+in_mantle; -in_mantle` bound
to SPACE/wheel, and `+in_mantle` to MOUSE2 in the current local user key config.
This file was not edited and its current contents are not proof of the older
capture's bindings. Compare the live isolated result before making a default.

Record sent wall timestamps and observed engine ticks as distinct fields. Log
unknown-command/version errors and abort on missing observed edges. A generic
`echo` confirms command parsing but does not associate a command with a particular
simulation tick. Use recorder transitions for measured timing. An emergency
release that was only transmitted remains unconfirmed until observed; if the
owned client must terminate, retain that failure explicitly.

## Concrete native fallback: ProcessUsercmds

Pinned Deadworks has a writable upstream command-batch hook:

- [Hook implementation](https://github.com/Deadworks-net/deadworks/blob/18595a33b4d0b0a5ffc7d7f389fad501449d78ed/deadworks/src/Core/Hooks/ProcessUsercmds.cpp)
- [Hook declaration](https://github.com/Deadworks-net/deadworks/blob/18595a33b4d0b0a5ffc7d7f389fad501449d78ed/deadworks/src/Core/Hooks/ProcessUsercmds.hpp)
- [Signature database](https://github.com/Deadworks-net/deadworks/blob/18595a33b4d0b0a5ffc7d7f389fad501449d78ed/config/deadworks_mem.jsonc)

The ABI is `void* __fastcall(void* controller, void* cmds, int numcmds,
unsigned char paused, float margin)`. The implementation serializes commands,
allows protobuf edits, reparses modified bytes, then returns the original
trampoline result with the original arguments. It assumes native stride `0xA8`
and an embedded protobuf at `+0x10`; those are reverse-engineered assumptions,
not schema-resolved storage. This is an ingress batch hook, so command arrival
time is still distinct from subsequent simulation time.

The Windows pattern is:

```text
48 8B C4 44 88 48 ?? 44 89 40 ?? 48 89 50 ?? 53
```

A read-only scan of the installed `game/citadel/bin/win64/server.dll` found
exactly one raw-file match at `0x1b51590`. This is **not an RVA** and must not be
used as a loaded-function address. File size: 63,863,960 bytes. SHA-256:
`8c67dc79f082a45a69c5eac99bd4cfd9e965d74154f3ce0bdf30a224158b4293`.
The candidate's opening bytes were:

```text
48 8b c4 44 88 48 20 44 89 40 18 48 89 50 10 53
56 48 81 ec d8 00 00 00 44 8b 91 e8 04 00 00
```

The match is a concrete function candidate, not proof of all native layout or
protobuf ABI assumptions. A diagnostic can preserve the original call exactly
once, verify controller identity, bounds/counts, pause state and command content,
and compare its downstream observations before any mutation is enabled. Do not
copy the upstream unchecked pointer arithmetic or ignore parse failures.

The pinned [base usercommand schema](https://github.com/Deadworks-net/deadworks/blob/18595a33b4d0b0a5ffc7d7f389fad501449d78ed/managed/protos/usercmd.proto)
contains command/tick fields, a uint64 button triple, view angles, three axes,
subtick moves, pawn handle and other metadata. The
[Citadel schema](https://github.com/Deadworks-net/deadworks/blob/18595a33b4d0b0a5ffc7d7f389fad501449d78ed/managed/protos/citadel_usercmd.proto)
wraps that base and adds ability/camera fields. Preserve unknown fields and
untouched metadata. A real injector must understand held/changed/subtick
consistency and validation/CRC behavior before accepting an edited command.

Local Modlock links its own protobuf fork; treating an engine-owned message as
its `google::protobuf::MessageLite` requires a verified binary ABI. A unique
function pattern does not establish that ABI. The normal-console backend is
the concrete initial probe route while this native integration is verified.
