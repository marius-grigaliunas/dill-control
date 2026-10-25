# 0048 Vulkan on Windows, not D3D12

*25 October 2026. The user asked for whichever is better; measured and
switched the same day. Changes the renderer in CLAUDE.md's verified
environment.*

## Context

The project ran Forward+ on D3D12. On the target machine (i5-1145G7, Iris
Xe, driver 32.0.101.7088) a D3D12 start hung in its first second about one
time in three: the main thread stopped in native code, the window stopped
responding, nothing was logged, at `475ac45` as much as after it, windowed,
fullscreen and run from the editor alike. Vulkan did not hang in 12 starts.

## Measured

Paired, alternating, on the target machine plugged in, 40 s cooling between
runs (`bench.gd`, `--rendering-driver`):

| | D3D12 | Vulkan |
|---|---|---|
| Contract level, idle: mean / GPU | 7.7-8.3 / 5.9-6.2 ms | 7.4-7.7 / 6.0-6.2 ms |
| Horde 25,000, arriving: mean / over 16.7 ms | 16.7-17.7 ms / 169-312 | 13.8-14.0 ms / 44-51 |
| Horde, crowd on you | 15.7-16.3 ms / 116-124 | 13.6-13.8 ms / 19-24 |
| Horde, rifle into it | 18.8-18.9 ms / 259-279 | 16.2-16.3 ms / 52-60 |
| Horde, holding ground | 20.8-20.9 ms / 231-235 | 16.9-18.8 ms / 59-232 |

Vulkan is as fast idle and 1.5-3.5 ms faster under the horde, with far
fewer frames over budget. None of these eight starts hung on either.

## Decision

`rendering/rendering_device/driver.windows` is Vulkan, Godot's default, so
`project.godot` no longer names it. Benches and screenshots are Vulkan's
from here; numbers recorded before today are D3D12's and are compared with
care.

## What it costs

- Every recorded frame time before this is on the other renderer.
- D3D12 stays one flag away (`--rendering-driver d3d12`) if a driver update
  turns the table.
