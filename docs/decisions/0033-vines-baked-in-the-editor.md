# 0033 To be determined: a nest is generated at load, or picked from nests made beforehand

*Rewritten 24 October 2026. Open. Builds on 0018, 0021, 0029, 0032 and 0034.*

> **What this record said before (21 October):** vines are baked by the
> editor dock to a `.vox` and a wrapper scene, nests are laid out in the
> editor and saved, and nothing generates at runtime. The first half was
> built and stays (below); the second was overtaken when a 25,000-pickle
> nest needed 117 unique vines, and a nest is now grown at load
> (`docs/handoffs/vine-tool.md`, decision 9). Which way contracts go is
> what this record is to settle.

## What is settled

- **Nothing loads or generates mid-mission.** A nest is made behind the
  contract's loading screen (0021) or not at all.
- **A seed is one nest, everywhere.** `NestLayout` plans it from a kit, a
  seed and a population; `VineGrower` grows each vine valid (0036);
  `VineFactory` builds it in memory through the importer's own
  `VoxModelBuilder`, identical to its baked `.vox`. Fdlibm keeps the
  trigonometry the same on every machine.
- **Single vines are still baked** by the Vines tool's Vine tab
  (`addons/vine_gen`, 0043) for hand-placed set pieces:
  `art/voxels/prp_vine_<kind>_<seed>.vox` and its wrapper scene, the stalk's
  foot on the scene's origin, a hand-changed vine named with a `c`.
- **A saved nest is a `NestPreset`**: kit, seed and population, not baked
  vines. It regrows the same nest.

## The two ways

1. **Generate at load** (what is built). Every contract's nest is new.
   Measured at 25,000 pickles on the target laptop, plugged in: about 22 s
   of load (vines grow and build on all cores while the planet generates;
   planting costs about 50 ms a vine on the main thread), about 1.06 GB of
   managed memory and 0.65 GB in the engine.
2. **Pick from nests made beforehand.** Nests are generated in the editor,
   played and judged (the Nest tab's Good/Bad), and a contract picks a Good
   one, either regrown from its `NestPreset` (the same load as 1, but every
   nest has been seen) or loaded from baked vines and chunk graphs (a
   shorter load, an asset per vine in LFS, a fixed pool).

## How it is decided

- **Load time** at 25,000 on the target, both ways measured, paired. Over
  about 30 s counts against generating.
- **Quality**: the share of generated nests judged Good in
  `tools/vine-logs/nest_verdicts.jsonl`. If most are Good, the seen-first
  guarantee buys little.
- **Memory and disk**: baked vines are 50-130 KB each in LFS and their
  imported scenes far more; regrown presets cost nothing on disk.

Until then contracts generate at load, and a preset is only the tool's.

## What it costs

- Generating keeps every vine one nobody has looked at. The validator's
  rules (0036) are what stands between a bad roll and a player.
- Baked nests would fix the pool of vines a game ships with.
