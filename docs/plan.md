# Dill Control: plan

*Written 8 October 2026, in the lab repository (`fps-swampy`), and moved here
with the scaffold. Read `pitch.md` first: it says what the game is. This file
says how this repository gets from nothing to a vertical slice, and why it is a
new repository at all. Paths like `SpikeDirector` or `scripts/spike/` are in the
lab.*

---

## The point

This repository found the game. It did not build it.

Everything in it was produced by generating and measuring: 52,000 lines of C#
across 135 files, and almost no authored content. The planet, the forest, the
tower city, the compound, the spires, the ruins, the jar and Mother Brine are
all assembled at runtime by `SpikeDirector` (4,149 lines on its own) out of
sliders in `SpikeTuning` (1,284 lines). `lvl_perf_spike.tscn` is nearly empty.
There is not one model, texture or recorded sound in the repo; every visual is a
shader, a primitive or generated voxels, and every sound is synthesised.

That was the right way to answer the two questions a prototype exists to
answer. **Is it fun?** Swinging through a compound while it comes apart, yes.
**Does it fit the machine?** 25,000 pickles at 1080p on Iris Xe, yes, nearly.

It is the wrong way to make the game, for three reasons:

1. **Nothing can be edited by hand.** Moving a spire means changing a number
   in `CompoundStats` and relaunching. A level designer, which is you, needs to
   drag it in the editor.
2. **There is nowhere for art to go.** A pickle is a capsule built in
   `HordeManager`. A spire is a column `CompoundModels.Spire` writes into a
   voxel grid. Replacing either with something you modelled means rewriting
   the code that makes it.
3. **The history outweighs the rules.** `CLAUDE.md` is about 2,000 lines of
   decisions, some superseded, some about features that were cut (the drill,
   the slicers, the swamp). It is a good diary and a poor rulebook.

So: **a new repository for the game, and this one stays as the lab.** The lab
keeps its "everything at max, no rules" sandbox, its benches and its
measurements. It is where an idea is tried and where a misbehaving system in
the game gets A/B'd against its original.

## What the vertical slice is

One mission, start to finish, at the quality the final game would ship at for
the content it contains, holding 60 fps at 1920x1080 native on the target laptop.

| In the slice | Not in the slice |
| --- | --- |
| Start in orbit on the bike, fly down, land | Star map, mission select, more than one planet |
| One planet, one compound, one Mother Brine | Procedural compounds, more than one biome |
| The five pickles, real models | New enemy kinds |
| Grapple: zip, swing, orbit, release float | New traversal verbs |
| A small arsenal (proposal below) and the bike with gun, laser and pod | The full prototype arsenal, airstrike ordnance types |
| Shield and health, three clones, a clear threshold | Persistent unlocks between missions |
| Company packages inside the mission | Meta-progression |
| An invoice end screen | Economy |
| Real models, textures, sounds you made or sourced | Final music |

**Proposed slice arsenal** (to confirm): machete, Brine Ripper (rifle), the
shotgun, the rocket launcher, the grenade. On the bike: the explosive gun, the
laser, the rocket pod. Five hand weapons is five viewmodels to model, and every
cut one is a week of art saved. The rest of the prototype's arsenal waits in the
lab.

## The art pipeline

You are the artist, in Blender. The pipeline below is chosen for one person
making a stylised game that has to draw 25,000 of something.

### Format: glTF binary (`.glb`), exported from Blender

glTF is Godot's first-class import format, it is what Blender's own exporter
writes best, and it carries meshes, materials, skeletons and animations in one
file. Godot can also import `.blend` files directly (it calls Blender's glTF
exporter behind the scenes), which is the faster loop. It is not the
recommendation, because:

- every import needs Blender installed and its path set, including the
  headless `bench.gd` runs on a fresh clone;
- a Blender upgrade can silently change what gets imported;
- a `.blend` holds everything you were trying out, so the game imports more
  than you meant to give it.

