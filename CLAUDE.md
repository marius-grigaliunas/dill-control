# Dill Control

A first-person horde shooter in Godot **4.7.2-stable mono (C#)** with Jolt
physics. A contract exterminator clears a planet of twenty-five thousand giant
cucumbers from a grappling line. `docs/pitch.md` is the game; `docs/plan.md` is
the road to the vertical slice (onboarding and three contracts,
`docs/decisions/0051`). In the fiction the enemies are cucumbers (`0050`); code,
files and bench modes keep "pickle".

The prototype that proved it is the **lab**, `C:\Projects\fps-swampy`. Its
`CLAUDE.md` holds the measured reasoning behind many rules below. Port from it;
never copy its content generators.

This file is rules only. A decision with more than one defensible answer gets a
record in `docs/decisions/` (only what is still true; an overwritten record is
deleted), and a line here pointing at it. An unsettled idea goes in `docs/ideas/`.

---

## The requirement

**60 fps at 1920x1080 native on an 11th-gen Core i5 with 16 GB and Iris Xe.**
A frame that misses it is a bug, on the same footing as a crash (`0002`; also in
`project.godot` under `[dill_control]`).

- Render scale ships at 1.0. Lowering it is a diagnostic, never a way to meet
  the target.
- Measured with `bench.gd` on the target machine, never by eye. Read `gpu mean`,
  the worst frame and the count over 16.7 ms, not an average.
- Pair every comparison: baseline, change, baseline, on an idle machine.
- Measure plugged in (on battery every phase reads 16.6 ms). Cool the laptop
  40-60 s between runs. Hands off during a bench; nothing else running.

## Verified environment

| Thing | Value |
|---|---|
| Engine | `4.7.2.stable.mono.official.ed1daf0bf`, not on PATH: `C:\Users\Elena\Desktop\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe` |
| Framework | `net8.0`, `Godot.NET.Sdk/4.7.2`, assembly `DillControl` |
| Renderer | Forward+, **Vulkan** (`0048`) |
| Physics | `physics/3d/physics_engine="Jolt Physics"`, explicit. **`DEFAULT` is not Jolt in 4.7.2.** Never fall back to Godot Physics. Body limit raised to 32,768 (`jolt_physics_3d/limits/max_bodies`). |
| Input device ids | Keyboard `16`, mouse `32`, joypad `-1` (any). A stored event with device `0` never matches. Layout: `0025`. |
| Art facing | MagicaVoxel `.vox` front (`-Y`) arrives facing Godot `+Z`: verified. Blender `.vox` mechs verified through `tools/blender/dill_mech`. Blender `.glb`: not yet verified. |
| Art scale | MagicaVoxel 0.1 m per voxel: verified. Blender 1 unit = 1 m with transforms applied: not yet verified. |

## Rules

1. **Authored over generated** (`0004`). If a designer would want to move it, it
   is a node in a scene. Generators are for terrain, the horde, craters and nests.
2. **Data over code.** A number that can be argued about lives in a
   `[GlobalClass] Resource` with `[Export]`s and a `.tres`; changing it needs no
   rebuild. One resource per system; no god-resource.
3. **Signals and exports, never node paths.** No `GetNode("../..")`. Collaborators
   inside a scene are `[Export]` references; the `EventBus` autoload is only for
   traffic whose sender cannot name its listener, each signal justified in a
   record (`0006`).
4. **Imported models are wrapped, never edited.** Every `.glb` is instanced by a
   small `.tscn` that adds scripts, hitboxes and collision.
5. **One system at a time:** ported, benched, committed. Never leave the project
   broken across more than one commit.
6. **No function keys.** Every debug key is a letter.
7. **No third-party addons** without a proposal. Our own editor plugins live in
   `addons/`: `vox_importer`, `vine_gen`, `editor_bridge`, `terrain_brush`,
   `asset_lab`.
8. **Claude works with Godot through the editor bridge** (`0044`) whenever the
   editor is open: look before and after every change (`view`), change through
   commands (one undo each), test with `run` and `output`. **When the toolset
   cannot do what the work needs, extend it** (`addons/editor_bridge`, or
   `Bridge.Register` in the tool's own plugin). Never hand-edit an open scene's
   `.tscn`. Headless scripts (`bench.gd`, the checks) are for what needs no editor.
9. **A handbuilt level is one the user can open and see**: every vine, prop,
   marker and pod drop is a real node in the scene, so it can be moved, swapped
   or deleted by hand. On a sculpted ground the ground is one too. On a planet
   the ground is built at load (a level the user asks for on a planet is built,
   see 10), so everything on it is placed roughly and set on the ground by a
   `PlanetSite` or `PlacedNestSite`. A scene's own `.tscn` is never the way to
   hide content from the editor. The onboarding level is `lvl_tutorial`
   (`0055`): a pinned planet, a baked nest scene on a `PlacedNestSite`, hooks
   on another, `PodLanding` dropping the player in a pod seen from inside, and
   `CloneBank` printing a new body when one is lost (`0053`).
10. **HARD RULE: levels are built the way a human edits them.** Open any level in
    the editor and everything in it is there to see: ground, props, vines, nests,
    markers, lights, spawn. The user places, moves, sculpts and decorates in the
    editor, then plays; a level that opens as an empty viewport and fills in only
    at runtime is wrong. Never make a level by copying another level's `.tscn`
    and rewiring it by text, and never call a wiring test a level. Look at the
    result in the editor (`view`) before saying it is done. If the ground cannot
    exist in the editor (a planet), say so and ask before building anything on it.

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
never saved into a level.** It also adds `PerfDriver`, so every level records with
`U` and benches with no wiring.

**The planet sits at the world origin** and everything on it is placed through a
`PlanetSite` (`0017`). Code with no body to ask `GetGravity()` asks `WorldUp`,
never `Vector3.Up`.

**Generation draws from authored pools and one seed** (`0018`). A contract is a
seed; every roll goes through `SeededRandom` with a named stream, never
`GD.Randi` or `System.Random`. Nests pin the planet at 1,260 m (`0037`).

**`Session` is the third autoload and carries state only** (`0021`): the offers
and the accepted contract across scene changes. A level built after it loads names
a `Builder` on its `LevelRoot`; a loading screen covers it.

## The nest

A nest is one clump of vines (`0038`: 55-70 m apart inside 400 m), grown at load
from the contract's seed and population (`0033`: 25 s at 25,000; a saved nest is a
`NestPreset`; `nest_bake` turns one into an authored scene to edit by hand, `0056`). `NestKit` names the kinds and layout;
`NestBuilder` plans and grows it; `NestPlacer` lays it at the flattest site. A
single vine is baked only for a set piece.

- **Every cucumber starts on a pod** (`0034`, `0040`): `PodField` holds pods in
  flat arrays, never a node a pod; the cucumber is drawn instanced and bent on the
  GPU (`pickle_hang`). Nothing is on the ground until the colony wakes.
- **The colony never strikes first** (`0041`): harm wakes it, nearness never, and
  over `WakeSeconds` every cucumber drops into the horde. Numbers:
  `resources/mission/pod_default.tres`.
- **Plants bend** (`0032`): a `Flex` node (`PlantFlex`) after the model; wind, hits
  and the grapple push it; cut weak enough, it falls. Leaves are meshes (`0035`).
  Vines leave no rubble (`0042`).
- **Only valid vines** (`0036`): `VineGrower` re-rolls a failing seed from the seed
  and an attempt number. A seed grows the same vine on every machine: `SeededRandom`
  and `Fdlibm` trigonometry, never .NET's. `vinecheck.gd` holds the generator to
  `scripts/debug/vine_golden.json`; re-record with `-- record` only after a change
  meant to move them.
- **Baking and placing a nest** (`0056`): a saved `NestPreset` stays a seed and
  grows at load; **Bake** (Nest tab, or `nest_bake`) turns the nest Generate shows
  into `scenes/props/nests/prp_nest_<preset>.tscn`, its vines baked to
  `scenes/props/vines/` (vines baked earlier stay in `scenes/props/`). One-way,
  never written over unless asked: it may hold hand edits. **On a planet** the
  nest scene stands under a `PlacedNestSite`, placed roughly like a `PlanetSite`.
  When the planet builds, each vine keeps its flat distance and bearing from the
  heart and is stood upright on the curved ground with a small pad under it, so
  vines moved or deleted in the editor stay so. The nest is held out of the tree
  until the ground exists (a vine's bend reads where it enters), then its
  `PlacedNest` (`RegisterOnReady` off; the site calls `Register()`) takes the
  pods. Contracts do not yet draw authored nests from a pool.
- **Static voxel bodies are on World and Structure, never Prop** (`0039`); torn
  leaves are on Litter.
- **The Vines tool** (`addons/vine_gen`, `0043`) plans, previews and plays nests and
  logs Good/Bad to `tools/vine-logs/`. **The logs are training data: never edit or
  prune them.** A tool's C# signal handlers do not survive a code reload; the plugin
  rebuilds its panel on the first frame after (`--editor -- reload-check`).

## Working in the editor

**Handcrafted levels are built with the `godot` MCP tools (`mcp__godot__*`)**, or,
when they are not listed, `python tools/editor-bridge/godot_mcp.py call <command>
'<json>'` (`commands` lists them all).

| For | Commands |
|---|---|
| Seeing | `view`, `here`, `tree`, `selection`, `get`, `history`, `debugger` |
| Project | `project_setting` (never edit project.godot by hand while the editor is open) |
| Making and changing | `new_level`, `place`, `add`, `set`, `delete`, `batch` (one undo), `undo`, `redo` |
| Ground, plants, water | `terrain_create`, `sculpt`, `terrain`, `terrain_reload`, `scatter`, `forest`, `vines`, `flood`, `water_paint` |
| Vines | `vine_kinds`, `vine_kind`, `vine_roll`, `vine_bake`, `vine_judge` |
| Scenes and testing | `open`, `save`, `plugin`, `run`, `stop`, `output` |
| Pointing | `select`, `frame` |
| Asset lab | `lab_assets`, `lab_open`, `lab_view`, `lab_resource`, `lab_test`, `lab_do` |

Build the code with `dotnet build`; the editor loads it when its window next gets
focus. Check a level's routes against the grapple's 140 m
(`grapple_tuning_default.tres`) and look at it from where a player stands, not only
from above. Every command that changes a scene is one undo; the bridge never
answers while it is already answering (a save once re-entered its own request and
killed the editor). Claude may judge vines (verdicts `"by": "claude"`) and open the
game window unasked.

- **Terrain is sculpted, never generated from markers** (`0044`): a
  `SculptedTerrain` saves its heights and chunks in `<scene>_terrain/`; the brushes
  are `addons/terrain_brush`. **Water is shallow** (`0045`): a `WaterBody` under
  Water; everything asks `WaterBody.DepthAt`.
- **The asset lab** (`addons/asset_lab`, `0047`) shows any wrapper or model at rest
  with its parts and damage map and tunes its resources; **Test** plays
  `lvl_asset_lab` with it. Save writes the shared `.tres` after naming who else uses
  it. Never rebuild while a test plays.

## Folder map

```
addons/         our own editor plugins only
art/
  _source/      .blend and paint sources, packs as downloaded. .gdignore'd, LFS.
  models/       .glb exports
  voxels/       .vox files, imported directly
  textures/     palette.png and the few textures that are not the palette
  materials/    shared .tres materials
audio/          recordings
scenes/         player/ weapons/ enemies/ props/ levels/ vehicles/ ui/
scripts/        core/ player/ weapons/ enemies/ horde/ voxel/ planet/
                vehicles/ mission/ levels/ ui/ audio/ debug/
resources/      mirrors scripts/
shaders/
tools/          project-board/, vox-tools/, editor-bridge/, blender/. .gdignore'd.
docs/           pitch.md, plan.md, decisions/, ideas/, handoffs/, levels/, models.md
perflogs/       recordings and bench screenshots. Gitignored.
```

`scripts/debug/` is tooling that ships with the project and not the game. Folders
appear when their first file does.

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

The pipeline from a model to a placed, destructible, animated thing is
`docs/art-to-game.md`; the reasoning is `0005`. Blender `.glb` models are UV-mapped
onto `art/textures/palette.png` and drawn with `art/materials/mat_palette_toon.tres`.
Outlines are `shaders/toon_ink.gdshader`, a depth-only pass carried by `ToonPass`.
Smoke and dust are opaque inked cartoon balls (`fx_toon_puff`), never soft alpha
sprites. Cucumbers are animated in the vertex shader, not rigged.

Voxel art is MagicaVoxel `.vox` in `art/voxels/`, imported by our own importer
(`0009`): one node per object, colour per vertex on `mat_voxel_toon.tres`, emission
from Emit materials, palette index 255 marks a part's pivot, objects named `@Name`
become markers. After changing the importer, force a reimport (delete
`.godot/imported/*.vox-*`). Vine colours are deep greens, never neon.

**Mechs are animated by code, never clips** (`0046`): a gait from a cycle clock and
IK, moves posed in Blender with `tools/blender/dill_mech` and baked to `MechMove`s,
melee as pose strokes (`0028`). Only the core or head kills a mech; the gait follows
what is left; mechs mend themselves (`0046`).

**Every voxel model is destructible, the same way** (`0011`): a `VoxelBody` in its
wrapper. No voxel thing gets damage code of its own; numbers are in
`voxel_destruction_default.tres`, `_mech`, `_tree` and `_jar`. Buildings carry
weight and fall by a chunk graph baked at import (`0027`); trees
hold by connection (`0024`). **Nothing that scales with a model's voxels runs in
the frame** (`0015`): re-meshing, structure checks and splits go to `VoxelWorkers`
and are applied only if the grid's `Version` has not moved. Rubble heaps up rather
than vanishing (`0016`, `0022`).

Everything not ours is listed in `credits.md` the day it arrives. What still needs
modelling is `docs/models.md`. Git LFS tracks every binary asset.

## Workflow

```
dotnet build
GODOT="/c/Users/Elena/Desktop/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe"
"$GODOT" --path . --script scripts/debug/bench.gd                                  # contract level, idle
"$GODOT" --path . --script scripts/debug/bench.gd -- res://scenes/levels/<lvl>.tscn <mode>
"$GODOT" --headless --path . --editor --quit                                       # import everything (fresh clone, new asset)
"$GODOT" --headless --path . --script scripts/debug/<check>.gd                     # checks
```

Bench modes (`bench.gd` header lists them all): `look`, `ink`, `gpuab`, `horde 25000`,
`siege 25000`, `grapple`, `mech`, `mechs`, `bike`, `arsenal`, `slam`, `collapse`,
`cut`, `downtown 30`, `flex`, `forest`, `planet 25000`, `planets 10 1`, `descent`,
`nest pop=25000`, `colony pop=25000`, `flow`. Most run on `lvl_horde_flats`;
`planet` on `lvl_planet_test`; `nest` and `colony` on `lvl_nest_lab`.

Headless checks: `seedcheck`, `graphcheck`, `mechcheck`, `flexcheck`, `flingcheck`,
`vinecheck` (`-- record`, `-- rejections 200`, `-- settle`), `podcheck -- pop=3000`.
Photographs: `vinelook.gd`; `tutorialshots.gd`, `cloneshots.gd`, `wakeshots.gd` (the tutorial from the rider's eyes: the pod drop, clones, the colony waking).

- `bench.gd` waits for the draw count to settle, then reports per phase: mean,
  worst, frames over budget, GPU mean, draws, window size and the CPU split from
  `PhysicsStepProbe`. A bench run compiles every shader against the real driver;
  `--headless` compiles none.
- A screenshot (`#name`) costs 100-150 ms in its own frame; never read a worst frame
  from a phase that takes one. Read worst frames from a `quiet` run.
- `U` in any level records every frame to `perflogs/` and writes
  `perf_<time>_summary.txt` (read it first) and `perf_<time>_probe.txt`. Game code
  reports through `PerfLog.Counter` once at load and `PerfLog.Event` inside
  `if (PerfLog.Recording)`. Register `FrameProfiler` sections in a class that exists
  at load. A console line costs the frame it is printed in 15-40 ms.
- The game raises itself above normal priority on Windows (`ProcessPriority`).

Commit at every point where the project builds and runs. Short, factual messages.

**Handoffs live in the repo, always**: a handoff, plan or brief for another session
is saved in `docs/handoffs/<topic>.md` and committed, never in a scratchpad, temp
folder or home directory.
