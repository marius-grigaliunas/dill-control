# 0020 The compound is a seeded layout of authored clusters

*10 October 2026. Extends 0004; supersedes `docs/plan.md`'s "procedural
compounds: not in the slice".*

## Context

A different planet per contract wants a different compound on it.
Generating the compound's shapes is what the lab did (`CompoundModels`) and
what 0004 left behind: nobody could art-direct it. Placing one authored
compound scene everywhere repeats the moment it is seen twice.

## Decision

- **A cluster is an authored scene**: a handful of props arranged by hand on
  flat ground, rooted in a `CompoundCluster` that declares its role (anchors,
  towers, jar, ruins), its footprint radius and its markers (nests, the giant's
  arena, a rearm pad). Everything inside a cluster stays as the designer put it.
- **A `CompoundKit` lists clusters and how many of each**, in the order they
  are fitted (the jar first), with the pad's size and spacing.
- **`CompoundPlacer` decides only where.** As the planet's site chooser it
  runs between generating the ground and levelling it: the flattest of 32
  seeded candidates (least spread of heights across where the pad would go),
  a seeded heading, then each cluster at a random spot on the pad whose
  footprint clears every placed one by the kit's spacing. A cluster that does
  not fit is left out and reported, never squeezed in. The layout is a pure
  function of kit, ground and seed (`CompoundPlacer.Layout`), and
  `seedcheck.gd` holds it to that.
- **The clusters go on the pad inside the planet's build**, right after the
  site is stood on its ground (`IPlanetSiteChooser.SitesPlaced`), so they
  exist before the first physics frame, when the horde stamps what stands in
  its way. Deferred to the end of the frame they arrived after the stamp, and
  the horde routed through them (0 cells blocked to 87).
- **The horde comes from a `HordeTier`**: how many, the mix, whether the dead
  respawn (off for a contract, so the population falls and a clear threshold
  can read it). Its garrison is the compound. The tier writes a copy of the
  shared `HordeStats`, never the shared resource.
- **One kit so far, from the graybox pieces**: the jar court (two nests, the
  arena marker), two to four tower rows, two to four spire rings, one to four
  ruins, on a 160 m pad. It runs before any art exists; art replaces the
  pieces inside the clusters and nothing else.

## Measured

Seeds 1-4 lay out 9-11 clusters, none left out. On seed 1 the horde's ground
build (slopes and the compound's obstacles) is 372 ms, as before; the planet
build with the layout and its instancing 440-720 ms. `bench descent` on seed 1
with the generated compound: GPU 6.4-12.5 ms, the worst frame 39 ms in cruise
(the laptop warm, as in 0019).

## Costs and open ends

- Clusters stand on a plane tangent at the compound's centre (0017's planar
  pad), so a kit's pad radius is bounded by how far the curve allows before
  the pad's edge stands proud: 160 m on 1000-1400 m planets is a few metres.
- Nests are found but the horde still has one garrison, the compound's
  centre. Seeding at each nest is the horde's next step.
- The arena and rearm markers are recorded for Mother Brine and the mission
  and used by nothing yet.
