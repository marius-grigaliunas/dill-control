# Dill Control: plan

*Written 8 October 2026, in the lab repository (`fps-swampy`), and moved here
with the scaffold. Read `pitch.md` first: it says what the game is. This file
says how this repository gets from nothing to a vertical slice, and why it is a
new repository at all. Paths like `SpikeDirector` or `scripts/spike/` are in the
lab.*

*Updated 29 October 2026: the slice, M5, M6 and the arsenal follow the locked design (`decisions/0050`-`0054`), M8 and M9 are new, and what 0033-0049 built is listed under Milestones. M0-M4 below are the history of how it was built.*

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

*Replaced on 29 October 2026 by `decisions/0051` (the contracts), `0052` (the
weapons and the shop), `0053` (death) and `0054` (the opening). The old slice was
one mission: orbit, bike, compound, Mother Brine.*

**Onboarding (`lvl_tutorial`, built, 0055) and three contracts in order, about
an hour:** nest clearing, loot grab on a storm planet, colony rescue. Scripted
beats on procedural terrain, one locked and tuned seed for the demo, ending on an
empty contract screen with a wishlist line. Any contract can be replayed.

| In the slice | Not in the slice |
| --- | --- |
| The opening: unclear cloning flashes, then control in the landing pod (0054) | The orbit-and-bike arrival, a star map, more than the three contracts |
| Contract 1, nest clearing: pod drop, the vine nest, clones, the minigun gift at 50%, the stationary queen, extraction by pod | Mother Brine, mole cucumbers, the underground queen |
| Contract 2, loot grab: gear run on a storm planet (challenge still open), the crash pod and the nuke designator, the bike | Escort missions, caves, walking colonists |
| Contract 3, colony rescue: a procedural town, shafts, turrets, five shuttles, four targets, a mech drop, the bike at 20,000 kills | A drill rig |
| Standard, Cornichon, Spitter, Tank (throws and punches) and the queen | The Thrower as its own kind |
| Five core weapons with two tiers each, in fixed slots; the grapple; the minigun and the nuke designator | Mods, tiers 3-6, the other specials |
| Shield and health, three clones, failure that pays nothing | Debt and the story (`ideas/story.md`) |
| The ship cabin: invoice printer, a shop with two purchases, a crate hatch | The full campaign, the contract board |
| Real models, textures and sounds you made or sourced | Final music |

**The slice arsenal is settled** (0052): assault rifle, bazooka, pole saw, laser
cannon (with its gravity-gun mode) and sniper. It replaces the proposed machete,
Brine Ripper, shotgun, rocket launcher and grenade; the rest of the prototype's
arsenal waits in the lab. Five weapons is five viewmodels to model.

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

### M4: The planet, generated per contract

*Changed on 10 October 2026: planets and compounds are generated per contract
for replayability, inside authored bounds (decisions 0017-0021). Built in six
steps, each benched and committed:*

1. **The planet ported** on a fixed seed: heightfield, chunks with levels of
   detail, a far shell for the whole silhouette, craters, point gravity, the
   horde on it (0017).
2. **Seeded planets and biomes**: a contract is a seed; an archetype's ranges,
   three biomes (0018).
3. **The system and the arrival**: the ship waits in orbit, the player starts
   on the bike, other bodies in the sky, the air thinning with height.
4. **The compound from clusters**: a seeded layout of authored cluster scenes
   on a levelled pad; the horde from a tier.
5. **Contracts and loading**: the session, the generator, the contract level
   building itself behind its loading screen (0021).
6. **Menu, ship and computer**: the title screen, the player's ship
   (`lvl_ship_hub`), the contract board.

*Done when:* the open ground and the compound bench at 60 on every seed a
sweep tries, a crater reroutes the horde, and menu to ship to contract to
orbit to the ground plays through. The flow plays through; the budget is to
be re-measured on a rested machine.

### M5: The queen

*Revised 29 October 2026: Mother Brine is no longer a slice milestone (0051).*

A stationary queen pod at a nest's heart, with feeder vines, a guard of Tanks and
Spitters, weak spots that open after she attacks, and three attacks: the screech,
the seed spit and the tearing down of vines. The minigun gift at 50% of the nest
destroyed, and the contract-1 win (the queen, every pod, 80% of the cucumbers).
The Tank grows throwing and punching in place of the Thrower. Mother Brine waits
as a later roaming boss.

