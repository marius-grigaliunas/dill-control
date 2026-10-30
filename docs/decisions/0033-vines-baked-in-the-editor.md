# 0033 Decided: a nest is generated at load behind the loading screen; saved nests are seeds, not baked vines

*Amended by 0056 and 0055: a nest can also be baked to an authored scene and placed by hand; contracts still generate theirs. Rewritten 24 October 2026; decided the same day from measured load times.
Builds on 0018, 0021, 0032 and 0034.*

> **What this record said before (21 October):** vines are baked by the
> editor dock to a `.vox` and a wrapper scene, nests are laid out in the
> editor and saved, and nothing generates at runtime. The first half was
> built and stays (below); the second was overtaken when a 25,000-pickle
> nest needed over a hundred unique vines.

## Context

Two ways were open for a contract's nest:

1. **Generate at load.** `NestLayout` plans the nest from a kit, a seed and
   a population; `VineGrower` grows each vine valid (0036); `VineFactory`
   builds it in memory through the importer's own `VoxModelBuilder`; all on
   every core while the planet generates.
2. **Pick from nests made beforehand**, with their vines baked to `.vox`
   and imported, so a load skips the growing and the model building.

It was to be decided by load time at 25,000 and by quality.

## Measured

`lvl_nest_lab` loaded headless on the target laptop, plugged in, seed 1:

| Population | Vines | Load | Grow | Models | Planting | Managed / engine memory |
|---|---|---|---|---|---|---|
| 25,000 | 114 | 25.2 s | 3.7 s | 10.8 s | 10.1 s | 1,132 / 1,056 MB |
| 1,000 | 5 | 4.9 s | 0.8 s | 0.8 s | 0.7 s | 201 / 113 MB |

The planet alone is about 4 s. Of the 25 s, growing and building the vines
add about 10 s beyond the planet; planting adds 10 s, of which 8.3 s is the
vines entering the tree (`VoxelBody` setup and its coarse copies). Baked
vines would pay the planting just the same.

## Decision

**A contract's nest is generated at load, behind the loading screen (0021).
Nothing loads or generates mid-mission. A saved nest is a `NestPreset`: a
kit, a seed and a population, which regrows the same nest.**

- Baking a nest would save at most about 10 s of a 25 s load, for over a
  hundred unique vines in LFS, their imported scenes, and a fixed pool.
  It is not worth it.
- **Quality is held by the seed, not by baking.** Every vine passes the
  validator (0036), and the Vines tool's Good/Bad verdicts go to
  `tools/vine-logs/nest_verdicts.jsonl`. If the share judged Good proves
  low, contracts draw their seeds from saved Good presets: the same load,
  no new path.
- **The next load cuts are in the planting** (8.3 s entering at 25,000),
  then the models (10.8 s): what is built at `_Ready` could be built on the
  workers with the models.
- **Single vines are still baked** by the Vines tool's Vine tab
  (`addons/vine_gen`, 0043) for hand-placed set pieces:
  `art/voxels/prp_vine_<kind>_<seed>.vox` and its wrapper scene, the stalk's
  foot on the scene's origin, a hand-changed vine named with a `c`.
- **A seed is one nest, everywhere**: rolls through `SeededRandom` streams,
  trigonometry through `Fdlibm`.

## What it costs

- About 25 s of load at 25,000, 5 s at 1,000.
- About 2.2 GB of memory at 25,000 (managed and engine), the dense voxel
  grids kept in several copies a vine.
- Every contract's vines are ones nobody has looked at; the validator and
  the verdict logs stand between a bad roll and a player.
