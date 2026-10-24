# Dill Control

A first-person horde shooter in Godot **4.7.2-stable mono (C#)** with Jolt
physics. A contract exterminator clears a planet of twenty-five thousand pickles
from a grappling line and a hoverbike. `docs/pitch.md` is the game;
`docs/plan.md` is the road from here to a vertical slice.

The prototype that proved it is the **lab**, `C:\Projects\fps-swampy`. Its
`CLAUDE.md` holds the measured reasoning behind most rules below, and its code
is what gets ported. Port from it; never copy its content generators.

This file is rules only. A decision with more than one defensible answer gets a
record in `docs/decisions/`, and a line here pointing at it.

---

## The requirement

**60 fps at 1920x1080 native on an 11th-gen Core i5 with 16 GB and Iris Xe.**
A frame that misses it is a bug, on the same footing as a crash. Recorded in
`project.godot` under `[dill_control]` too. See `docs/decisions/0002`.

- Render scale ships at 1.0. Lowering it is a diagnostic (fill cost or not?),
  never a way to meet the target.
- Measured with `bench.gd` on the target machine, never by eye. Read
  `gpu mean`, the worst frame and the count over 16.7 ms, not an average.
- Pair every comparison: baseline, change, baseline, on a machine that has been
  idle. A hot laptop drifts by milliseconds with nothing changed.

## Verified environment

| Thing | Value |
|---|---|
| Engine | `4.7.2.stable.mono.official.ed1daf0bf`, on the Desktop, not on PATH: `C:\Users\Elena\Desktop\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe` |
| Framework | `net8.0`, `Godot.NET.Sdk/4.7.2`, assembly `DillControl` |
| Renderer | Forward+, D3D12 |
| Physics | `physics/3d/physics_engine="Jolt Physics"`, explicit. **`DEFAULT` is not Jolt in 4.7.2.** Never fall back to Godot Physics; raise it instead. Jolt's body limit is raised to 32,768 (`jolt_physics_3d/limits/max_bodies`); past it bodies silently fail and hinges error. `!bodies` in a bench timeline counts them. |
| Input device ids | Keyboard events arrive as device `16`, mouse as `32`. A stored event with device `0` never matches. Hand-edited input maps keep `16`/`32`, or `-1` for any. |
| Art facing | **MagicaVoxel: verified.** A `.vox` model's front (`-Y`) arrives facing Godot's `+Z` (`Vector3.ModelFront`); the tank in `lvl_pipeline_test`. **Blender: not yet verified.** Expected the same from Blender's `-Y`; confirm with a Blender test asset and replace this half. |
| Art scale | **MagicaVoxel: verified,** 0.1 m per voxel (import option `voxel/size`). **Blender: not yet verified.** Expected: 1 Blender unit = 1 m with transforms applied. |

Joypad events in the input map use device `-1` (any pad). The layout is
`docs/decisions/0025`.

## Rules

1. **Authored over generated.** If a designer would want to move it, it is a
   node in a scene, not a number in a generator. Generators are for what no one
   places by hand: terrain, the horde, crater shapes. See `docs/decisions/0004`.
2. **Data over code.** A number that can be argued about lives in a
   `[GlobalClass] Resource` with `[Export]`s and a `.tres`. Changing it never
   needs a rebuild. One resource per system; no god-resource.
3. **Signals and exports, never node paths.** No `GetNode("../..")`. Inside a
   scene, collaborators are `[Export]` references; parent-child traffic is local
   signals; the `EventBus` autoload is only for traffic whose sender cannot name
   its listener, and each signal on it is justified in a decision record.
4. **Imported models are wrapped, never edited.** Every `.glb` is instanced by
   a small `.tscn` that adds scripts, hitboxes and collision. Re-exporting from
   Blender replaces the mesh and leaves the wrapper alone.
5. **One system at a time:** ported, benched, committed. Never leave the
   project broken across more than one commit.
6. **No function keys.** Development is on a laptop where they need `Fn`. Every
   debug key is a letter.
7. **No third-party addons** without a proposal and a reason. Our own editor
   plugins live in `addons/` because Godot looks for them nowhere else; there
   are two, `vox_importer` and `vine_gen`.

## The level tree

Every level has exactly these branches, in this order, even when empty:

```
Level        (Node3D, LevelRoot.cs)
├── Geometry     static world collision and its meshes
├── Water
├── Foliage
├── Props        interactive and set-dressing objects, authored set pieces
├── Lighting     DirectionalLight3D, WorldEnvironment, local lights
├── Audio
├── Spawns       player and encounter markers
└── Navigation
```

`LevelRoot` instances the player at `Spawns/PlayerSpawn`; **the player scene is
never saved into a level.** It also adds `PerfDriver`, so every level records
with `U` and benches with no wiring.

**The planet sits at the world origin** and everything on it is placed through
a `PlanetSite`: placed roughly, levelled and stood on the ground at build. Code
with no body to ask `GetGravity()` asks `WorldUp`, never `Vector3.Up`. See
`docs/decisions/0017`.

**Generation draws from authored pools and one seed.** A contract is a seed;
every roll goes through `SeededRandom` with a named stream, never `GD.Randi`
or `System.Random`. A planet rolls inside a `PlanetArchetype`'s ranges and
wears a `PlanetBiome`; a range widens only after `bench.gd planets` holds at
it. See `docs/decisions/0018`. A compound is a seeded layout of authored
cluster scenes from a `CompoundKit`: the seed decides where, never what
(`docs/decisions/0020`).

**A vine nest is grown at load** (`docs/handoffs/vine-tool.md`, step 6;
whether to keep that or pick saved nests is `docs/decisions/0033`). A
`NestKit` (`resources/mission/nest_kit_default.tres`) names the kinds and the
layout; `NestBuilder`, the level's builder over `SystemBuilder`, plans the
nest from the contract's seed and population (one pod per pickle) and grows
and builds its vines on all cores while the planet generates; `NestPlacer`, a
site chooser (`PlanetTerrain.MoreSiteChoosers`), lays it at the flattest of
a few places on levelled pads. **A nest is one clump**: vines 55-70 m apart
inside 400 m, more pods a vine before more room. No compound or city goes
with a nest. `lvl_nest_lab` is a contract level with a nest, no ship and no
compound: `pop=N`, `nest=SEED`, `horde=0`, `pickles=0` after `--`.
A vine stands on its own foot; a crater under it unroots it (`PlantFlex.Unroot`).

**Every pickle starts on a pod** (`docs/decisions/0034`). `PodField` holds a
nest's pods in flat arrays, never a node a pod: each hangs from a voxel of its
vine and goes with it, and its pickle is drawn instanced and bent on the GPU
(`pickle_hang`). Hanging pickles are shot through a pool of `PodHitProxy`
on those nearest the aim, and blasts through the bus. The colony never
strikes first (`docs/decisions/0029`): harm to any of it wakes it, and over
`WakeSeconds` every pickle drops into the horde (`SpawnOrigin.Pod`), which
has none of its own. Numbers: `resources/mission/pod_default.tres`.

**Static voxel bodies are on World and Structure, never Prop.** The planet's
gravity field watches Prop for loose pieces; watching a nest's 64,000 static
boxes too cost Jolt 16 ms a tick. A query that looks for props looks for
Structure too (`GameLayers`). Torn leaves are on Litter: they fall and rest
but never push the player.

**`Session` is the third autoload and carries state only**: the offers and the
accepted contract, across scene changes. A level built after it loads names a
`Builder` on its `LevelRoot`; a loading screen covers it and the player is
made once the builder says the frames have settled. See
`docs/decisions/0021`.

## Folder map

```
addons/         our own editor plugins only (vox_importer, vine_gen)
art/
  _source/      .blend and paint sources, packs as downloaded. .gdignore'd, LFS.
  models/       .glb exports
  voxels/       .vox files, imported directly (they are source and export at once)
  textures/     palette.png and the few textures that are not the palette
  materials/    shared .tres materials models are mapped onto
audio/          recordings
scenes/         player/ weapons/ enemies/ props/ levels/ vehicles/ ui/
scripts/        core/ player/ weapons/ enemies/ horde/ voxel/ planet/
                vehicles/ mission/ levels/ ui/ audio/ debug/
resources/      mirrors scripts/
shaders/
tools/          project-board/, vox-tools/ (and voxel-bench/ from M2). .gdignore'd.
docs/           pitch.md, plan.md, decisions/, handoffs/, project_board.json
perflogs/       recordings and bench screenshots. Gitignored.
```

`scripts/debug/` is tooling that ships with the project and not the game.
Folders appear when their first file does.

## Naming

**`PascalCase`** for C# classes and files, node names and `[Export]`s.
**`snake_case`** for `.tscn`, `.tres`, `.gdshader`, `.glb` and `.blend`.

| Prefix | Meaning |
|---|---|
| `lvl_` | level |
| `prp_` | prop or set piece |
| `wpn_` | weapon scene or stats |
| `nme_` | enemy kind or boss |
| `shp_` | ship |
| `fx_`  | short-lived effect |
| `mat_` | material |
| `snd_` | sound bank or event |
| `vxl_` | voxel model |
| `mch_` | mech or other walker |

## Art

The pipeline, from a model to a placed, destructible, animated thing, is
`docs/art-to-game.md`; the reasoning is `docs/decisions/0005`. In short:
Blender, `.glb` export, every model UV-mapped onto `art/textures/palette.png`
and drawn with `art/materials/mat_palette_toon.tres` (toon diffuse and specular,
nearest filtering). The outlines are `shaders/toon_ink.gdshader`, a depth-only
screen pass carried by `ToonPass` on the camera. Smoke and dust are
`shaders/fx_toon_puff.gdshader`: opaque cartoon balls the ink outlines, never
soft alpha sprites. Pickles are animated in the vertex shader, not rigged.

Voxel art is MagicaVoxel `.vox`, saved into `art/voxels/` and imported by our
own importer: one node per MagicaVoxel object, colour per vertex on
`art/materials/mat_voxel_toon.tres`, emission from MagicaVoxel's Emit
materials, palette index 255 marks a part's pivot, objects named `@Name` become
markers. See `docs/decisions/0009` and `docs/art-to-game.md`.
Everything not ours is listed in `credits.md` the day it arrives. Mechs are
animated by code, never clips: `docs/decisions/0010`. A melee stroke is the
same, poses in a resource rather than a clip: `docs/decisions/0028`.

**Every voxel model is destructible, the same way** (`docs/decisions/0011`): a
`VoxelBody` in its wrapper (static for buildings, hinged parts for mechs). No
voxel thing gets damage code of its own; its numbers are in
`resources/voxel/voxel_destruction_default.tres`, or for armoured things
(mechs) `voxel_destruction_mech.tres`, or for trees `voxel_destruction_tree.tres`
(no weight check: only what is cut free falls, `docs/decisions/0024`). A stats
resource sets how its own model splits; loose pieces and every budget always
use the default. How much dust a kind of thing raises is its own resource's
`DustScale` (cloud width, after the size cap), and its loose pieces keep it. Buildings carry their own weight
(`docs/decisions/0013`); what comes loose falls whole, crushes through storey
by storey, hangs on a hinge when it went on one side, and snaps where it lands
(`docs/decisions/0014`). Buildings decide what falls by a chunk graph baked by the importer: a cut chunk splits along the cut, pieces break and crush by chunks, overloaded edges give way in stages, and the gravity gun tears chunks out (`docs/decisions/0027`).

**Plants bend** (`docs/decisions/0032`, proposed). A vine or tree wrapper
has a `Flex` node (`PlantFlex`) after its model: a spine built from its
voxels, stepped by `FlexField`, drawn by `voxel_toon` from one shared texture,
with its collision sections moved along (and carrying what stands on them).
Wind, hits and the grapple push it; a cut softens it, and cut weak enough it
gives way and falls over from the pose it was in. Hits on a bent body go
through `VoxelBody.Bend` back to its voxels. A static `VoxelBody` with
`WholeUntilChanged` draws its imported model (and coarse 2x/4x copies of its
voxels further off, `CoarseFromMetres` in its stats) until anything about it
changes, and only then meshes its sections: a forest of 200 vines draws in
about 200-450 calls instead of 10,000. Its numbers are
`resources/foliage/flex_vine.tres` / `flex_tree.tres` and `wind_default.tres`.

**A vine's leaves are meshes, not voxels** (`VineLeaves`, the wrapper's
`Leaves` node): a few heart-shaped blades a kind (`LeafMesh`), drawn as
MultiMeshes in `mat_leaf_toon`, which bends by the same
`shaders/flex_bend.gdshaderinc` as `voxel_toon`. Each leaf is one thin box on
its section's existing body (`VoxelPart.AddExtraShape`): stood on, hooked,
shot through `VoxelBody.Intercept`; torn off whole under the loose-piece cap,
and a leaf whose stalk or arm is cut away falls. A baked vine's leaves are
`art/voxels/prp_vine_<kind>_<seed>_leaves.tres`; their numbers
`resources/foliage/leaf_default.tres`. Vines have no tendrils.

