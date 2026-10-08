# 0008 The horde stands on an interface, and the flats come first

*8 October 2026*

## Context

The lab's horde (`HordeManager`, 3,400 lines) was written against the planet.
Up was the position normalised, height was the distance from the centre,
spawns were scattered over a sphere, cells came from the cube-sphere, and the
far cull was the horizon. The flow field searched the cube-sphere grid
directly.

The plan has the planet at M4. The horde (M3) and the weapons need a level
before then, and a focused one is easier to tune and measure than a planet.

## Decision

- **`IHordeGround`**: up at a point, the ground under a point and its flow
  cell, scatter in a ring, the bounds, and a horizon radius (zero for none).
  Those were every place the horde knew the ground's shape. Everything else is
  the lab's code unchanged.
- **`IFlowGraph`**: cells, neighbours, step lengths and tangents. The lab's
  `FlowField` searches it, copied into flat arrays once at construction.
- **`FlatGround`** implements both: a square of level ground, 6 m cells, eight
  neighbours. Cells something authored stands in are blocked by one physics box
  query per cell at load, so the horde routes round whatever a designer placed.
  A blocked cell points at its cheapest open neighbour, so an agent pushed into
  a pillar's footprint walks out.
- **`lvl_horde_flats`** is the level: 1.2 km of ground, a graybox compound of
  pillars, columns and blocks, the player and a `HordeDirector`.
- With no horizon on the flats, a draw distance culls instead, set inside the
  depth fog's end so nothing visibly pops.

At M4 the planet's terrain implements `IHordeGround` and its grid implements
`IFlowGraph`, and the horde does not change.

## Measured

`bench.gd -- res://scenes/levels/lvl_horde_flats.tscn hordeab 25000` on the
target machine, the crowd arrived and on top of the player, 6 s phases:

| phase | mean | worst | over 16.7 | gpu mean |
|---|---|---|---|---|
| base | 10.9 ms | 28.1 ms | 1/553 | 8.5 ms |
| flow rebuild off | 10.5 ms | 15.8 ms | 0/570 | 8.2 ms |
| base | 10.7 ms | 20.9 ms | 1/560 | 8.4 ms |
| horde on one thread | 13.4 ms | 30.8 ms | 5/449 | 7.4 ms |
| base | 10.8 ms | 18.1 ms | 1/558 | 8.6 ms |

About 12,500 agents drawn, 5,000 stepped a frame, 96 hit proxies. The flow
build is 10-12 ms of wall time on a worker every half second, sharing cores
with the horde's own passes; it costs the main thread nothing measurable.

The lab put the same fight on the planet at 16-17 ms. The difference is the
terrain, not the horde.

## Costs and open ends

- The flats are flat. Slope cost in the flow field went with the planet's
  terrain and comes back with it.
- One frame in about 550 is still over budget, worst 20-35 ms. Not yet found.
  A bench printing status lines twice a second, or the perf recorder, makes it
  several times worse, so read hitches from a quiet run.
- Spitters have nothing to spit until the shot pool comes across with the
  weapons' impact effects; their requests are dropped.
