# 0055 The tutorial is handbuilt on the planet: `lvl_tutorial`, a baked nest, a pod drop and clones

*30 October 2026. Built; asked for by the user. Replaces 0049 for the onboarding level. Amends 0033 (with 0056).*

## Context

`lvl_nest_intro` (0049) was a generated nest on the planet: nothing in the
editor but an empty tree, so the user, who works visually, could not see it or
move a thing. A first rebuild on a sculpted flat ground fixed that and lost the
planet, which every decision assumes (0017, 0037). The tutorial is the first
level a player sees, so it is on the planet, and built by hand.

## Decision

- **`lvl_tutorial` is on the planet, pinned.** It uses the contract level's
  `PlanetTerrain` and `SolarSystem`, with no ship, no bike and no compound.
  `SystemBuilder.PinToFallbackSeed` makes it the same planet every time, whatever
  contract the session holds (a 1,260 m rock, Brine Flats, seed 1).
- **The map is real nodes**, as far as a planet allows (rule 9): the baked nest
  `prp_nest_intro_1789143419_1000` (16 vines, 1,015 pods) on `Props/NestSite`, a
  `PlacedNestSite` (0056); five hooks (two tall trees, three shrubs) on
  `Props/HookSite`, another `PlacedNestSite` with no pods, so a first shot cannot
  wake the colony; `Spawns/LandingSite`, a `PlanetSite`. Each is placed roughly
  and set on the curved ground when the planet builds. Move, swap or delete any of
  them in the editor. The planet itself is not in the editor view.
- **The pod drop is the opening** (0054). `PodLanding` drops every body in
  `prp_landing_pod` at `LandingSite`, whose -Z is the way the rider faces out. The
  rider looks out of the pod's camera, turns their head with the mouse while it
  falls, feels it land, waits `ExitDelay` and steps out. `DropPod` falls along the
  site's own up, not the world's, so it works on a planet. `prp_landing_pod`
  replaced `prp_drop_pod`, which is deleted; the mechs' part-drops use it too.
  The cabin is boxes: no art exists (0054).
- **Clones are built** (0053). `CloneBank` reads the player's health; at zero the
  body is stowed, "BODY LOST" shows while the next prints (`CloneRules.PrintSeconds`),
  and `LevelRoot.RespawnPlayer` makes a new body which `PodLanding` drops in a pod
  the same way. Three bodies; losing the third voids the contract and `R` starts it
  again (there is no ship to wake on yet). Nothing the body carries is lost.
  `CloneHud` shows a pip per body and the lab flashes (0054): unexplained white cuts
  and green bars as each body begins its fall, deliberately wordless.
- **The prompts** are `guide_tutorial.tres` (`IntroGuide`), the old flow without the
  jar; the first step lasts until the rider steps out.
- **Not built:** extraction by pod, the queen, the minigun gift (0051), the
  descent's art, sound. `lvl_nest_intro`, `NestLandmark` and its preset stay on disk.

## Measured

Idle bench on the target machine: 11.5 ms mean, 15.2 ms worst, 0 of 525 frames over
16.7 ms, GPU 10.1 ms, 207 draws. The opening, the clones (three bodies, then void)
and a woken colony (778 dropped in 22 s) were run in the real game and photographed
from the rider's eyes (`tutorialshots.gd`, `cloneshots.gd`, `wakeshots.gd`). The
grapple, the shooting and the prompts' later steps have not been played with real input.

## Costs and open ends

- The nest is 1,015 pods spread 400 m across, wider than 0038's clump; it is a baked
  nest, edited by hand.
- Every vine and tree is destructible. Cut the hooks and the colony's vines away and
  the planet has nothing to grapple: an indestructible anchor (a rock, a pylon) is
  not modelled.
- In the editor the nest and hooks float in a void; an editor-only preview of the
  planet's ground would fix that and is not built.
