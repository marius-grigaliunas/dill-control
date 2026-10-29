# 0049 The onboarding level is a nest

*29 October 2026. Built; asked for by the user. Replaces the onboarding of 0026.*

## Context

`lvl_drop_test` taught the line with graybox towers, a road, mesas and a camp,
and then had its signs removed (they were jokes), leaving nothing that said a
grappling hook existed. It was also unlike the game: the game is a vine nest,
the level was a city. And its jar, on the mech armour, could not be opened
with the starting rifle.

## Decision

- **A new level, `lvl_nest_intro`, on the planet, with the nest as the place.**
  No building, no road, no mesa. Anchors are vines. The old level stays on
  disk, unreferenced.
- **The nest is a saved `NestPreset`** (`NestBuilder.Preset`): kit, seed and
  population stand in for the contract's, so the level is the same nest every
  time. It uses a kit of its own, `nest_kit_intro.tres` (heart, fruiting and
  leafy, 300 m): at 1,000 pickles the default kit makes five 100 m vines, an
  anchor field too thin to swing in. Leafy vines carry about 30 pods, so the
  same 1,000 are 22 vines.
- **Authored places round a nest are `NestLandmark`s**: a bearing from the
  player's avenue and metres past the edge (`NestPlacer.Landmarks`).
- **Prompts are `IntroGuide`**: steps in a `GuideScript` resource, each done by
  an action of the player's, never by a timer alone, with the player's own
  keys in the text. The text is plain instruction. (The earlier signs were
  removed for being jokes; these state controls.)
- **The jar stays, weaker**: `voxel_destruction_jar.tres`. The mech stats are
  not touched.
- **Not carried over**: the drop pod, clones and extraction (0026's
  `ExtractionMission`). The contract is the colony's (`ColonyMission`):
  destroy every pod, leave at most a fifth alive.

## Open

- The jar's carve numbers are reasoned, not yet played. Tune them in the Asset
  lab (it is shared with nothing).
- The drop and the pod home are left out until the trial of 0026 is decided.
