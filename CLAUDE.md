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
| Physics | `physics/3d/physics_engine="Jolt Physics"`, explicit. **`DEFAULT` is not Jolt in 4.7.2.** Never fall back to Godot Physics; raise it instead. |
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
   is one, `vox_importer`.

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

**`Session` is the third autoload and carries state only**: the offers and the
accepted contract, across scene changes. A level built after it loads names a
`Builder` on its `LevelRoot`; a loading screen covers it and the player is
made once the builder says the frames have settled. See
`docs/decisions/0021`.

## Folder map

```
addons/         our own editor plugins only (vox_importer)
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
docs/           pitch.md, plan.md, decisions/, project_board.json
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
"$GODOT" --path . --script scripts/debug/bench.gd -- res://scenes/levels/lvl_horde_flats.tscn gpuab     # what each part of the view costs the GPU
"$GODOT" --path . --script scripts/debug/bench.gd -- res://scenes/levels/lvl_horde_flats.tscn siege 25000  # crowd vs player and allies
"$GODOT" --path . --script scripts/debug/bench.gd -- res://scenes/levels/lvl_horde_flats.tscn horde 25000
"$GODOT" --path . --script scripts/debug/bench.gd -- res://scenes/levels/lvl_planet_test.tscn planet 25000  # ground, 300 m, 1 km, orbit, crowd
"$GODOT" --path . --script scripts/debug/bench.gd -- res://scenes/levels/lvl_planet_test.tscn planetab      # what the planet costs the GPU
"$GODOT" --path . --script scripts/debug/bench.gd -- res://scenes/levels/lvl_planet_test.tscn planets 10 1   # ten seeds from 1: build, ground, heights
"$GODOT" --path . --script scripts/debug/bench.gd -- res://scenes/levels/lvl_contract.tscn descent          # from the ship in orbit down to the compound
"$GODOT" --path . --script scripts/debug/bench.gd -- res://scenes/levels/lvl_contract.tscn garrison         # the horde at home: spread round its nest, 40 s
"$GODOT" --path . --script scripts/debug/bench.gd -- res://scenes/levels/lvl_ship_hub.tscn flow             # ship, computer, contract taken, loaded, in orbit
"$GODOT" --headless --path . --script scripts/debug/seedcheck.gd -- 20                                      # same seed, same planet; cube seams
"$GODOT" --headless --path . --script scripts/debug/graphcheck.gd -- 40                                     # chunk graph vs a cell flood: what falls, what floats
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
  The summary is the file to read first or hand on. Game code reports through
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
