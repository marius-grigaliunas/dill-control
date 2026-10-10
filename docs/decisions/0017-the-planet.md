# 0017 The planet: ported at the origin, a far shell and chunk levels, sites for what is authored

*10 October 2026. Builds on 0008. The first step of the generated-contract
plan: a planet on a fixed seed, before seeds, biomes and contracts.*

## Context

The slice needs a planet, and the arrival from orbit and the mission cannot
exist without one. The lab's planet (`scripts/planet/`, about 2,760 lines)
was one class that generated heights, drew chunks and dug craters, and its
`SpikeDirector` streamed it, placed things on it and turned blasts into
craters. From space it was a jagged disc: chunks were culled to the viewer's
horizon and there was nothing else to draw.

Everything ported since was written on the flats, with up as +Y.

## Decision

- **The planet sits at the world origin.** Every query treats a world point
  as a direction from the centre, as the lab's did and the horde's ground
  must. That keeps every lab query and needs no floating origin: the ship's
  orbit at about 2.5 radii is inside 5 km. Other bodies will be scenery.
- **`PlanetHeightfield` is pure data**: generation on the job threads,
  height, normal, slope, surface transform, heightfield raycast, craters.
  **`PlanetTerrain`** is the node: chunks, streamed colliders, a point-gravity
  `Area3D`, craters from `EventBus.AreaBlast`. Shape is a `PlanetSpec`;
  everything else (spacing, streaming, craters, horde slope) is
  `PlanetStreamingStats`. Ridged noise is added to the lab's rolling noise,
  mixed by `RidgeShare`.
- **`PlanetFarShell`**: the whole planet in six coarse meshes, one per face,
  always drawn, each vertex at the lowest ground it covers less 1.5 m so the
  near chunks win the depth test wherever both draw. Six draw calls give the
  whole silhouette from orbit and fill everything past the near chunks'
  draw distance (1.8 km).
- **Chunk levels of detail.** Each chunk's mesh carries coarser index grids
  over the same vertices (every 2nd, 4th and 8th), and Godot picks one by
  screen-space error. A crack between two levels shows the far shell a metre
  or two under, in the same colours.
- **`PlanetSite`** is how authored things meet generated ground. A site is
  placed roughly in the editor; at build the planet levels a pad there into
  the *tangent plane* (a spherical pad falls 8 m away by 140 m out on a
  1200 m planet, and a cluster built on flat ground would float), stands the
  site on it upright with its heading, and builds the ground under it now.
  A site may keep its colliders wherever the camera is, so the compound's
  rubble has ground to land on while nobody watches.
- **The horde's ground comes from a provider.** `HordeDirector` takes a
  `GroundProvider` (`IHordeGroundProvider`); the planet's is `PlanetGround`,
  a cube-sphere grid of eight-neighbour cells over the heightfield, with
  slope costs back and authored props stamped round the garrison (terrain
  colliders excluded). No provider is the flats, unchanged. The eye's height
  and direction now reach the horde's horizon cull.
- **`WorldUp`** answers "which way is up here" for code with no body to ask
  `GetGravity()`: dust, the debris ground ray, rubble heaps (now stood upright
  where they form), a crush blast's up. Straight up on the flats; radial while
  a planet exists. `HordeStats.Gravity` stays a magnitude: its direction was
  always the ground's up.

## Measured

`lvl_planet_test`: the flats' compound, downtown and allies on one site at the
top of a 1200 m, 22 m-relief planet (864 chunks of 33², 4.91 m spacing). Build:
heightfield, pads, far shell and the ground under the sites, 410-560 ms; the
horde's ground (98,304 cells of 14.7 m, slopes and props), 310-360 ms.

**The laptop was hot for every run here**: an unchanged baseline drifted 3 ms
of GPU between runs. Read the pairs, not the absolute numbers.

Chunk levels, `bench planet`, before and after (separate runs, the second
hotter):

| phase | gpu, full detail | gpu, with levels |
|---|---|---|
| surface | 8.4 ms | 11.5 ms |
| 300 m up | 14.1 ms | 12.8 ms |
| 1 km up | 13.0 ms | 8.5 ms |
| orbit, 2.4 km | 6.0 ms | 6.5 ms |

Height over the surface cost 5.7 ms of GPU at 300 m and now costs 1.3 ms.
`bench planetab` before the levels: the near chunks were 1.5 ms of GPU on the
ground and 8 ms at 300 m; the far shell under 0.1 ms; the shader's grid and
hatching within noise.

The same crowd on both grounds, `bench horde 25000`, back to back:

| phase | flats mean / over / gpu | planet mean / over / gpu |
|---|---|---|
| arriving | 15.4 / 106 / 12.3 | 16.0 / 118 / 11.2 |
| crowd on you | 15.3 / 82 / 12.0 | 15.6 / 68 / 10.8 |

The planet is at the flats' cost. The horizon cull draws 7,500 agents to the
flats' 12,500; the planet's ground costs the horde about 1 ms more CPU (a cube
locate per agent per step) and physics about 0.8 ms more. The lab's gap
(16-17 ms against 11) does not reappear.

## Costs and open ends

- **Flow cells, revised the same day: 8 m, searched 700 m out.** At 15 m the
  compound stranded nobody and the crowd pressed in several times thicker
  than on the flats. 8 m cells (421k on a 1350 m planet) match the flats'
  routing, and the search stops at 700 m of path cost from the hunt point,
  past the horde's draw distance and seed ring; beyond it agents walk
  straight at the hunt point over open ground. The flow build went from
  40-55 ms (15 m, whole planet) to 11-23 ms, the flats' 15 ms; stranded
  agents 0 to about 180 (flats 135-290); the crowd near the player builds
  about half as fast as at 15 m, still faster than on the flats. The horde's
  ground takes about a second to build behind the loading screen, most of it
  the slope stamp over every cell.
- **The pickles were drawn a planet's radius up in the sky** until the
  horde's multimesh instances (and the brine's) were made top-level: their
  buffers hold world positions and their parent, the director, stands at the
  garrison. On the flats it stood at the origin and nobody could tell.
- Colliders exist round the camera and round sites that keep them; a body
  anywhere else falls through. The lab froze debris that left the ring; here
  nothing yet does.
- The test level's sky and fog are the flats'. They only look right at the
  top of the planet, and from altitude the fog swallows the planet whole. The
  atmosphere and sky are the system step's job.
- Over budget in fights on both grounds, as before; that is the horde's and
  the weapons', not the planet's.
