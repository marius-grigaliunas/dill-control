# Handoff: the Vines tool, part 3 — pods and hanging pickles, then the main-screen tool

*23 October 2026.* Read `CLAUDE.md` first. The original task, its settled
decisions and its build order are in `docs/handoffs/vine-tool.md`. What the
user saw playing the first nest, and the eight fixes for it, are in
`docs/handoffs/vine-tool-2.md`. This file covers what has happened since and
what comes next. **Work only inside `C:\Projects\dill-control`.** The rules
are the same: one system at a time, numbers in `.tres`, bench on the target
machine, and short commits of your own files only (never
`docs/project_board.json`, `engine/` or `.vscode/`).

## Where the whole tool stands

| Step (vine-tool.md build order) | State |
|---|---|
| 1. Cleanup, `SeededRandom`, golden vines, 1,260 m planet | done |
| 2. A vine built in memory (`VineFactory`, `VoxModelBuilder`) | done |
| 3. Valid-only vines (`VineValidator`, `VineGrower` re-roll) | done |
| 4. Leaves as meshes (`VineLeaves`, `LeafMesh`, `mat_leaf_toon`) | done, overhauled this session |
| 5. Colour per kind, vine and leaf | done |
| 6. `NestKit`, `NestBuilder`, `NestPlacer`, `lvl_nest_lab`, `nest` bench | done |
| Fixes 1-8 from playing the nest (vine-tool-2.md) | done |
| **7. Pods and hanging pickles (decision 0034)** | **not started: next** |
| 8. The main-screen tool (Nest tab, range lab, vine panel, Play, Good/Bad/Save) | not started; today's Vines dock is its vine panel |
| 9. Decision records (rewrite 0033, write new ones) | not started |

The user has asked from the start to "generate the nest, see it, press one
button to play it". That is step 8. They chose to do pods (step 7) first.

## What this session did (commits fd31257..c7a42a4)

The eight fixes are in vine-tool-2.md's status section. After them came
four changes, all prompted by the user's screenshots:

1. **Stems run into their blades** (a3fc5f4). A stem used to end 0.6-1.7 m
   short of its blade. `LeafMesh.StemJoin` now places its end inside the
   blade's notch, and `VineLeaves.StemJoinOf` uses it for drawing, collision
   and shots. vinecheck's `StemsIntoBlades` holds it (point-in-polygon in
   blade space).
2. **`resources/ui/font_display.tres` exists** (1a8b758). It's a
   SystemFont asking for a comic face in bold; the loading screen no longer
   errors.
3. **A stem can be walked on** (090a500). Each stem has a box beam
   `StemWalkWidthM` (0.9 m) wide on its section's body, wider than it's drawn.
   The flex bench has a "stand on a stem" phase: the player holds at about
   35 m, riding the sway.
4. **Leafier vines** (c7a42a4). The user said the leaves "just don't look
   good" and asked for the cheapest change with the biggest effect. Before,
   heart and fruiting vines (most of a nest) grew one small leaf per arm tip
   on a long stem. Now every kind has:
   - 3-5 leaves along each arm and 1-3 at each ring, plus the tip leaf: about
     70-80 leaves a vine (heart 79, fruiting 71, weeping 54) instead of 45-50;
   - leaves 10-18 m across that fill their size, about 1.8x the old area;
   - a 12-25° tilt so their faces show from below, while staying standable;
   - 1-2 m stems;
   - pointed cucumber-like lobes (`LeafMesh.Edge`, a triangle wave);
   - leaves left out where they would cross a pod's drop
     (`VineGenerator.OverDrop`), so the pod count is unchanged;
   - LODs on the blades (`ImporterMesh.GenerateLods`).

   Without the LODs, the change cost about 6 ms of GPU on the ground; with them
   it costs nothing measurable. All four baked vines were re-baked and the
   golden vines re-recorded. vinecheck compares leafy and fruiting vines by
   leaf area per pod (471 against 25 m²).

