# 0056 Decided: a saved nest can be baked to an authored scene

*30 October 2026. Amends 0033; builds on 0055 and rule 9.*

## Context

Generated nests read as empty. The tutorial (0055) showed what hand
placement gives: a nest to playtest, add to and take from. But 0033 keeps a
saved nest a seed, so a generated nest could not be opened in the editor.

## Decision

**A saved nest stays a `NestPreset` (kit, seed, population) and grows at
load. That is the default.** Hand-editing is the exception: **Bake** turns a
preset into `scenes/props/prp_nest_<preset>.tscn`, the plan's vines baked as
single vines are (`VineBaker`) and laid out round the origin as the planet's
placer would lay them. Nest scenes go in `scenes/props/nests/`, baked vines
in `scenes/props/vines/` (vines baked earlier stay in `scenes/props/`).

- One-way. The scene no longer regrows from its seed (the preset is kept for
  provenance) and is never written over unless asked: it may hold hand edits.
- Used like any set piece: placed in a level, a `PlacedNest` with `Vines`
  pointing at the scene's root hands its pods to the colony.
- Run by the Nest tab's **Bake** button (the nest Generate shows) and the
  bridge command `nest_bake` (`NestBaker`).
- **On a planet** a baked nest stands under a `PlacedNestSite`, which is
  placed roughly like a `PlanetSite`. Each vine keeps its flat distance and
  bearing from the heart and is stood on the curved ground with a small pad,
  so a vine moved or deleted in the editor stays so. The nest is held out of
  the tree until the ground exists (a vine's bend reads where it enters), then
  its `PlacedNest` takes the pods. `lvl_planet_nest` is the test.
- Contracts drawing authored nests from a pool is not built.

## What it costs

- A baked nest is its vines' `.vox` files, imports and scenes in LFS
  (0033's objection), paid only for nests worth keeping.
- Vines carry their size in the name (`prp_vine_leafy_4664888s1101`).
