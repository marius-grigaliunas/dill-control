# 0055 The tutorial is handbuilt: `lvl_tutorial`, a placed nest and a pod drop

*29 October 2026. Built; asked for by the user. Replaces 0049 for the onboarding level.*

## Context

`lvl_nest_intro` (0049) was a generated nest on the planet. The planet, the nest
and the places round it exist only after load, so the level's editor view was an
empty tree: the user, who works visually, could not see the map, move a vine, swap
an asset or touch the ground. That breaks rule 1 (a designer would want to move it,
so it is a node) however good the nest was.

## Decision

- **`lvl_tutorial` is a handbuilt level on a `SculptedTerrain`**: a 720 m ground
  with a rim of hills, two ridges, a flat landing field in the south and the nest
  in the middle. Everything is visible in the editor: select, move, delete, sculpt.
- **The nest is real vines placed by hand**, 27 baked vine scenes under Foliage
  (25 leafy painted with the Vines brush, a heart and a fruiting vine placed from
  the existing bakes), 1,115 pods. Three shrub vines under `Props/Hooks` are the
  first hooks on the way in; they carry no pods, so the first shot cannot wake the
  colony by accident.
- **`PlacedNest` hands the placed vines' pods to `PodField`** at load (the `Pod`
  markers inside each vine's model) and empties the horde: every cucumber hangs on
  a pod, as in a generated nest (0034). Swap, move or add a vine and its pods follow
  with no other step. It is the handbuilt twin of `NestBuilder`.
- **This is an exception to 0033** ("a saved nest is a `NestPreset`, never baked
  vines"), made for a set piece the designer must be able to edit. Contracts keep
  generating their nests; the 25 baked leafy vines are LFS files, the price of a
  nest you can see.
- **The pod drop is a first-person placeholder.** `PodLanding` drops every body,
  the first and each clone, in `prp_landing_pod` at the `LandingSite` marker
  (move it to move the drop; its -Z is the way the rider faces out). The rider
  looks out of the cabin's own camera, turns their head with the mouse while it
  falls (`DropPod.FirstPerson`), feels it land (a blast of `LandingRadius`), waits
  `ExitDelay` and steps out facing where they looked. The cabin is boxes: no art
  for it exists (0054). Numbers: `resources/mission/drop_rules_tutorial.tres`.
- **The jar is gone.** The prompts (`guide_tutorial.tres`, `IntroGuide`) are the
  old flow without it, and the first step, "look out of the window", lasts until
  the rider steps out (`GuideStep.Done.Landed`). Distances count from where the
  player stands as each step appears.
- **Not built:** extraction by pod, clones' flashes (0054), the queen, the minigun
  gift (0051). `lvl_nest_intro`, `NestLandmark` and the generated preset stay on
  disk as the demo of a generated nest; nothing refers to them from the slice.

## Measured

Idle bench on the target machine: 12.5 ms mean, 15.2 ms worst, 0 of 483 frames over
16.7 ms, 300 draws. The opening was photographed from the rider's eyes with
`scripts/debug/tutorialshots.gd` (fall, landing, stepping out).

## Costs and open ends

- A dark band lies along the horizon from standing height on the flat field (the
  toon ink pass at a grazing angle); not investigated.
- The nest sits 100-130 m north of the pad and spreads about 300 m, wider than
  0038's clump of 55-70 m between vines; it is a tutorial, and is edited by hand.
- The drop's shaking, sound, the cloning flashes and the pod's model are all to do.
