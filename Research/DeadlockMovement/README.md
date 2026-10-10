# Deadlock movement research

Independent tooling for recovering movement parameters and testing behavioral
hypotheses. This folder lives in the game repository for version control but is
outside Unity's Assets/Packages: no Unity installation, scene, or game assembly is
needed. All commands below run from `Research/DeadlockMovement`.

## Install on a new machine

Install Python 3.10 or newer, then:

```sh
python bootstrap.py
python research.py doctor
python -m unittest discover -s tests -v
```

Use `python3` if that is your host's Python command. Windows users can also install
the graphical viewer with `python bootstrap.py --gui`, then open
`.tools/20.0/gui/Source2Viewer.exe`. Neither installer changes PATH, Unity, agent
configuration, nor system packages. Python scripts use only the standard library.

`tools.lock.json` pins Source 2 Viewer 20.0 release assets and their upstream GitHub
SHA-256 digests. Bootstrap detects Windows/Linux/macOS and x64/ARM64, downloads
from the official repository, verifies the archive, and installs under `.tools`.
It can be rerun; verified downloads are cached under `.downloads`. Linux/macOS
execution still depends on the upstream binary's supported OS/runtime libraries.
Only Windows x64 has been exercised locally; other platforms are not yet tested.
CI runs the Python harness on Windows, Linux, and macOS without game files.
The optional synthetic-VPK integration test runs when the pinned CLI is installed;
otherwise it is explicitly skipped.
To upgrade, review a specific upstream release, update its URLs and digests in
the lock file, and repeat the extraction smoke test. Do not replace pinned URLs
with `latest`.

## Find and extract resources

Use your local Deadlock `game/citadel/pak01_dir.vpk`. Its numbered archive chunks
must remain alongside it. The installed game is read only; exports are separate.

```sh
python research.py list --vpk "/path/to/Deadlock/game/citadel/pak01_dir.vpk" --filter "scripts/"
python research.py extract --vpk "/path/to/Deadlock/game/citadel/pak01_dir.vpk" --filter "scripts/abilities.vdata_c,scripts/heroes.vdata_c" --run build-ID-initial --build STEAM_BUILD_ID
python research.py search local/runs/build-ID-initial/exports
```

First list the archive to verify the current paths. Use Source 2 Viewer path
prefixes separated with commas. Extraction fails
if no files are exported. Run IDs cannot be reused: choose a new ID for each run.
The build ID is in Steam's `steamapps/appmanifest_1422450.acf`; record only the
build ID, not the entire manifest (which contains account/machine metadata).

Each run contains exports, stdout/stderr logs, and a manifest with the supplied
build label, UTC time, filter, CLI version/hash, all source archive member hashes,
and exported file hashes. Full archive hashing can take minutes. Finish Steam
updates before extraction and keep the game installation unchanged throughout
the run. The build label is operator-supplied, not automatically authenticated.

Search results retain file paths and line numbers. They are evidence locators,
not a KV3 parser: inherited/default values and equations still require analysis.
The tool does not claim to reconstruct executable gameplay logic.

## Measurement harness

Capture repeated trials for flat sliding at different starting speeds, steering,
up/downhill sliding, dash-slide timing, and slide-jump momentum. Record build,
hero, items, terrain, input sequence, capture method, and uncertainty with each
trial. Keep real captures and measurements in `local/`.

The initial comparison contract is CSV with `time_s,speed_mps`: seconds relative
to the same input event and horizontal speed in metres/second. Convert original
units explicitly and document the conversion; do not assume Source units match
Unity units. Record the method used to derive speed from video or telemetry.

```sh
python research.py compare examples/synthetic-reference.csv examples/synthetic-candidate.csv
python research.py compare local/reference.csv local/unity-candidate.csv
```

This prints RMSE, maximum absolute error, signed mean error, and input hashes.
It interpolates candidate speed at reference timestamps and refuses extrapolation,
NaN, negative speeds, or duplicate/reversed timestamps. Samples receive equal
weight, so use a uniform reference sampling interval for comparable runs.
There is no automatic event alignment or pass threshold: choose tolerances from
measurement uncertainty before claiming a match. The examples are synthetic,
not recovered Deadlock values. No game telemetry capture or Unity exporter is
implemented yet; those depend on the measurement source we select.

## What is committed

Commit scripts, lock files, synthetic fixtures, tests, and reviewed research notes.
`.tools`, `.downloads`, `.venv`, and `local` are ignored. Keep game archives,
DLLs, decompiled resources, captures, Ghidra databases, and machine-specific paths
under `local` (or outside the repository). Do not force-add generated artifacts.
Tool binaries retain their upstream licenses; bootstrap downloads them rather
than redistributing them in Git.

Remote machines clone this repository and rerun bootstrap. They must separately
have access to the required game files for extraction. A Git clone contains the
research workflow, not the Deadlock installation or local research exports.

## Later: executable analysis

Ghidra and REA are deferred until resource inspection produces specific questions
that require compiled-code analysis. They are not required for this first stage.
REA's documented setup is `npx rea-agents setup`; it changes agent registrations,
so this bootstrap deliberately does not run it. Its Windows Ghidra support is
currently described as experimental. Verify its current Node/JDK/provider
requirements and pin versions before adding it to this reproducible toolchain.
Use static copies under `local/binaries` and keep Ghidra projects under `local`.

## References

- [Source 2 Viewer CLI](https://s2v.app/ValveResourceFormat/guides/command-line.html)
- [Pinned release](https://github.com/ValveResourceFormat/ValveResourceFormat/releases/tag/20.0)
- [Slide schema](https://s2v.app/SchemaExplorer/deadlock/server/CAbilitySlideVData)
- [REA](https://github.com/morluto/rea)
- Existing Unity integration points: `Assets/Scripts/Gameplay/Player/Movement/`
  and `Assets/Tests/ReplayHarness/` at the repository root.

## Initial local validation

On 2026-10-10, Windows x64 CLI 20.0 successfully listed and exported
`scripts/abilities.vdata_c` and `scripts/heroes.vdata_c` from installed Deadlock
build 25841406. Local run `build-25841406-initial` contains two decompiled exports
and hashes for 326 source archive members. Slide parameter names were found in
the exported text. Those artifacts are intentionally absent from Git; remote
users reproduce the run from their own matching game installation. This validates
the extraction setup, not any movement equation or a Unity behavior match.