Before and after shots, in `perflogs/` (gitignored, on the user's laptop):
`leaves_before*.png`, `leaves_big.png`, `leaves_big_close_40.png`.

## Open, and waiting on the user

- **How the leaves look.** The user hasn't judged the overhaul yet. From
  the side at a distance, the leaves still read thinner than in the user's
  cucumber reference, because they're held near level so they can be stood
  on, and the voxel arms dominate the outline. The user suggested two
  further options:
  - drooping, outward-facing leaves, which can't be stood on (or only the
    flatter ones near the stalk can);
  - "make them out of voxels or something": blades styled like voxels (for
    example, a stepped edge and flat-shaded cells drawn into the mesh) so
    they match the stalk. Real voxel leaves were rejected in step 4 for
    their cost.

  Ask before step 7. A few screenshots from `vinelook.gd` (`close` mode) are
  enough to decide.
- **The bazooka kick** (KickPerDamage 40, MaxKick 8,000 N s) hasn't been
  judged by eye either.

## Measured, 25,000 pickles, plugged in, paired

| Phase | Before the leaf overhaul | Now |
|---|---|---|
| Ground | 19.0 ms (GPU 13.4) | 18.9 ms (GPU 13.4) |
| Canopy | — | 17.3 ms |
| 400 m above | 21.1 ms (GPU 16.2) | 21.6 ms (GPU 16.6) |
| Load | 36 s | 42 s |

The nest has 117 vines and reaches 393 m. **Every phase is over the 16.7 ms
budget, and that predates this session**: the first run, before any change,
already read 18.0 ms on the ground. The previous handoff's 12.9 ms is not
reproducible today. Treat this as a bug to fix, not a number to live with.
The likely levers, from the earlier sessions:
- **Draw calls from above (about 860).** Each vine has its own leaf
  MultiMeshes. A nest-wide MultiMesh per leaf shape, with the vine's flex row
  in instance custom data, would cut them sharply.
- **Shadows.** Leaves now cast as far as the stalk does (fix 4), and there
  are twice as many leaves. `forestab` or `gpuab` style switching would show
  what they cost.
- **Load time.** Planting costs about 50 ms a vine on the main thread
  (`VoxelBody` setup and coarse copies meshed at `_Ready`), and the leaf LODs
  added about 6 s.
- **Memory is about 1.9 GB.** Dense grids and chunk maps are kept in several
  copies per vine.

## Next: step 7, pods and hanging pickles

Decision 0034 is settled: every pickle starts on a pod, an empty pod regrows,
only destroying pods or vines ends the colony, and the contract is won when
every pod is destroyed and at most 20 % of the starting pickles are alive
(100 % earns a bonus). Decision 0029 says how the colony wakes (over 30 s once
aggravated).

What exists to build on:
- **Pods are markers today.** `VineGenerator.HangPods` grows them,
  `KeepHanging` keeps only those a pickle can hang from (a clear drop below),
  and they come out as `@Pod01..` markers in the model. The baked `.vox` and
  `VineFactory` carry them the same way. `NestLayout` sizes the nest at one
  pod per pickle and reports `Short`.
