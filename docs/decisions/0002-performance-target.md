# 0002 The performance target, and how it is measured

*8 October 2026*

## Decision

**60 fps at 1920x1080 native on an 11th-gen Core i5 with 16 GB and Iris Xe
integrated graphics.** A requirement on the same footing as a crash, carried
over from the lab unchanged. Render scale ships at 1.0; lowering it is a
diagnostic, never how the target is met.

## How

`scripts/debug/bench.gd` on the target machine, unattended. It waits for the
draw count to settle, then reports per phase the worst frame, frames over
16.7 ms, the GPU's own mean and the CPU split from `PhysicsStepProbe`. A mean
alone is not a measurement; it is built to hide the hitch worth finding.

Every comparison is paired (baseline, change, baseline) on a machine that has
been idle. The lab measured the same build at 10.2 ms and then 13.3 ms twenty
minutes apart on a hot laptop.

Godot's `TIME_PROCESS` monitor is not used: read from the bench's `MainLoop` it
reported 114-381 ms a frame against 9-11 ms frames.

## The floor, 8 October 2026

`lvl_pipeline_test.tscn` (a floor, four primitives, one shadowed sun, a
procedural sky, the ink pass), `ink` mode, **on a machine busy with an editor
and a coding session**, so provisional:

| | mean | gpu mean | draws |
|---|---|---|---|
| ink on | 9.7-10.2 ms | 7.3-7.8 ms | 17 |
| ink off | 8.0-8.5 ms | 5.7-6.1 ms | 16 |

The frame is entirely render-bound: physics and scripts together are under
0.2 ms. Two things to re-measure on a quiet machine before quoting either:

- **The ink pass at about 1.5 ms**, against about 0.6 ms recorded in the lab.
- **5.7-6.1 ms of GPU for an almost empty scene**, against 5.3 ms for the lab's
  whole planet idle. The directional shadow and the sky are the first suspects.

Replace this table with a quiet measurement and delete the caveat.
