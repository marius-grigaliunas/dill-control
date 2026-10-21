# Task: the Vines tool — a main-screen editor extension that generates, previews, plays and records pickle nests

Read `CLAUDE.md` first; its rules apply (one system at a time, numbers in `.tres`,
decision records, bench on the target machine, short commits, commit only your own
files). **Work only inside `C:\Projects\dill-control`.** You may change any engine
code in the project (`scripts/`, `addons/`, `shaders/`, resources); never touch
files outside the project folder.

Design records to read: `docs/decisions/0029` (vine hive), `0032` (plants bend),
`0033` (vines baked in the editor, **open**: see below), **`0034` (pods regrow,
decided)**, `0021` (level builder behind a loading screen), `0017` (planet sites),
`0020` (compound placement), `0027` (chunk graph). Auto-memory holds the direction:
`vines-baked-not-runtime` (revised), `design-direction-vine-hive`,
`generators-want-ranges`, `vine-palette-deep-green`.

## What exists now (commits 8c7a56c..88b0889)

- **Vine generator in C#.** It ports the retired browser tool exactly: 16 of 16
  golden vines match voxel for voxel.
  - `scripts/foliage/VineGenerator.cs` is the generator: pure, with no Godot nodes.
  - `VineParams.cs` holds the settings table and `JsMath`.
  - `VineRandom.cs` is mulberry32 with FNV stream names.
  - `Fdlibm.cs` holds sin, cos, tan, atan2 and exp, bit-exact with V8. Keep it:
    it makes the same seed give the same vine on every machine.
- **Resources.** These are `[GlobalClass, Tool]`, because without `[Tool]` the
  editor loads them as empty placeholders.
  - `VineKind` (`resources/foliage/vine_kind_{heart,fruiting,weeping,leafy}.tres`):
    every setting is a `Vector2` range, x = least and y = most.
  - `VinePalette` (`vine_palette.tres`): deep greens. Shell 52,84,44; knuckle
    34,60,32; core 150,146,108; leaf 30,70,40; petiole 22,50,30.
  - `VineBakeStats` (`vine_bake.tres`): voxel size 0.5, the checks, and the
    wrapper's stats. It holds those stats as plain `Resource` and reads
    `SectionEdge` by name, because game classes are placeholders in the editor.
- **Writing and baking.**
  - `scripts/voxel/VoxWriter.cs`: a pure `.vox` writer.
  - `scripts/foliage/VineBaker.cs` writes `art/voxels/prp_vine_<kind>_<seed>.vox`,
    its `.vox.import` (size 0.5) and `.json`, and
    `scenes/props/prp_vine_<kind>_<seed>.tscn`, the `prp_vine_41051` wrapper.
  - The stalk's foot is on the origin. A setting changed by hand adds a `c` to the
    name. A re-bake keeps the scene.
- **The dock** `addons/vine_gen` (`VineGenPlugin.cs`, `VineGenDock.cs`) uses the
  4.7 `EditorDock` API:
  - kind, seed, Roll, pool seed;
  - a preview (drag turns, the wheel zooms, right-drag pans, double-click frames);
  - a report, per-setting overrides, and Bake.
  - Set dock properties from the plugin, never in the dock's constructor: that
    crashes when the dock is created from script.
- **Checks.** `scripts/debug/VineCheck.cs` with `vinecheck.gd` compares against
  `tools/vine-gen/golden/golden.json`, runs the prototype's checks, reads a written
  `.vox` back, and checks every baked scene's wiring and foot. `VineBake.cs` with
  `vinebake.gd -- <kind> <seed|pool:N>` bakes headless.
- **Baked vines.** `prp_vine_{heart_6303521,fruiting_2101677,weeping_7474758,leafy_7103694}`.
  The Fruiting one stands in `lvl_horde_flats` at (-240, 0, -170).
- **Bench.** `flex <vine> <shot height>`, with `@gotorel:Node:x,y,z`, and the
  grapple thrown at `BenchStalk:45`. At 40 m the baked vine passes the whole
  timeline: wind, shots, grapple (426 pulls), perch, bite, fall. At 12 m the rocket
  volley fells either vine, about half the time.
