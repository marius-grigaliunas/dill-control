# 0036 Only valid vines: a seed that fails is rolled again from the seed and an attempt number

*24 October 2026. Built. Builds on 0018 and 0033.*

## Context

A kind's settings are ranges rolled per seed. Some rolls grow vines that
cannot be: a leaf through the stalk, an arm into the ground, pods with no
room for a pickle to hang, a stalk that sags or kinks. With a nest of 117
vines no one looks at before a player does, a bad roll would reach play.
Two answers were possible: reject a bad vine and pick another seed (the
seed no longer means one vine), or keep rolling from the seed itself.

## Decision

**A vine is judged by `VineValidator` and, if it fails, grown again from
`VineGrower.AttemptSeed(seed, attempt)`. Attempt 0 is the seed itself, so a
seed that grows a valid vine keeps it, and a seed always means one valid
vine, on every machine.**

- **Shape:** every voxel joined to the ground, nothing below it
  (`GroundClearanceM`), leaves clear of the stalk, arms and each other
  (`LeafGapM`), pods with a pickle's clear drop, no axis over 256, bodies
  under `BodiesMax`, under `MaxPodsLeftOut` and `MaxLeavesLeftOut` dropped.
- **Plausibility:** the stalk tapers (`StalkWidening`), arms do not grow
  upward (`ArmGrowth`), lean is bounded (`MaxLeanDeg`), leaves near level
  (`MaxLeafTiltDeg`).
- **The spine's load:** the flex spine (0032) at rest, worked out, not
  simulated: no joint kinked over `MaxKinkDeg`, tip sag under `MaxSagM`.
- **Jolt never decides a roll.** A physics test was planned and left as a
  check (`vinecheck.gd -- settle`), not a gate: a simulation's result is
  not guaranteed the same on every machine, and the roll must be.
- Up to `MaxAttempts` (16); if none passes, the last is used and flagged.
- The generator itself leaves out pods with no drop and leaves with no room
  before judging.
- Thresholds: `resources/foliage/vine_bake.tres`.

The range lab shows each kind's rolls before any re-roll, and
`vinecheck.gd -- rejections N` reports how often each kind is valid and why
not. A kind that is often re-rolled has poor ranges, and is narrowed from
that evidence rather than hidden by the re-roll. On 24 October: fruiting
93 %, heart 100 %, weeping 95 %, leafy 53 % valid on their own roll.

## What it costs

- A failed seed grows two or more vines' worth of time (20-80 ms each).
- Re-rolling hides poor ranges unless someone reads the rejection rates.
- A rule changed in the validator changes which vine some seeds mean:
  golden vines are re-recorded and baked vines re-baked.