**Vines are baked, never grown in the game** (`docs/decisions/0033`,
proposed). The Vines dock (`addons/vine_gen`) rolls a vine from a kind
(`resources/foliage/vine_kind_*.tres`) and a seed, previews it and bakes
`art/voxels/prp_vine_<kind>_<seed>.vox` and `scenes/props/prp_vine_<kind>_<seed>.tscn`,
the wrapper above, with the stalk's foot on the scene origin. Its colours are
its kind's palette (`vine_palette_<kind>.tres`, else `vine_palette.tres`):
deep greens, never neon or lime. Each vine rolls a shade of it from its seed
and each leaf a little more, held inside the palette's green band. Its checks
and voxel size are `vine_bake.tres`. A seed grows the same vine on every machine: its rolls are
`SeededRandom` streams and its trigonometry is `Fdlibm`'s, never .NET's.
`vinecheck.gd` holds the generator to its recorded golden vines
(`scripts/debug/vine_golden.json`); a change meant to move them re-records
with `vinecheck.gd -- record`. **Only valid vines are baked or placed:**
`VineGrower` rolls a seed that fails `VineValidator` (shape, pods, taper,
lean, the spine's load; thresholds in `vine_bake.tres`) again from the seed
and an attempt number, so a seed is always one valid vine; the generator
keeps only pods a pickle can hang from. Jolt never decides a roll.
`VineFactory` builds the same vine in memory, no files, through the
importer's own `VoxModelBuilder`.

**The Vines tool** is `addons/vine_gen`'s main screen (a tab beside 2D and
3D). Its Nest tab plans a nest with the game's own `NestLayout` from a kit,
a seed and a population, stands it on a flat disc (the planet is not drawn:
its classes are placeholders in the editor), reports its measures, and
Play loads `lvl_nest_lab` with it through `user://nest_play.json`
(`NestPlay`, taken and deleted by `NestBuilder`); the lab records how the
play went (`NestPlaytest`), and Good/Bad go to
`tools/vine-logs/nest_verdicts.jsonl` (Save keeps a Good one as a
`NestPreset`). The range lab rolls N seeds of a kind as they roll, before
any re-roll, judges each, logs Good/Bad to `vine_verdicts.jsonl`, shows
each setting's spread, and edits and saves the kind's ranges. **The logs
are training data: never edit or prune them.** Check it in the editor:
`"$GODOT" --path . --editor -- nesttab-check=<png>` or
`rangelab-check=<prefix>` (their verdicts go to `user://`, not the logs). A tool's C# signal handlers do not survive the
editor reloading the project's code, so the plugin rebuilds its panel and
dock on the first frame after a reload; `--editor -- reload-check` proves it
(rebuild the code under it, then make `user://reload_go`). **It once
reset every `art/voxels/*.vox.import` to the importer's defaults** (voxel
size 0.1): snapshot them first and restore them after, or run it on a copy.
What comes next is `docs/handoffs/vine-tool-4.md`.

