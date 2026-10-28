# Zen probes

Each probe is one small script that answers a few questions the Zen GPC guide leaves open. Run it on
your Zen, read `TRACE_1`..`TRACE_6` in Zen Studio's Device Monitor, and write the numbers in the
**Zen** column below. Where the Zen disagrees with the engine, the matching `CompatFlags` default
(`engine/Runtime/CompatFlags.cs`) changes, and so does the pinned row in `engine.tests/ProbeTests.cs`.

These scripts are original. They are written from the guide's descriptions, not from anyone's scripts.

**How to run one:** open it in Zen Studio, compile and program it to a slot, load the slot, leave the
controller untouched (unless the header says otherwise), and read the six trace values. Note your Zen
firmware and Zen Studio version in the table.

You can see what this engine answers without a Zen: `gpcrun sim probes/<file>.gpc 3`.

## Probe 0: identifier numbers (start here)

`p0_identifiers.gpc` prints the numbers behind names like `PS4_CROSS`, `TRACE_1` and `TIME_MAX` on the Zen's
screen, 12 per page; press CROSS for the next of 3 pages. Photograph each page and send the photos. If it
doesn't compile on the Zen, send the error instead: it means one of those names doesn't exist.

## Results

Firmware / Zen Studio version: _fill in_

| Probe | Trace | Question | Engine today | Zen |
|---|---|---|---:|---:|
| p1a | 1 | `7 / 0` | 0 | |
| p1a | 2 | `7 % 0` | 0 | |
| p1a | 3 | `-7 / 2` | -3 | |
| p1a | 4 | `-7 % 2` | -1 | |
| p1a | 5 | `7 / -2` | -3 | |
| p1a | 6 | `7 % -2` | 1 | |
| p1b | 1 | `INT32_MAX + 1` | -2147483648 | |
| p1b | 2 | `abs(INT32_MIN)` | -2147483648 | |
| p1b | 3 | `INT32_MIN / -1` | -2147483648 | |
| p1b | 4 | literal `2.5` | 3 | |
| p1b | 5 | literal `1.5` | 2 | |
| p1b | 6 | literal `0.5` | 1 | |
| p1c | 1 | `-16 >> 2` | -4 | |
| p1c | 2 | `-1 >> 31` | -1 | |
| p1c | 3 | `1 << 31` | -2147483648 | |
| p1c | 4 | `1 << 32` | 1 | |
| p1c | 5 | `5 >> 33` | 2 | |
| p1c | 6 | `1 << -1` | -2147483648 | |
| p2a | 1 | `b = a++` (a = 5) | 5 | |
| p2a | 2 | `++a` afterwards | 7 | |
| p2a | 3 | calls made by `0 && bump()` | 0 | |
| p2a | 4 | calls made by `1 \|\| bump()` | 0 | |
| p2a | 5 | argument order (12 = left first) | 12 | |
| p2a | 6 | calls made by `0 ? bump() : 0` | 0 | |
| p2b | 1 | `arr[2]` writes the next variable | 77 | |
| p2b | 2 | `arr[i]`, runtime i = 3 | 88 | |
| p2b | 3 | zero-size array alias | 50 | |
| p2b | 4 | `first[1]` writes `second` | 25 | |
| p3 | 1 | `get_val` after `set_val` same cycle | 40 | |
| p3 | 2 | `get_ival` after `set_val` | 0 | |
| p3 | 3 | `get_lval` = last cycle's final value | 40 | |
| p3 | 4 | `get_lval` sees combo writes | 55 | |
| p3 | 5 | `get_lval` before remap (30) or after (0) | 30 | |
| p3 | 6 | combo writes visible to main this cycle | 0 | |

TRACE_4..6 hold 16-bit values on the Zen, so probes keep large results in TRACE_1..3.

Paste the six numbers per probe back into the chat and they get folded into the flags and tests.

## Still to write

Groups 4 to 7 from the plan: cycle model (`get_rtime`, `vm_tctrl`), combo timing (`wait` quantization,
same-cycle step output, `combo_run` vs `combo_restart`), ranges (`block`, `wait`, `get_ptime` saturation,
first `get_brtime`), and the built-ins described only in words (`sensitivity`, `deadzone`, `stickize`).