If exporting by hand gets tedious after the first month, switch. It is a
per-file decision and nothing else depends on it.

### Where files live

```
art/
  _source/        .blend files, the palette's source image, references
                  .gdignore so Godot never imports it; tracked by LFS
  models/         .glb exports, one per asset
  textures/       palette and the few textures that are not the palette
  materials/      the shared .tres materials models are mapped onto
```

### Conventions to fix on day one

- **Units:** 1 Blender unit = 1 metre. Apply scale and rotation before export.
- **Facing:** model the front facing Blender's `-Y` (what Front view looks
  at). It arrives in Godot facing `+Z`, which Godot calls `Vector3.ModelFront`.
  Verify this with the first test asset and write the result into CLAUDE.md.
- **Origin:** at the feet for anything that stands, at the grip for a weapon.
- **Collision:** use Godot's name suffixes on helper meshes in Blender
  (`-colonly`, `-convcolonly`) for props that need it. Voxel bodies make their
  own collision and need none.
- **Never edit an imported scene directly.** Each `.glb` gets a small wrapper
  `.tscn` that instances it and adds the script, hitboxes and anything else.
  Re-exporting from Blender then replaces the mesh and leaves the work around
  it alone.

### Materials: one palette, not many textures

Model, then UV-map every face onto a swatch of one small palette image (say
256x256 of flat colour blocks and a few gradients). Every model then shares one
material on one toon shader. That is the standard cheap approach for flat
stylised art, and it suits this game for four reasons:

- it matches the cel and ink look the prototype already proved;
- one material is one batch, which is what the multimeshes need;
- recolouring the game is editing one image;
- texture memory is almost nothing, which matters on an integrated GPU sharing
  RAM.

Godot's import dialog can map each imported material to an external `.tres`, so
the Blender material is just a name and the real shader lives in the project.

### The pickles: animated by the shader, not by a skeleton

The lab planned vertex animation textures for the horde. Pickles have no limbs,
which makes that unnecessary. A pickle's whole repertoire (waddle, wobble,
squash before a lunge, stretch in the air, flop on death) is a deformation along
its own length, and a vertex shader can do it from a time phase plus a state
number passed per instance through the multimesh's `INSTANCE_CUSTOM`. There is no
rig, no bake and no per-animation memory, and it costs one shader for all 25,000.

What that asks of the model: enough edge loops along the length to bend
smoothly, and a triangle budget the horde bench sets. The lab's near capsule is
72 triangles. Start the pickle near 200 and let the bench say whether that
holds. The far tier stays a few-sided column; at that distance nobody sees more.

Mother Brine is the same idea scaled up, and she was already a body that squashes
rather than a creature that walks.

### What still needs real animation

- **First-person hands.** The one place a skeleton is genuinely needed. This
  is the biggest single art item in the slice. Ship the first milestones with
  gun-only viewmodels (the procedural sway, recoil and swing motion already
  works without hands) and model hands once the weapons are settled.
- **The bike.** A rigid model with a few moving parts, done with node
  transforms, not a skeleton.

### Voxel models stay in `voxel-bench`

*Changed by `docs/decisions/0009` and `0011`: voxel models are authored in
MagicaVoxel and imported directly, and every one of them is destructible
through `scripts/voxel/`. The lab's `VoxelBuilding` and `VoxelModel` are not
ported; the bench stays only as a reference.*