**Nothing that scales with a model's voxels runs in the frame**
(`docs/decisions/0015`): re-meshing, structural checks and splits go to
`VoxelWorkers` from a copy of the cells and are carried out only if the grid's
`Version` has not moved. The main thread removes cells, re-boxes what must
collide now and makes bodies, within the allowances in the stats resource.
Nothing disappears before what replaces it is drawn, and crushed material
heaps up as rubble rather than vanishing (`docs/decisions/0016`). Past the
loose-piece cap a piece becomes dust before it is made, only a piece on the
ground joins a heap, a piece at rest is re-boxed finely, and a building tells
the horde when its footprint changes (`docs/decisions/0022`). To see a
collapse frame by frame: `"$GODOT" --path . --write-movie <dir>/f.png
--fixed-fps 10 --script scripts/debug/bench.gd -- <level> cut quiet`.

**Vines leave no rubble** (`LeavesRubble` off in
`voxel_destruction_vine.tres`, carried by their pieces as `DustScale` is):
nothing of a vine is heaped or baked into a heap. A felled vine falls in big
dark dust (`FallDust*` in `flex_vine.tres`), does not crush through, snaps
into logs where it lands (`SnapReachM`, `SnapMinVoxels`), and its pieces
crumble after `CrumbleAfterSeconds`. A voxel part's up is the ground's
(`WorldUp`). `bench.gd ... flex <vine> 12 rec` records a rocket-felled vine.