- **Existing systems to reuse:**
  - `ILevelBuilder` with `LevelRoot.Builder` and `LoadingOverlay` (`SystemBuilder`
    builds planet, compound and horde behind a screen);
  - `PlanetSite` (levelled pads), `CompoundPlacer`, `WorldUp`, `SeededRandom`;
  - `HordeManager` and the contract tier's `AgentCount`;
  - `PlantFlex` (spine from voxels; foot = `Body.Model` origin; `WorldUp.At(Foot)`);
  - `VoxelBody` (reads cells, dims, origin, chunk graph, palette and emission from
    metadata on the model's `MeshInstance3D` children);
  - `VoxChunker`, `VoxMesher`, `SinkProbe`, `FlexCheck`.
  - The importer's `AddShape` and `WriteChunks`
    (`addons/vox_importer/VoxSceneImporter.cs`) are what a vine model needs, built
    from pure parts.

## Decisions taken in the design talk (settled, do not reopen)

1. **The browser tool is retired.** Delete `tools/vine-gen` (and its golden-file
   check). Switch vine rolls to `SeededRandom` named streams (CLAUDE.md rule),
   delete `VineRandom`, and drop the JavaScript-only `JsMath.Round` and `Hypot`.
   **Keep fdlibm** for determinism. Re-record golden vines from C# as a regression
   check: same seed, same voxels and pods.
2. **Only valid vines.** Validity is a hard rule; a seed that fails is re-rolled
   deterministically (the seed plus an attempt number), so a seed still always means
   one vine. Report the rejection rate per kind: high rejection means poor ranges.
   - **Shape checks:**
     - every voxel face-joined to the ground;
     - nothing below the ground (arms and leaves never reach it);
     - leaves clear of the stalk, the arms and each other;
     - pods hanging free with a pickle's drop;
     - no axis over 256;
     - collision bodies under the limit;
     - in a nest, no vine overlapping a neighbour.
   - **A short physics test:** a few simulated seconds with no player, wind or
     shots. It fails on tip drift or ringing, a joint kinked at rest, a piece
     sunk or loose untouched, or a leaf falling on its own. Reuse what `FlexCheck`
     and `SinkProbe` measure.
   - **Realism rules:** the stalk tapers; arms shorten and thin upward; leaves sit
     at joints facing up; pods hang below arms; lean is bounded. The user judges
     "good" by eye; the rules enforce plausibility only.
3. **Leaves leave the voxel body; tendrils are removed.**
   - **Look:** the cheapest real leaf. One simple mesh per leaf (a few dozen
     triangles): broad, heart-shaped, shallowly lobed with toothed edges, a cupped
     blade, darker veins in vertex colour, on a long stem, facing the light.
     - Drawn with the toon material and ink outline.
     - A small set of shapes per kind, scaled, turned and tilted per leaf.
     - 6–14 m across, held nearly level so they are platforms, not ramps.
   - **Physics:**
     - one thin shape per leaf on the vine's existing section body, so there are
       no new bodies and it follows the bend;
     - the player can stand on a leaf and hook it (the pull goes into the arm);
     - a leaf has a little health; when it runs out it tears off as one loose body
       (under the loose-piece cap) and puffs into toon dust;
     - a cut arm takes its leaves;
     - bent by the same shader as the vine; **no sag**.
   - **Why:** leaves, stems and tendrils are about 20 % of a Fruiting vine's voxels
     but about 50 % of its faces and collision boxes, and add bodies (7 vs 4). On
     Leafy they are 70 % of voxels, 88 % of faces and 89 % of boxes.
4. **Variety from ranges.**
   - Colour: per-kind palette ranges (base colour plus hue and brightness spread)
     kept inside the deep-green band, never neon; each vine rolls a shade and each
     leaf varies slightly.
   - Size: kind height ranges, plus a per-vine size jitter in the nest kit; no two
     neighbours match.
5. **One planet size: radius 1,260 m.** Pin `planet_archetype_rock.tres`
   `RadiusMin` and `RadiusMax` to 1260, and check `bench.gd planets` still holds.
   Nests are generated *on* the planet, after planet and compound, in the
   builder:
   - each vine stands on its own `WorldUp`, with lean toward the heart in the
     local frame;
   - each gets a small `PlanetSite`-style levelled pad plus a root flare grown
     into the ground;
   - spacing and swing reach are measured along the surface.
6. **Nest size follows population.**
   - One pod per pickle. Population is the contract's `AgentCount`, clamped to
     1,000–25,000.
   - The generator adds vines, each with a rolled pod count, until pods cover the
     population. The footprint grows in rings from the heart.
   - If it doesn't fit at minimum spacing (20 m is unplayable; 100 m swings well;
     grapple range 140 m), shift toward high-pod kinds rather than crowding.
   - Kinds' pod ranges likely need retuning toward about 100 per vine (today
     12–220; Leafy 14–32).