Destructible structures (spires, towers, the jar, ruins, Mother Brine's armour)
are voxel bodies, because being carved and collapsing is what they are for.
`tools/voxel-bench` already writes `VoxelModel` `.tres` files and comes across
whole. What changes is that a voxel model is *authored* in the bench and *placed*
as a node in a scene, not generated from a seed at runtime.

### Git LFS from the first commit

The lab deferred LFS until art arrived. Art is why the new repo exists, so it
goes in before anything else:

```
git lfs install
# .gitattributes
*.blend  filter=lfs diff=lfs merge=lfs -text
*.glb    filter=lfs diff=lfs merge=lfs -text
*.png    filter=lfs diff=lfs merge=lfs -text
*.jpg    filter=lfs diff=lfs merge=lfs -text
*.exr    filter=lfs diff=lfs merge=lfs -text
*.kra    filter=lfs diff=lfs merge=lfs -text
*.wav    filter=lfs diff=lfs merge=lfs -text
*.ogg    filter=lfs diff=lfs merge=lfs -text
*.ttf    filter=lfs diff=lfs merge=lfs -text
*.otf    filter=lfs diff=lfs merge=lfs -text
```

and `*.blend1` in `.gitignore`, since Blender writes a backup on every save.
GitHub's free LFS quota is small; check it before the first big push, or host
the remote somewhere with more room.

## What comes across, and what does not

The rule: **port engines, re-author content.** Code that simulates something
comes across, cleaned up, one system at a time. Code that *decides where things
are or what they look like* stays behind, and that job goes to scenes and assets.

### Comes across

| System | Lab files | Size | Notes |
| --- | --- | --- | --- |
| Player and camera rig | `scripts/player/` | 3,875 lines | `PlayerController` depends only on `Core`, `Audio`, `Weapons`. `MovementTuning` comes with it. |
| Grapple | `GrappleHook.cs` | 706 | Depends only on `Core` and `Audio`. The surface-ray and orbit delegates stay delegates. |
| Core contracts | `scripts/core/` | 1,504 | `EventBus`, `IDamageable`, `Hitbox`, `DamageInfo`, `Blast`, `GameLayers`. Mentions the spike only in comments. |
| Horde | `HordeManager.cs` | 3,382 | Depends on `Core`, `Audio`, `Enemies`, `Planet`, so it moves out of a `spike/` folder into its own. The kind data in `scripts/enemies/` comes with it. |
| Voxel bodies | `VoxelBuilding.cs`, `VoxelField.cs` | ~2,800 | Sections, budget, collapse, armour threshold, greedy meshing. The generators (`TowerModels`, `CompoundModels`) do not come across. |
| Planet | `scripts/planet/` | 2,758 | Cube-sphere, streaming, craters, flow field. Already free of the spike. |
| Weapons | `scripts/weapons/` | 9,248 | Only the slice's arsenal. `ExplosionEffect`'s six-clock staging and inked layers come across; its visuals get remade. |
| Flight rig | `scripts/vehicles/flightrig/` | ~1,000 | Already portable, with its own README. |
| Audio | `scripts/audio/` | 2,225 | `SoundEvent`, `SoundBank`, `AudioDirector`. The synthesiser stays as the placeholder until recordings replace it. |
| Menu module | `scripts/ui/menu/` | | Already portable. |
| Instruments | `bench.gd`, `PerfRecorder`, `FrameProfiler`, `PhysicsStepProbe` | 2,654 | Ported first, before any game code. |
| Shaders | `toon_ink`, `planet_terrain`, `planet_sky`, `fx_*` | | The ink pass and toon lighting are the look. |
| Tools | `tools/project-board` now; `tools/voxel-bench` in M2 | | The board is seeded from the milestones below. The voxel bench writes `.tres` files pointing at the voxel scripts, so it comes across with them. |

### Stays in the lab

- `SpikeDirector` and `SpikeTuning`. Their job, wiring everything and holding
  every slider, is replaced by an authored level scene plus a small mission
  director that knows only the mission.
- The generators for content: the forest, the tower city, the scatter props,
  the grove, the ruins and the compound layout.
- Everything the slice does not use: obstacle courses, the look filter (`X`),
  the stress ladder as a key, the graybox level and its volumetric fog, the
  magma floor, the airstrike's five ordnance types, the moon, wetness and
  coolant.
- The sandbox rule. The game has fail states, and the lab is where "no rules"
  lives.
- The 2,000-line `CLAUDE.md`. Its reasoning stays readable here, and the new
  one links back to it.

### How to port one system

1. Read the lab file and its section in the lab's `CLAUDE.md`.
2. Bring it across in the new namespace, with its tuning in its own resource
   rather than a field on a god-resource.
3. Replace anything that generated content with an `[Export]` that takes
   authored content.
4. Make a bench mode for it before it is called done.
5. Run the bench on the target machine, write the number in its decision
   record, and commit.

## The new repository

### Name

**Dill Control.** The folder and repository are `dill-control`, the C#
namespace and assembly `DillControl`.

### Shape

```
dill-control/
  CLAUDE.md                  rules only, short
  README.md                  what this is, how to build, how to bench
  project.godot  DillControl.csproj  .gitattributes  .gitignore
  art/                       see above
  audio/                     recordings, as they arrive
  scenes/   player/ weapons/ enemies/ props/ levels/ vehicles/ ui/
  scripts/  core/ player/ weapons/ enemies/ horde/ voxel/ planet/
            vehicles/ mission/ ui/ audio/ debug/
  resources/  mirrors scripts/
  shaders/
  tools/    project-board/  voxel-bench/   (.gdignore)
  docs/
    pitch.md
    decisions/               one short file per decision
  perflogs/                  gitignored
```

What changes from the lab's layout: `horde/` and `voxel/` are top-level systems
rather than tenants of `spike/`; `mission/` holds the one thing that knows a
mission's shape; `props/` is where authored set pieces live; there is no
`spike/`, `site/` or `encounters/`.

### Settings carried over exactly

- Godot `4.7.2-stable mono`, `net8.0`, `Godot.NET.Sdk/4.7.2`.
- `physics/3d/physics_engine = "Jolt Physics"`, set explicitly, because
  `DEFAULT` is not Jolt in 4.7.2.
- Window 1920x1080, the `[swampy]` target section (renamed), Forward+.
- `<Optimize>true</Optimize>` in Debug, since F5 runs Debug and that is the
  build that gets played.
- Input map device ids: `16` for keys, `32` for mouse buttons.
- Shadow atlas 2048 and PCF Low; glow off by default.
- The `[shader_globals]` the sky needs, when the sky arrives.

### `CLAUDE.md`, rules only

Short enough to read in two minutes. A draft:

```
# Dill Control

A first-person horde shooter in Godot 4.7.2 mono (C#), Jolt physics. See
docs/pitch.md for the game. The prototype that proved it is the lab repo
(fps-swampy); its CLAUDE.md holds the reasoning behind most rules below.

## Requirements
- 60 fps at 1920x1080 native on an 11th-gen i5 with Iris Xe. A frame that misses
  is a bug. RenderScale is a diagnostic and ships at 1.0.
- Measured with bench.gd on the target machine, paired against a baseline. Never by eye.

## Rules
- Authored over generated. If a designer would want to move it, it is a node in
  a scene, not a number in a generator.
- Data over code. Arguable numbers live in [GlobalClass] Resources.
- Signals and [Export]s, never GetNode paths across scenes. The EventBus is for
  traffic whose sender cannot name its listener.
- Imported models are wrapped in a .tscn, never edited in place.
- One system at a time: built, benched, committed.
- Every level has the fixed tree: Geometry, Water, Foliage, Props, Lighting,
  Audio, Spawns, Navigation. The player is never saved into a level.
- Decisions with more than one defensible answer get a file in docs/decisions/,
  not a paragraph here.

## Naming
PascalCase for code and nodes; snake_case for .tscn/.tres/.glb; prefixes lvl_
wpn_ nme_ shp_ fx_ mat_ snd_ vxl_ prp_.

## Verified environment
(engine version, Jolt key, device ids, art facing — filled in as verified)

## Build, run, bench
...
```

### Decision records, not a diary

Each decision gets a file in `docs/decisions/`, numbered, one page at most:
context, decision, what was measured, what it costs. `CLAUDE.md` holds the rule
and links to the record. That keeps the rules readable while keeping the
reasoning that made the lab's `CLAUDE.md` valuable. A superseded decision gets
a line pointing at its replacement rather than being rewritten.

## Milestones

Each one ends with a bench run on the target machine and a commit. The order
puts the grapple first, because it is the first focus in the pitch and because
the compound only makes sense as something to swing through.

### M0: Scaffold

- Empty Godot project with the settings above, LFS, `.gitignore`, `.gdignore`s.
- `bench.gd`, `PerfRecorder` and `FrameProfiler` ported, reporting on an empty
  scene.
- `CLAUDE.md`, `README.md`, `docs/pitch.md` and this plan copied over.
- `tools/project-board` copied, seeded from these milestones.
- **Art pipeline smoke test:** one Blender model, exported as `.glb`, mapped onto
  the palette material, wrapped in a scene, drawn with the ink pass. This is how
  the facing, scale and material-mapping conventions get verified and written
  down.

*Done when:* a fresh clone builds, `bench.gd` runs headless, and an asset made
in Blender appears in the game the way it looked in Blender.

### M1: Feel

Player, camera rig, grapple and jetpack hover, in a hand-built gym level with a
dozen tall pillars and blocks you model in Blender. No enemies, no weapons
beyond something to point.

*Done when:* zip, swing, release and float feel right, and the gym benches far
under budget. This is where "polish the line before any new feature" is honoured.

### M2: Something to wreck

Voxel bodies ported. A first spire, tower and jar authored in `voxel-bench` and
placed by hand. `Blast`, `ExplosionEffect` and the first two weapons (Brine
Ripper and the rocket launcher) with gun-only viewmodels you modelled.

*Done when:* you can swing between spires, shoot one out from under yourself,
and watch it come down as dust, at 60.

### M3: The pickles

The horde ported into `scripts/horde/`. Standard and cornichon first, as real
models with shader animation; then thrower, spitter, tank. Hit proxies, health
and the shield.

*Done when:* `bench.gd horde 25000` holds the budget on the target machine with
real pickle models, not capsules.

### M4: The planet

Cube-sphere, streaming, craters and the flow field ported. The compound moves
onto the planet as an authored scene placed at a marker.

*Done when:* both the compound and the open ground bench at 60, and a crater
reroutes the horde.

### M5: Mother Brine

Ported and re-skinned: authored armour in `voxel-bench`, hitboxes for the warts
and core, the orbit grapple.

*Done when:* she can be fought from the line, start to finish.

### M6: The bike and the arrival

Flight rig and bike systems ported, the bike modelled. The orbit-to-ground
descent, including the distant-planet stand-in the pitch's open questions call
for, so the planet's edge is clean from space.

*Done when:* a mission can start in orbit and end with you standing in the
compound, with no load and no cut.

### M7: The mission

Clear threshold, three clones, company packages, the invoice screen, title and
pause menus. Hands for the viewmodels, recorded sounds replacing the synthesised
ones where they matter most.

*Done when:* someone who has never seen it can play it start to finish, and it
holds 60.

## Open decisions

1. ~~**The name.**~~ Dill Control.
2. **The slice arsenal.** The proposal above, or a different five.
3. **The remote.** GitHub with paid LFS, or somewhere with more room for art.
4. **Hands.** At M7 as planned, or earlier if gun-only viewmodels feel wrong
   in M1.
5. **Whether the lab gets a README note** pointing at the new repo and saying it
   is now the lab.

## The first week

1. ~~Pick the name.~~
2. Create the repo, set up LFS, make the empty Godot project with the carried-over
   settings, commit.
3. Port `bench.gd` and the perf instruments; bench the empty scene on the target
   machine; commit.
4. Write the short `CLAUDE.md` and the first decision records (Jolt, the
   performance target, authored over generated, glTF and the palette).
5. Model one test asset in Blender, take it through the whole pipeline, and
   write down the facing and scale result.
6. Copy the project board, seed it from M0 to M7, connect it to `docs/`.
7. Start M1: the gym level and the player.