What still needs modelling, part by part, is `docs/models.md`. After changing the importer,
force a reimport (delete `.godot/imported/*.vox-*`): Godot skips unchanged
sources.

Git LFS tracks every binary asset (`.gitattributes`), `.vox` included. Godot's text formats stay
in git proper.

## Workflow

```
dotnet build
GODOT="/c/Users/Elena/Desktop/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe"
"$GODOT" --path . --script scripts/debug/bench.gd                                       # the contract level on its fallback seed, idle
"$GODOT" --path . --script scripts/debug/bench.gd -- res://scenes/levels/<lvl>.tscn look  # screenshots
"$GODOT" --path . --script scripts/debug/bench.gd -- res://scenes/levels/<lvl>.tscn ink   # ink A/B
"$GODOT" --path . --script scripts/debug/bench.gd -- res://scenes/levels/lvl_horde_flats.tscn grapple
"$GODOT" --path . --script scripts/debug/bench.gd -- res://scenes/levels/lvl_horde_flats.tscn mech
"$GODOT" --path . --script scripts/debug/bench.gd -- res://scenes/levels/lvl_horde_flats.tscn bike
"$GODOT" --path . --script scripts/debug/bench.gd -- res://scenes/levels/lvl_horde_flats.tscn mechs     # all three allies
"$GODOT" --path . --script scripts/debug/bench.gd -- res://scenes/levels/lvl_horde_flats.tscn mechsab   # what vehicles cost to draw
"$GODOT" --path . --script scripts/debug/bench.gd -- res://scenes/levels/lvl_horde_flats.tscn arsenal   # designator, burner, lance, manipulator
"$GODOT" --path . --script scripts/debug/bench.gd -- res://scenes/levels/lvl_horde_flats.tscn slam      # sledge: ground, crowd, voxel, wall, miss
"$GODOT" --path . --write-movie perflogs/slam/f.png --fixed-fps 10 --script scripts/debug/bench.gd -- res://scenes/levels/lvl_horde_flats.tscn slam quiet
"$GODOT" --path . --script scripts/debug/bench.gd -- res://scenes/levels/lvl_horde_flats.tscn wreck     # the compound shot down
"$GODOT" --path . --script scripts/debug/bench.gd -- res://scenes/levels/lvl_horde_flats.tscn mechwreck # the allies shot apart
"$GODOT" --path . --script scripts/debug/bench.gd -- res://scenes/levels/lvl_horde_flats.tscn collapse  # a skyscraper brought down
"$GODOT" --path . --script scripts/debug/bench.gd -- res://scenes/levels/lvl_horde_flats.tscn cut       # a tower cut through: drops whole, crushes down
"$GODOT" --path . --script scripts/debug/bench.gd -- res://scenes/levels/lvl_horde_flats.tscn cutside   # cut on one side: leans, tears, topples
"$GODOT" --path . --script scripts/debug/bench.gd -- res://scenes/levels/lvl_horde_flats.tscn cut quiet # the same, every frame recorded
"$GODOT" --path . --script scripts/debug/bench.gd -- res://scenes/levels/lvl_horde_flats.tscn lasercut # the bike's beam swept through Tower11
"$GODOT" --path . --script scripts/debug/bench.gd -- res://scenes/levels/lvl_horde_flats.tscn tree quiet # rockets into a tree's canopy, then its trunk cut
"$GODOT" --path . --script scripts/debug/bench.gd -- res://scenes/levels/lvl_horde_flats.tscn cut quiet 40  # recorded for 40 s: does the debris drain?
"$GODOT" --path . --script scripts/debug/bench.gd -- res://scenes/levels/lvl_horde_flats.tscn downtown 30 noprobe  # eight towers cut, rockets into the rubble, 30 s after
"$GODOT" --path . --script scripts/debug/bench.gd -- res://scenes/levels/lvl_horde_flats.tscn gpuab     # what each part of the view costs the GPU
"$GODOT" --path . --script scripts/debug/bench.gd -- res://scenes/levels/lvl_horde_flats.tscn siege 25000  # crowd vs player and allies
"$GODOT" --path . --script scripts/debug/bench.gd -- res://scenes/levels/lvl_horde_flats.tscn horde 25000
"$GODOT" --path . --script scripts/debug/bench.gd -- res://scenes/levels/lvl_planet_test.tscn planet 25000  # ground, 300 m, 1 km, orbit, crowd
"$GODOT" --path . --script scripts/debug/bench.gd -- res://scenes/levels/lvl_planet_test.tscn planetab      # what the planet costs the GPU
"$GODOT" --path . --script scripts/debug/bench.gd -- res://scenes/levels/lvl_planet_test.tscn planets 10 1   # ten seeds from 1: build, ground, heights
"$GODOT" --path . --script scripts/debug/bench.gd -- res://scenes/levels/lvl_contract.tscn descent          # from the ship in orbit down to the compound
"$GODOT" --path . --script scripts/debug/bench.gd -- res://scenes/levels/lvl_contract.tscn garrison         # the horde at home: spread round its nest, 40 s
"$GODOT" --path . --script scripts/debug/bench.gd -- res://scenes/levels/lvl_nest_lab.tscn nest pop=25000  # a nest for 25,000: ground, canopy, above; load by stage and memory (horde=0 without the horde)
"$GODOT" --path . --script scripts/debug/bench.gd -- res://scenes/levels/lvl_nest_lab.tscn colony pop=25000  # its pickles hanging, calm; harmed; waking over 30 s; in the fight
"$GODOT" --path . --script scripts/debug/bench.gd -- res://scenes/levels/lvl_ship_hub.tscn flow             # ship, computer, contract taken, loaded, in orbit
"$GODOT" --headless --path . --script scripts/debug/seedcheck.gd -- 20                                      # same seed, same planet; cube seams
"$GODOT" --headless --path . --script scripts/debug/graphcheck.gd -- 40                                     # chunk graph vs a cell flood: what falls, what floats
"$GODOT" --headless --path . --script scripts/debug/flexcheck.gd                                            # a plant's spine: its period, settling, a pull, a cut
"$GODOT" --headless --path . --script scripts/debug/vinecheck.gd                                            # vine generator vs its golden vines; every baked vine's wrapper and foot
"$GODOT" --headless --path . --script scripts/debug/vinecheck.gd -- record                                  # re-record the golden vines, only after a change meant to move them
"$GODOT" --headless --path . --script scripts/debug/podcheck.gd -- pop=3000                                 # a nest's pods: in the field, on voxels, bending; a shot, a blast; the wake into the horde; regrowth; a felled vine takes its own; the win and the bonus
"$GODOT" --headless --path . --script scripts/debug/vinecheck.gd -- rejections 200                          # per kind: how often a seed's roll is valid, why not, how each measure spreads
"$GODOT" --headless --path . --script scripts/debug/vinecheck.gd -- settle                                  # every baked vine stood 5 s with Jolt: no drift, nothing loose or sunk
"$GODOT" --path . --script scripts/debug/vinelook.gd -- perflogs/vines.png                                   # every baked vine in a row, photographed
"$GODOT" --path . --script scripts/debug/vinelook.gd -- perflogs/vines_above.png 50                        # the same from 50 deg up; `leaves` instead of 50 lays out every kind's leaf shapes
"$GODOT" --headless --path . --script scripts/debug/vinebake.gd -- fruiting pool:0                          # bake a vine as the dock does (then --editor --quit to import)
"$GODOT" --path . --script scripts/debug/bench.gd -- res://scenes/levels/lvl_horde_flats.tscn flex          # a vine in the wind, rocketed, swung on, stood on, bitten and felled
"$GODOT" --path . --script scripts/debug/bench.gd -- res://scenes/levels/lvl_horde_flats.tscn flex prp_vine_fruiting_4532344 40  # any vine by name; shots at 40 m keep it standing
"$GODOT" --path . --script scripts/debug/bench.gd -- res://scenes/levels/lvl_horde_flats.tscn flex mem:fruiting:4532344 40  # the same, grown in memory in its place
"$GODOT" --path . --script scripts/debug/bench.gd -- res://scenes/levels/lvl_horde_flats.tscn forest        # 85 vines 100 m apart (prp_vine_forest) round the middle: spawn, edge, among them, a swing
"$GODOT" --path . --script scripts/debug/bench.gd -- res://scenes/levels/lvl_horde_flats.tscn forestab      # what the forest costs the GPU: shadows, ink, bending, the forest
"$GODOT" --path . --script scripts/debug/bench.gd -- res://scenes/levels/lvl_horde_flats.tscn collapse sink  # any mode + sink: pieces under the ground or with boxes off their voxels
"$GODOT" --path . --script scripts/debug/bench.gd -- res://scenes/levels/lvl_horde_flats.tscn cut quiet 13 boxes nodust  # see what breaks: boxes (collision), wire, nodust
"$GODOT" --path . --script scripts/debug/bench.gd -- res://scenes/levels/lvl_horde_flats.tscn hordeab 25000
```

