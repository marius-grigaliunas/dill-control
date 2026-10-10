# 0018 A contract is a seed; planets roll inside authored ranges; biomes are presets

*10 October 2026. Builds on 0017. Supersedes `docs/plan.md`'s "procedural
planets: not in the slice" for the planet's shape and look; the compound is
the next decision.*

## Context

Replayability asks for a different planet per contract. Rule 1 (0004) asks
that a designer, not a generator, decides what things are. The two meet if
the generator only rolls inside bounds a designer authored.

## Decision

- **A contract is one 64-bit seed.** `SeededRandom` (SplitMix64) is the only
  random source generation uses: never `GD.Randi` (global, shared with every
  other roll) or `System.Random` (not promised stable across .NET versions).
  Each stage takes a named stream, `SeededRandom.Stream(seed, "planet shape")`,
  so adding a roll to one stage never reshuffles another. The same seed builds
  the same planet in every process (checked: `seedcheck.gd`, and the sweep's
  planets match it).
- **`PlanetArchetype`**: a kind of planet as ranges (radius, relief, noise,
  ridges, craters, gravity), rolled into a `PlanetSpec`. One so far, Rock:
  1000-1400 m, 14-32 m relief, 0-0.7 ridged, 0-40 old craters.
- **Old craters are ground.** Pressed in at generation, before the base
  heights are taken, so the dig floor measures from them.
- **`PlanetBiome`**: an authored look (ground colours, sky, fog, sun colour
  and elevation over the compound), shifted by the seed within a hue and
  value range the biome declares. Brine Flats, Rust Dunes, Vinegar Frost.
  Applied to the terrain material, the environment and the sun; nothing else
  changes with the biome.
- **Gravity does not vary by default.** The line, the jump and the bike were
  tuned at 9.8; a planet that changes how they feel is a choice to make on
  purpose. The archetype has the range; it is 9.8 to 9.8.
- **Ranges are capped by the bench.** A seed that misses 60 is a bug, so a
  bound widens only after `bench.gd planets` holds at it.
- **The horde's grid is rounded, not a power of two.** The lab's was, for a
  wetness texture that stayed behind, and `NearestPo2` rounds up: a 1350 m
  planet got 393k cells to a 1200 m planet's 98k, a 1.4 s build and four
  times the flow. Cells are now 15 m on every planet.

## Measured

`bench.gd -- lvl_planet_test planets 10 1`, ten seeds back to back. **The
laptop throttled through the run** (four minutes of load, no rest; the last
seeds' every section reads 30-60 % over the first's, physics and render
alike), so read across seeds, not against the budget.

| | range over ten seeds |
|---|---|
| radius | 1090-1390 m (726-1176 chunks) |
| planet build | 355-765 ms |
| horde ground | 227-539 ms |
| gpu on the ground | 10.2-13.3 ms |
| gpu 300 m up | 13.2-15.4 ms |
| gpu 1 km up | 9.0-10.8 ms |
| gpu from orbit | 6.4-8.5 ms |

No seed stands out from the others; the spread follows when in the run it
came, not its shape. Streaming went from 0.6-0.9 ms a frame at 1,176 chunks to
dot products against cosines and cached fields (two arccosines and two engine
reads a chunk a frame were most of it).

## Costs and open ends

- The sweep needs repeating on a rested, plugged-in machine before the ranges
  are called proven, a rest between seeds if need be.
- 300 m up is still the planet's dearest view, about 3 ms of GPU over the
  ground. `LodErrorScale` is the knob.
- The test level's sky, fog and sun direction only suit the top of the planet,
  where its site is (0017). The system step replaces them.