7. **Pods and hanging pickles, per decision 0034 (decided):**
   - every pickle starts on a pod; workers are pickles that dropped;
   - an empty pod regrows its pickle while the pod and its vine stand;
   - **nothing regrows until the colony is aggravated** and pods empty;
   - destroying a pod, or felling its vine, ends it for good;
   - won when **every pod is destroyed and at most 20 % of the starting pickles
     are alive**; all killed earns a bonus;
   - regrowth numbers live in a resource, tuned in play;
   - pods become shootable objects with state (full, empty, regrowing, destroyed),
     simulated in bulk, never a node each, up to 25,000.
8. **Saving and data.**
   - **Play** writes nothing.
   - **Good** and **Bad** append a verdict record to a log: seed, kit, generator
     version, metrics, playtest stats and note.
   - **Save** (a separate button) writes the nest scene; only Good nests can be
     saved.
   - Keep the verdict log and the per-vine parameter log in **separate files**:
     they are future ML training data, so never drop bad results.
   - Each log is JSON Lines whose **first line is an `"about"` record**
     explaining the file in plain words (what a line is, each field, the generator
     version). Track the logs in git.
9. **Decision 0033 stays open.** Nothing loads or generates *mid-mission*.
   Whether the loading screen generates the nest or picks from pre-generated saved
   nests is decided by measured load time and quality. Rewrite 0033 as
   "to be determined: generate at load or pre-generate", with the criteria, and
   settle it once the numbers exist.
10. **The single-vine Bake stays** as one panel, for hand-crafted set pieces.
11. **The tool becomes a main-screen editor plugin**: a C# `EditorPlugin` with its
    own top tab beside 2D, 3D, Script and AssetLib. It is an extension of
    `addons/vine_gen`, not a GDExtension: no native code. Keep the dock for quick
    access, or fold it in. Update CLAUDE.md rule 7 if the plugin set changes.

## Measured (for budgets; re-measure as you go)

Per baked vine, sections at edge 64, step 1:

| Vine | Solid voxels | Faces | Boxes | Bodies | Grid cells | Chunk bake |
|---|---|---|---|---|---|---|
| Fruiting | 17,099 | 28,274 | 2,849 | 7 | 874 k | 0.3 s |
| Heart | 29,197 | 45,446 | 4,494 | 15 | 1.87 M | 0.7 s |

- **Hollowing interiors makes everything worse:** faces +22 %, boxes +13 %,
  chunks +47 %, and the vine falls apart into 158 islands. Grids are dense, so it
  saves no memory. Do not hollow.
- **Hidden voxels cost nothing to draw,** and a solid interior merges into a few
  big boxes.
- **For 250 unique vines the real risks are:**
  - dense grid memory of 250–750 MB, which needs sparse or brick storage, or
    tighter parts;
  - chunk baking of 75–175 s on one core, about 10–25 s on eight;
  - several million faces near. Removing leaves from the voxel body roughly
    halves faces and boxes.
- **Generation time:** 20–80 ms per vine in C#.
- **Frame times:** 85 shared-mesh vines run at 9–12 ms. The flex bench on one
  baked vine ran 10–14 ms mean, with a worst frame of about 30 ms during the fall.
- **The goal:** 250 unique vines holding 25,000 pickles at 60 fps on the target
  (11th-gen i5, Iris Xe, 1080p). Today's forest is a test, not the target.

## The tool (what the user wants)

