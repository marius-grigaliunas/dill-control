# 0037 One planet size for nests: radius 1,260 m

*24 October 2026. Built. Narrows 0018.*

## Context

0018 rolls a planet's radius inside its archetype's range. A nest's size
follows its population (0034, 0038), and a nest laid on planets of different
sizes curves differently: spacing, swing reach and what can be seen from the
canopy all change with the radius. Tuning the nest and the planet at once
would leave neither tuned.

## Decision

**The rock archetype's radius is pinned: `RadiusMin` and `RadiusMax` are
both 1,260 m in `planet_archetype_rock.tres`.** The seed still rolls
everything else in 0018's ranges. `bench.gd planets` held at it.

A nest of 25,000 reaches 393 m from its heart, about 18 degrees of arc,
and each vine stands on its own `WorldUp`.

## What it costs

- Every contract's planet is the same size. A range can open again later,
  once the nest is tuned and `bench.gd planets` holds at the new ends (0018).
