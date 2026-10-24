# Handoff: the Vines tool, part 3 — pods and hanging pickles, then the main-screen tool

*Superseded for what comes next by `docs/handoffs/vine-tool-4.md`.*

*23 October 2026.* Read `CLAUDE.md` first. The original task, its settled
decisions and its build order are in `docs/handoffs/vine-tool.md`. What the
user saw playing the first nest, and the eight fixes for it, are in
`docs/handoffs/vine-tool-2.md`. This file covers what has happened since and
what comes next. **Work only inside `C:\Projects\dill-control`.** The rules
are the same: one system at a time, numbers in `.tres`, bench on the target
machine, and short commits of your own files only (never
`docs/project_board.json`, `engine/` or `.vscode/`).

## Status, 24 October 2026 (later): step 8 is done; step 9 (the records) is next

The Vines tool is `addons/vine_gen`'s main screen (commits 30c1d3c..):
- **Nest tab**: kit, seed, population, horde. Generate plans the nest with
  `NestLayout` and stands it on a flat disc with its measures (the planet
  is not drawn: its classes are placeholders in the editor; this departs
  from "on the 1,260 m planet in the editor viewport"). Play hands it to
  `lvl_nest_lab` (`NestPlay`, `user://nest_play.json`). After play the lab's
  `NestPlaytest` result is shown; Good/Bad with a note append to
  `tools/vine-logs/nest_verdicts.jsonl`; Save keeps a Good one as a
  `NestPreset` in `resources/mission/nests/`, and Saved loads it back.
  Save keeps the seed, not baked vines: whether contracts load saved nests
  is still 0033.
- **Range lab**: N seeds of a kind, each its own roll (no re-roll),
  thumbnails, valid or why not, Good/Bad to `vine_verdicts.jsonl`, each
  setting's spread over its range, ranges edited in place and saved.
- **Vine tab**: the dock's panel (`VinePanel`), also still in the dock.
- Checked in the editor with `--editor -- nesttab-check=<png>` and
  `rangelab-check=<prefix>` (their verdicts go to `user://`).

Found with it, not yet acted on:
- Fruiting vines are 56 % valid on their own roll; 20 of 48 fail on
  pods left out. The re-roll hides it; the range lab shows where.
- Nearest-neighbour spacing in a nest goes down to 45 m, under the 55-70 m
  the user set: the layout's jitter (0.22) pulls slots in.

## Status, 24 October 2026: step 7 is done; step 8 (the tool) is next

Decided with the user before coding (memory `colony-wake-rules`): any harm
to the colony wakes it (getting close does not); a pickle shot on its pod
drops dead; the leaf overhaul stays for now. The user's editor re-saves were
checked against the class defaults and committed (fdc22bf).

Commits 5763c23..f0228a2, each part of step 7 its own:
1. `PodField`: every pod in flat arrays, filled by `NestBuilder` from
   `VineFactory.Data.Pods` (the `@PodNN` markers, read from the built model
   with no nodes). Each hangs from the voxel nearest its marker and is
   destroyed when that voxel or its vine goes.
2. Hanging pickles: instanced by 150 m ground cells, capsules near and
   columns far (`pickle_hang`, `pickle_hang_far`), bent on the GPU by their
   vine's flex row in instance custom data (`FlexField.Track`,
   `flex_bend_row`). Paired at 25,000: under 1 ms of GPU.
3. Shooting: a pool of `PodHitProxy` on the pickles nearest the aim; blasts
   from the bus. Killed, a pickle falls as a body (capped) and puffs.
4. The wake: harm (a pickle, a vine or leaf via `VoxelBody.Damaged`, a
   blast in the nest) drops every pickle into the horde over 30 s, nearest
   first (`SpawnOrigin.Pod`, lobbed so the fall does not kill it). The lab
   now has a horde with no agents of its own. **A rifle round on bare
   ground near the nest does not wake it**: nothing broadcasts one, and a
   new `EventBus` signal wants a decision record.
5. Regrowth: an emptied pod regrows after 45 s, 2,000 at once at most,
   slower on a damaged vine, and only for a pickle that died (the colony
   never grows past its pods). In an awake colony a regrown one hangs 5 s
   and drops.
6. `ColonyMission`, `ColonyRules`, `ColonyHud`: every pod destroyed with at
   most 20 % alive clears the nest; none alive earns the bonus.
7. `bench.gd ... lvl_nest_lab.tscn colony pop=25000`, plugged in:

   | Phase | Mean | GPU | Over 16.7 ms |
   |---|---|---|---|
   | Calm, under the canopy | 14.7 ms | 12.8 | 3 of 341 |
   | Calm, in the canopy | 14.6 ms | 12.8 | 3 of 275 |
   | Waking 0-10 s | 15.7 ms | 13.7 | 87 of 637 |
   | Waking 20-30 s | 16.3 ms | 14.1 | 56 of 614 |
   | In the fight | 16.9 ms | 14.7 | 331 of 887 |

   The fight is the horde's own cost at 25,000 (about 2 ms of CPU in its
   update); the pods add about 0.5 ms of CPU. The `nest` bench's numbers
   swing by 5 ms between runs on this laptop with nothing changed: pair them.

Also fixed on the way: `Rod`'s calibration cache was a plain dictionary
written from every nest worker at once, and could fail a load (79a1e2e).

`scripts/debug/podcheck.gd -- pop=3000` holds all of it (17 checks, about
3 minutes headless).

Not decided, worth asking: whether the colony goes back to sleep; whether a
hanging pickle's kind should match what it drops as (all draw as Standard,
the horde's composition picks the kind on the drop).

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
| 7. Pods and hanging pickles (decision 0034) | done (see the status above) |
| 8. The main-screen tool (Nest tab, range lab, vine panel, Play, Good/Bad/Save) | done (see the status above) |
| **9. Decision records (rewrite 0033, write new ones)** | **next** |

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