*Done when:* the queen can be fought from the line in 2-3 minutes at tier 1,
roughly 25 bazooka hits, and the contract is won and extracted from.

### M6: Arrival, extraction and the opening

*Revised 29 October 2026: the bike and the arrival from orbit are no longer slice
milestones (0054).*

The landing pod and its handcrafted descent through the window, the same pod for
extraction, the free choice of drop point on the contract screen, the opening's
unclear cloning flashes and their replay at the first death. The bike's model is
no longer needed for the arrival; it is a special item (0052).

*Done when:* a contract starts inside the pod, lands on a chosen point and ends
in a pod that carries the player up, with no load between.

*What is built: the system, the ship in the sky and the far shell (M4 step 3)
stay in the code and are not slice work.*

### M7: The mission

Clear threshold, three clones that fail the contract when spent, the invoice
screen, title and pause menus. Hands for the viewmodels, recorded sounds replacing
the synthesised ones where they matter most.

*Done when:* someone who has never seen it can play it start to finish, and it
holds 60.

### M8: Contracts 2 and 3

Added 29 October 2026 (0051).

- **Contract 2:** the gear run, to be reworked first (0051: the bike would let the
  player simply fly out). A storm planet, weather (fog, lightning, a tornado),
  the built hive with no ground cucumbers, a fixed landing, gear at the hive's centre, the crash pod and the nuke designator, the bike with a laser that
  fires 10 s then cools down.
- **Contract 3:** a procedural town on a levelled pad (authored clusters may serve it), skyscrapers, shafts as spawn holes with sealing, turrets on roof
  relays, five shuttles with automatic guns, four targets, a mech drop, the bike
  at 20,000 kills.

*Done when:* each contract plays through on its locked seed and holds 60, with
its cap (24,000 in the town) in the bench.

### M9: The ship cabin

Added 29 October 2026 (0052, 0053).

The cabin as a walkable hub with a window onto the next planet, the invoice
printer, a shop that shows both tiers of every weapon and sells two purchases in
the slice (each logged), and the crate hatch. The menu is the same window with
its buttons. Failure wakes the player here, unpaid.

*Done when:* menu, cabin, contract, invoice, shop and the next contract play
through.

### To do, noted: remove the prototype weapons

Decided but not started (29 October 2026). Once the five core weapons exist
(0052), the rest of the prototype arsenal is deleted: `wpn_bayou_boomstick`,
`wpn_bayou_machete`, `wpn_brine_burner`, `wpn_brine_ripper`, `wpn_dill_hammer`,
`wpn_frag_grenade`, `wpn_gherkin_volley`, `wpn_gravity_manipulator`,
`wpn_grenade_thrower`, `wpn_sledge`, `wpn_strike_designator` and
`wpn_vinegar_lance`. They are wired into the player's code and the `arsenal` and
`slam` benches, so the removal is its own code pass. The weapons are not changed
until then.

### Built since this plan was written

Marked done by what 0033-0049 built:

- **The vine nest** grown at load from a seed, the Vines tool and the editor
  bridge (0033, 0043, 0044); vines that bend and leaves that are meshes (0032,
  0035); only valid vines (0036); the planet pinned at 1,260 m (0037); one clump
  of 55-70 m vines (0038); structure and litter layers (0039); pods in bulk, with
  the wake and the contract's win (0034, 0040, 0041); no rubble from vines (0042).
- **Shallow water** (0045); **mechs that fight on broken**, mend and take
  Blender moves (0046); **the asset lab** (0047); **Vulkan** on Windows (0048).
- **`lvl_tutorial`**, the onboarding level: handbuilt, a placed nest of 1,115 pods and a first-person pod drop (0055; its generated forerunner is `lvl_nest_intro`, 0049).
- Of the milestones: M0-M3 and M4 stand; M4's arrival and system are built and
  parked.

## Open decisions

1. ~~**The name.**~~ Dill Control.
2. ~~**The slice arsenal.**~~ Settled: the five core weapons of 0052.
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
