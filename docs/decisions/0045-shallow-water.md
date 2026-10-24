# 0045 Water is shallow: you wade and splash, and it is made by flooding a hollow

*24 October 2026. Agreed with the user. Builds on 0026 and 0044.*

## Context

The level tree has always had a Water branch and the project a Water
collision layer and a splash sound, but no water: the lab's swamp and its
coolant stayed in the lab. The user asked for a water brush beside the
terrain and scatter brushes, so water needed deciding first: what it does
in play, and how it is made.

## Decision

**Water is shallow.** Knee- to waist-deep: it slows walking and running,
the deeper the more, to `PlayerWadeShare` at `FullSlowDepthM`; the horde
wades at `HordeWadeShare` and its flow field prices water cells a little
dearer (`HordeFlowCost`), so the crowd goes round a pond when the way round
is short. Stepping in faster than `SplashMinSpeed` splashes. No swimming.
Numbers: `resources/levels/water_default.tres` (`WaterStats`).

**Water is made by flooding a hollow.** The brush's Water mode, or the
bridge's `flood`: a click in a hollow and a level; every cell connected to
it with ground lower than the level gets water, flat at that level, like a
lake. It refuses to spill off the terrain's edge or past 40,000 cells. The
result is real nodes (0044): a `WaterBody` under Water holding which cells
are wet, relative to itself (drag it and the water goes with it), and a
`Surface` mesh saved in `<scene>_water/`. Shape a pond with the terrain
brush, then flood it; to change a pond, remove it (Shift-click, or delete
the node) and flood again.

**Everything asks `WaterBody.DepthAt`**: the player each tick, the horde
once when its ground is made (`IGroundPace`, read from its parallel passes
as an array), the scatter brush so nothing is painted into water. No
physics: water has no collider yet.

**Its look** is `shaders/water_toon.gdshader`: opaque, toon-lit, drifting
ripple bands and a glint, so the ink pass outlines the shore and it costs
no blended overdraw on Iris Xe.

## What was not chosen

- **Deep water and swimming**: a refuge from the horde, but a swim
  controller of its own. Later, if play asks for it.
- **A hazard (brine, coolant)**: the same body could carry damage later.
- **Painting the surface freely**, or **rivers that flow downhill**:
  flooding gives a level that is right by construction; rivers are much
  more work.

## What it costs

- Bullets, blasts and the grapple pass through water: no splashes from
  shots, no collider. A Water-layer surface is the next step if play needs
  them.
- A pond is fixed at its level until it is flooded again; the terrain under
  it can be sculpted after, and the water does not follow.
- Not yet felt in play: the slowdown and the splash are untested by hand.
