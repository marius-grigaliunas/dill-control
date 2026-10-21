# 0033 Proposed: vines are baked in the editor, nests laid out and saved there; nothing generates at runtime

*21 October 2026. Proposed. Builds on 0018, 0029 and 0032. Step 1 (the
generator, the dock and the bake) is built; steps 2 and 3 are planned.*

> **Amended 21 October 2026:** the browser prototype is retired and
> `tools/vine-gen` deleted. Vine rolls go through `SeededRandom` named
> streams (`vine:<stage>`); `VineRandom` and V8's `hypot` are gone, fdlibm
> stays so a seed grows the same vine on every machine. The golden vines are
> recorded from C# (`scripts/debug/vine_golden.json`, `vinecheck.gd --
> record`). Every seed now grows a different vine than before, so the baked
> vines were re-baked. Whether a nest is generated at load or picked from
> saved nests is reopened (`docs/handoffs/vine-tool.md`); this record is
> rewritten when that is measured.

## Context

The vines of 0029 came from a browser prototype, `tools/vine-gen/`: settings
as ranges rolled per seed, a `.vox` downloaded and carried into Godot by
hand. Nothing in the editor could make one. The baked vines stood off their
own node (`prp_vine_41051`'s foot is 6.5 m from its origin, so aiming at
the node missed the stalk), were neon green, and every vine was a trip
through a browser.

Two places could grow a vine: the game, at contract load, or the editor,
ahead of time. Growing in the game means every contract pays the time to
grow, mesh and chunk tens of thousands of voxels per vine, and the vine a
player swings on is one nobody ever looked at. Growing in the editor means
a vine is an asset like any other. It is imported once, chunked by the
importer (0027) and drawn whole until hit (0032), and someone has seen it
before a player does.

## Decision

**Vines are generated in the editor by the Vines dock and baked to assets.
Nests are laid out in the editor from baked vines and saved as scenes.
Contracts place saved nests. Nothing about a vine or a nest is generated at
runtime.**

- **The dock** (`addons/vine_gen`, our second editor plugin) picks a kind and
  a seed, previews the vine as the importer will mesh it, and reports what it
  is judged against:
  - voxels against the tall tree's 30,352;
  - collision bodies at the vine's `SectionEdge`;
  - pods without a pickle's clear drop;
  - pods too close;
  - any axis over 256.

  A setting can be changed for one vine. **Bake** writes:
  - `art/voxels/prp_vine_<kind>_<seed>.vox`, with its `.vox.import` at
    `vine_bake.tres`'s voxel size (0.5 m);
  - a `.json` of every setting and count;
  - `scenes/props/prp_vine_<kind>_<seed>.tscn`, the wrapper of 0032.

  A name regrows its vine; a hand-changed vine gets a `c`. Re-baking replaces
  the model and keeps the wrapper and its uid. `vinebake.gd` bakes the same
  way, headless.
- **The stalk's foot is the scene's origin.** The writer translates the stalk
  so its bottom layer's centre lands within half a voxel of `(0, 0, 0)`.
  `PlantFlex` takes its spine's foot from there, and the bench aims at the node.
- **Data:**
  - a kind is a `VineKind` (`resources/foliage/vine_kind_*.tres`), every
    setting a range;
  - the look is `VinePalette` (`vine_palette.tres`);
  - how a vine is baked and judged is `VineBakeStats` (`vine_bake.tres`).

  None needs a rebuild.
- **The browser prototype is the reference until it is retired.**
  - `VineGenerator` ports `vine.js` function for function.
  - Its random numbers are the prototype's own, mulberry32 from 32-bit FNV
    stream names (`VineRandom`), not `SeededRandom`'s SplitMix64.
  - Its `hypot` is V8's and its `sin`, `cos`, `tan`, `atan2` and `exp` are
    fdlibm's (`Fdlibm`), as V8's are.

  So the same settings grow the same voxels: `vinecheck.gd` compares 16
  vines from `make-golden.js`, and all 16 match exactly. .NET's own
  trigonometry differs in the last bit on 2-18 % of inputs, and that moved a
  stem voxel in one vine of 16. `VineRandom` is bake-time only; everything
  that rolls in the game still goes through `SeededRandom`.
- **Next steps:**
  - **Step 2:** a nest kit resource, the nest laid out by the dock into
    `lvl_nest_lab.tscn` (baked vine scenes under `Props`), with metrics from
    what has been measured:
    - 20 m apart is unplayable;
    - 100 m swings well but slowly;
    - grapple range 140 m;
    - a vine about 118 m tall with 24 m arms;
    - 85 vines at 9-12 ms;
    - about 13 bodies a vine.

    It also adds a `nest` bench mode.
  - **Step 3:** a recorded playtest (swings, time to the heart, ground
    touches), Save good or bad with a note, and the nest written as a
    `.tscn` and a data record.

  The prototype retires after step 3.

## What it costs

- Two copies of the generator until the prototype retires. The golden check
  is what keeps them the same; change one, regenerate the golden file, and
  the check says whether the other followed.
- A vine cannot vary per contract except by which baked vines a nest uses and
  how it places them. That is the point: variety comes from the pools and
  the nests, and every vine has been looked at.
- Baked vines are binary assets (`.vox` in LFS), about 50-130 KB each.