- **`HordeManager.SpawnOrigin`** has `Base, Brood, Shed, Pickle, Thrown`.
  Extras are counted and capped per origin. Add `Pod` (or reuse `Brood`,
  "a nest's produce", if that's what it was meant for: check the callers).
- **The horde draws its agents by instancing** and animates them in the
  vertex shader. Hanging pickles should be drawn the same way, never a node
  each.
- **`lvl_nest_lab` has no horde** (`WithHorde = Horde != null`). The nest's
  pickles are to become its horde.
- The tier's horde count is still not the nest's population (see
  vine-tool-2.md). Once pods are the horde, the population is the pod count.

Build it in this order, each part committed separately:
1. **`PodField`, bulk pod state** (full, empty, regrowing, destroyed; its
   vine and marker; a regrow timer) in flat arrays for up to 25,000 pods,
   filled by `NestBuilder` from each vine's markers in world space. The pods
   must follow the vine's flex bend: read the pod's section and flex row the
   way leaves do (`flex_bend.gdshaderinc`).
2. **Hanging pickles drawn.** A pickle at every full pod, through the
   horde's instancing and swaying with its vine. Bench `nest pop=25000`
   against the current numbers; this is the part most likely to cost frame
   time.
3. **Pods can be shot and die with their vine.** A pod is a target
   (`VoxelBody.Intercept`, or a cheap query against `PodField`). A felled,
   cut-away or unrooted vine destroys its pods.
4. **Drop and wake.** When the colony is aggravated (0029), pickles drop from
   their pods into `HordeManager` as the new origin. Killed on a pod, a pickle
   empties it.
5. **Regrowth.** Numbers in a new resource (regrow seconds, how many at once,
   whether a damaged vine regrows slower), per 0034.
6. **Win check and bonus**, and a HUD count of pods and pickles left.
7. **A bench phase with 25,000 hanging pickles**: calm, waking and in the
   fight.

Ask the user before coding: what aggravates the colony (a shot anywhere in
the nest, a pod hit, or the player getting close), and whether a pickle shot
on its pod drops dead or falls and fights.

## Then step 8, the tool, and step 9, the records

Both are as specified in vine-tool.md ("The tool" and "Build order"), with
the additions in vine-tool-2.md: play is handed off through a `user://` file
to `lvl_nest_lab` via `EditorInterface.PlayCustomScene`, and both JSON Lines
logs start with an `"about"` line. Step 9 has gained topics. Write records for:
- leaves as meshes, including their walkable stems and holes;
- nest sizing (the clump: 55-70 m apart, a 400 m cap);
- the 1,260 m planet;
- the Structure and Litter layers;
- the deterministic re-roll gate;
- the tool.

Rewrite 0033 from the load numbers: it is 42 s at 25,000 now.

## Known and not yet done

- A felled vine sheds every leaf, and they sail down for seconds.
- `lvl_horde_flats`'s horde wanders into the flex bench mid-run.
- Leafy vines are about 46 % valid on their first roll (they sag). That's
  fine with the re-roll, but they're a range-lab candidate.
- **The working tree has uncommitted re-saves of the user's**: the four
  `vine_kind_*.tres`, `leaf_default.tres`, `vine_bake.tres`, the enemy and
  horde stats, `kit_graybox_compound.tres`, `lvl_drop_test.tscn` and one
  `_leaves.tres`. They look like the editor re-saving: uids added, and values
  equal to their defaults dropped. Leave them out of your commits, and ask
  the user whether to commit them. Before re-baking a vine, check that the
  dropped values really match the class defaults in `VineKind.cs`,
  `LeafStats.cs` and `VineBakeStats.cs`. If one doesn't, the vines will
  change and vinecheck will say so.

## How to work here

The lessons in vine-tool-2.md still hold: check the power first, no
foreground `sleep`, use the Edit tool for lines with `\n`, no engine calls on
nest worker threads, and measure a nest by loading it headless. This session
added:
- **Visibility ranges with no fade use their margins as hysteresis.** Two
  neighbouring copies can both be hidden. Hand over with no margins and a
  small overlap (`VoxelBody.HandoverOverlapM`).
- **Check a visual claim with a test before calling it fixed.** The "floating
  leaves" were two bugs: a drawing gap, and stems that stopped short of the
  blade. The first fix passed a check that the second failed. vinecheck now
  holds both.
- **More geometry in a nest needs LODs.** Thousands of blades a few pixels
  across cost 6 ms of GPU until they had coarser copies.
- **To change leaves:** edit the kind `.tres`, re-bake each baked vine
  (`vinebake.gd -- <kind> <seed>`), delete
  `.godot/imported/prp_vine_*.vox-*`, run `--editor --quit`, then
  `vinecheck.gd -- record` and `vinecheck.gd`.
- **Tools added:**
  - `vinelook.gd -- <png> close <vine> d1,d2 [elevation]`: a vine from given
    distances;
  - `vinelook.gd -- <png> holes <vine>`: a shot leaf's holes;
  - the `nest` bench's plan view from 900 m, its under-the-canopy view, its
    under-ground rescue phase and its cratered heart;
  - the flex bench's `peak=` (the tip's peak offset) and its stand-on-a-stem
    phase.