> Generate the nest, see it in the viewport, and press one button to load straight
> into the generated level, free to grapple and destroy and see how it is, with the
> horde or without.

- **Nest tab:**
  - controls: kit, seed, population (1,000–25,000), horde on or off;
  - **Generate** builds the nest on the 1,260 m planet in the editor viewport,
    using the same builder the game runs;
  - metrics: vines, pods, footprint, spacing, rejection rate, generation and
    chunk time, memory;
  - **Play** opens `lvl_nest_lab.tscn` with that kit and seed through
    `EditorInterface.PlayCustomScene`; the level's `ILevelBuilder` builds the nest
    behind the loading screen;
  - after play: **Good**, **Bad**, a note, **Save**.
  - Record playtest stats: swings, time to the heart, ground touches, pods
    destroyed, pickles left, win state.
- **Range lab:**
  - pick a kind and roll N seeds (about 50); each shows as a thumbnail with
    pass/fail and its rejection reason;
  - mark Good or Bad, which goes to the parameter log;
  - show, per setting, how good and bad vines are spread over its range, so ranges
    can be narrowed from evidence;
  - edit kind ranges in place; saving writes the `.tres`.
- **Vine panel:** today's single vine: preview, report, overrides, Bake.
- **The game side:**
  - `NestKit` resource: layout ranges for rings, spacing, avenues, zones, size
    jitter, and kind weights;
  - `NestBuilder` (`ILevelBuilder`): vines grown on all cores, models built in
    memory with the importer's own pure code (no `.vox` files), placed with
    `PlanetSite` pads; pods with hanging pickles;
  - `lvl_nest_lab.tscn`, which follows the level tree in CLAUDE.md.

## Build order (each step builds, is benched and committed)

1. **Cleanup.** Delete `tools/vine-gen`; move rolls to `SeededRandom`; re-record
   golden vines from C#; pin the planet radius at 1,260 and run `bench.gd planets`.
2. **A vine built in memory.** Factor the importer's `AddShape` and `WriteChunks`
   into a shared pure builder used by both the importer and the game. A vine made
   in memory must pass `flex <vine> 40` like a baked one. Re-measure memory and
   chunk time.
3. **Valid-only generation:** shape checks, the physics test and deterministic
   re-roll, with rejection rates in `vinecheck.gd`.
4. **Leaves out of the voxel body:** meshes, collision on the section body,
   health, tearing off, bent by the shader. Tendrils removed. Add a `flex` phase
   that stands on and hooks a leaf.
5. **Colour and size ranges** per kind.
6. **`NestKit` and `NestBuilder` on the planet,** sized by population.
   `lvl_nest_lab`. A `nest` bench mode: load time per stage, frame time from the
   ground, the canopy and above, and memory. Measure the 25,000 case.
7. **Pods and hanging pickles (0034):** bulk pod state, pickles hanging and
   dropping when aggravated, regrowth, the win condition and bonus. A bench
   phase for 25,000 hanging pickles.
8. **The main-screen tool:** Nest tab, range lab, vine panel, Play, Good/Bad/Save,
   both logs with their about lines.
9. **Decide 0033** from the step-6 numbers. Write the decision records for the new
   choices: leaves, nest sizing, the planet radius, and the tool.

## Lessons from this session

- **Test editor code in the editor, not only headless scripts.** Run
  `--headless --editor --quit-after 300` and print what the plugin read. Headless
  `--script` runs instantiate full C# classes; the editor gives non-`[Tool]` classes
  placeholders.
- **Claim mouse input in the preview** (`AcceptEvent()`), or the dock's scroll
  container takes the wheel.
- **Bench screenshots overwrite each other between runs.** Move them into a
  folder per run.
- **Cool the laptop 40–60 s between bench runs.** Read worst frames from runs
  without screenshot phases.
- **Bash heredocs with backticks or unbalanced quotes fail here.** Write scripts
  with the Write tool, then run them. Python `\n` inside replaced C# strings becomes
  a real newline.

## Done for the next session

At least steps 1–3, each committed. Report how close the 25,000-pod nest is to the
budget as soon as step 6 can measure it. Show screenshots of each visual step.
