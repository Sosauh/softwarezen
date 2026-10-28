# softwarezen

A Windows runtime for Zen-style GPC scripts on PC: DualSense in over USB, a script in the middle,
one virtual DualShock 4 out to the game. Same `.gpc` source as a Zen, but no Zen hardware and no
console output. PC games only.

Status: engine, DS5 decode, scheduler and virtual pad are written. The engine is tested on Linux.
The Windows device path has **not yet run on real hardware**; your first desktop run is its first test.

## Desktop setup (Windows 10/11)

1. Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
2. Install [ViGEmBus 1.22.0](https://github.com/nefarius/ViGEmBus/releases/tag/v1.22.0) (needed for `passthrough` and `run`).
3. Optional but recommended: install [HidHide](https://github.com/nefarius/HidHide/releases) 1.5.230.0 or later, hide
   the DualSense, and add the built `gpcrun.exe` to HidHide's application list. Without it, games see both pads.
4. Plug the DualSense in over USB, then from the repo root:

```
dotnet build -c Release
dotnet run --project runner -c Release -- monitor
```

## Commands

| Command | Needs | What it does |
|---|---|---|
| `gpcrun monitor` | DS5 on USB | Live stick, trigger and button values in GPC units |
| `gpcrun drift [seconds]` | DS5 on USB | Measures stick drift at rest and suggests a deadzone |
| `gpcrun passthrough` | + ViGEmBus | DS5 to virtual DS4 with no script; prints tick timing on exit |
| `gpcrun run <file.gpc>` | + ViGEmBus | DS5 to script to virtual DS4, with rumble and lightbar back to the DS5 |
| `gpcrun check <file.gpc>` | nothing | Compile and print diagnostics |
| `gpcrun sim <file.gpc> [cycles]` | nothing | Run with an untouched controller and print TRACE_1..6 |

Add `--xbox` to `passthrough` or `run` to output a virtual **Xbox 360** pad instead of a DS4. Many older PC
games (Bully: Scholarship Edition among them) only read Xbox controllers.

With `dotnet run`, put the command after `--`, e.g. `dotnet run --project runner -c Release -- run scripts/anti_drift.gpc`.

**First run order:** `monitor` (does the DS5 read correctly?), `passthrough` (does a controller tester such as
`joy.cpl` show one DS4 that matches?), then `run scripts/anti_drift.gpc`.

## Game lock

`--only <game.exe>` ties `passthrough` or `run` to one game: it waits for the game to start and quits when it
closes, so the script can't be left running after you play. Use it every time, e.g.
`run scripts/turbo_cross.gpc --xbox --only Bully.exe`. On every exit the virtual pad is unplugged and HidHide
cloaking is turned off, so games see your real DualSense again.

Scripts change your input, and online games with anti-cheat can ban for that. There is no automatic
anti-cheat detection (it would also block single-player games that ship anti-cheat), so keep scripts to
single-player and offline games.

## Layout

```
engine/          GPC lexer, parser, compiler, deterministic VM (no Win32; builds and tests on Linux)
engine.tests/    doc-derived vectors, combo timing, diagnostics, determinism, zero-allocation check
hostwin/         DS5 USB HID, ViGEm DS4 sink, deadline scheduler, HidHide wrapper, engine bridge
hostwin.tests/   report codec, drift and bridge tests
runner/          gpcrun console app (the headless engine process)
scripts/         ready-to-use scripts (anti_drift.gpc, turbo_cross.gpc, settings_menu.gpc)
probes/          Zen probe scripts and the results table
```

## Compatibility

Supported: `define`, `enum`, `data()`, typed `const` arrays (1D/2D) and strings, `int` variables and arrays
with slot aliasing, multiple `init`/`main`, `function`, `combo`/`fcombo` with `wait`/`call`, `if`/`else`,
`while`, `do`/`while`, `for`, `switch`, `?:`, `^^`, all compound assignments, `sizeof`, `addr`,
`remap`/`unmap`, and about 90 built-ins: inputs and edges, timing, combos, math, bits, trig, data readers,
rumble, lightbar, polar, SPVAR/BVAR in memory, and the 128x64 OLED (`gpcrun sim` prints it as text).

Every behaviour the guide leaves unclear is a flag in `engine/Runtime/CompatFlags.cs` with a guessed default,
until the probes in `probes/` measure it on a real Zen. Known gaps:

- **Identifier numbers** come from the official Cronus constant tables (guide.cronus.support, 774 values in
  `engine/Runtime/PublishedConstants.cs`). Not published anywhere: `TRACE_*`, `TIME_MAX`, `BUTTON_MAX` (best guesses
  in `Identifiers.cs`; `probes/p0_identifiers.gpc` shows the real ones on a Zen).
- **`sensitivity`, `deadzone`, `stickize`, polar** use reasonable curves, not measured ones.
- **`remap` semantics** (what happens to the source) are a guess.
- **OLED font sizes** other than LARGE (16x26) are a best guess; `probes/p0_identifiers.gpc` helps check them.
- **Out of v1:** Bluetooth, gyro, touchpad, adaptive triggers, haptics, mouse and keyboard, `.gbc` bytecode.

Not affiliated with Cronus or Collective Minds.
