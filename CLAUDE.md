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
| Art facing | *Not yet verified.* Expected: a model facing Blender's `-Y` arrives facing Godot's `+Z` (`Vector3.ModelFront`). Confirm with the M0 test asset and replace this line. |
| Art scale | *Not yet verified.* Expected: 1 Blender unit = 1 m with transforms applied. |

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
7. **No third-party addons** without a proposal and a reason.

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

## Folder map

```
art/
  _source/      .blend and paint sources. .gdignore'd, LFS. Read its README.
  models/       .glb exports
  textures/     palette.png and the few textures that are not the palette
  materials/    shared .tres materials models are mapped onto
audio/          recordings
scenes/         player/ weapons/ enemies/ props/ levels/ vehicles/ ui/
scripts/        core/ player/ weapons/ enemies/ horde/ voxel/ planet/
                vehicles/ mission/ levels/ ui/ audio/ debug/
resources/      mirrors scripts/
shaders/
tools/          project-board/ (and voxel-bench/ from M2). .gdignore'd.
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

## Art

The pipeline is in `art/_source/README.md` and `docs/decisions/0005`. In short:
Blender, `.glb` export, every model UV-mapped onto `art/textures/palette.png`
and drawn with `art/materials/mat_palette_toon.tres` (toon diffuse and specular,
nearest filtering). The outlines are `shaders/toon_ink.gdshader`, a depth-only
screen pass carried by `ToonPass` on the camera. Pickles are animated in the
vertex shader, not rigged.

Git LFS tracks every binary asset (`.gitattributes`). Godot's text formats stay
in git proper.

## Workflow

```
dotnet build
GODOT="/c/Users/Elena/Desktop/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe"
"$GODOT" --path . --script scripts/debug/bench.gd                                       # main scene, idle
"$GODOT" --path . --script scripts/debug/bench.gd -- res://scenes/levels/<lvl>.tscn look  # screenshots
"$GODOT" --path . --script scripts/debug/bench.gd -- res://scenes/levels/<lvl>.tscn ink   # ink A/B
"$GODOT" --path . --script scripts/debug/bench.gd -- res://scenes/levels/lvl_horde_flats.tscn grapple
"$GODOT" --path . --script scripts/debug/bench.gd -- res://scenes/levels/lvl_horde_flats.tscn horde 25000
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
- `U` in any level records every frame to `perflogs/` and prints a summary.
- After a fresh clone or new asset: `"$GODOT" --headless --path . --editor --quit`
  imports everything.
- The board: open `tools/project-board/index.html`, connect the project folder,
  and `docs/project_board.json` becomes the copy git keeps.

Commit at every point where the project builds and runs. Short, factual
messages.