- `bench.gd` waits for the draw count to settle, then reports per phase: mean,
  worst, frames over budget, GPU mean, draws, the window size it actually got,
  and the CPU split from `PhysicsStepProbe` (physics step, physics scripts,
  process scripts, render and wait). Bench modes are added with the systems
  they measure; the lab's `bench.gd` has the full set.
- A bench run compiles every shader against the real driver, so it is how a
  shader edit is checked. `--headless` compiles none.
- A screenshot (`#name`) costs 100-150 ms in its own frame; never read a worst
  frame from a phase that takes one. Status lines (`@status`) and the recorder
  add hitches of their own; read worst frames from a quiet run.
- A bench window is fullscreen and captures the mouse: keys and clicks while it
  runs reach the player (`@status` shows `fire=`). Hands off, and nothing else
  running -- a stuck game in the background halves the frame rate.
- `U` in any level records every frame to `perflogs/` (a REC label in the
  corner while it runs) and writes `perf_<time>_summary.txt` beside the CSV:
  the machine and its power state, the worst frames each with its sections,
  counts and events, what the spikes were spent on, and how the counts grew.
  The summary is the file to read first or hand on. Beside it,
  `perf_<time>_probe.txt` is SinkProbe's: pieces sunk, hanging or misfit, and
  a census of the loose pieces every 5 s. The probe is billed to "physics step
  [engine]" (about 4 ms a tick with a hundred pieces): the word `noprobe`
  measures without it. Game code reports through
  `PerfLog`: `PerfLog.Counter` once at load for a column, `PerfLog.Event`
  inside `if (PerfLog.Recording)` for the events column.
  `cut quiet` / `cutside quiet` record the run that way, with no screenshots
  and nothing printed while recording: a console line costs the frame it is
  printed in 15-40 ms on the target machine. Read hitches from these, with
  every section's time in the CSV beside each one.
- A section registered after a recording starts has no column in it:
  register `FrameProfiler` sections in a class that exists at load.
- Measure plugged in. On battery Windows holds the frame to the display's
  60 Hz and the GPU clocks down: every phase reads 16.6 ms and the GPU about
  twice its plugged-in time, whatever is on screen. `gpuab` switches the sun's
  shadows, the horde's draws, downtown and the fog off in turn.
- The game raises itself above normal priority on Windows
  (`ProcessPriority`). Cool the laptop 40-60 s between runs: back-to-back
  runs throttle it and the level alone goes 20-30 % over.
- After a fresh clone or new asset: `"$GODOT" --headless --path . --editor --quit`
  imports everything.
- The board: open `tools/project-board/index.html`, connect the project folder,
  and `docs/project_board.json` becomes the copy git keeps.

Commit at every point where the project builds and runs. Short, factual
messages.

**Handoffs live in the repo, always.** A handoff, plan or brief written for
another session is saved in `docs/handoffs/<topic>.md` and committed. Never
save one outside the project: not in a session scratchpad, temp folder or
home directory, where the next session cannot find it.
