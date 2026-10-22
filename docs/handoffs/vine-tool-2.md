# Handoff: the Vines tool, part 2 — fix what playing the nest showed, then pods and the tool

Read `CLAUDE.md` first, then `docs/handoffs/vine-tool.md` (the original task,
its settled decisions and its build order). This file is what happened since
and what to do next. **Work only inside `C:\Projects\dill-control`.** Same
rules: one system at a time, numbers in `.tres`, bench on the target machine,
short commits of your own files only (never `docs/project_board.json`).

## Where it stands (commits 7ca8c3d..8986f35)

Steps 1-6 of the original build order are done and committed:

1. **Prototype retired.** Rolls go through `SeededRandom`. Golden vines are
   recorded from C# in `scripts/debug/vine_golden.json`, and
   `vinecheck.gd -- record` re-records them. The rock planet is pinned at 1,260 m.
2. **A vine built in memory.** `VoxModelBuilder` holds the importer's shape
   code. `VineFactory` builds a vine with no files, identical to a baked one.
3. **Valid-only vines.**
   - `VineValidator` judges a vine and `VineGrower` re-rolls a failing seed
     from the seed plus an attempt number. Jolt never decides a roll.
   - The generator leaves out pods with no clear drop and leaves with no room.
   - `vinecheck.gd -- rejections N` reports per kind; `-- settle` stands each
     baked vine 5 s under Jolt.
4. **Leaves are meshes.**
   - `VineLeaves` draws them with `LeafMesh` and `mat_leaf_toon`, bent by
     `shaders/flex_bend.gdshaderinc`.
   - Collision is one thin box per leaf on its section's body
     (`VoxelPart.AddExtraShape`); damage reaches leaves through
     `VoxelBody.Intercept`.
   - Tendrils are gone. Baked vines carry `<name>_leaves.tres`.
5. **Colour variety.** Each kind has a palette (`vine_palette_<kind>.tres`),
   each vine a shade and each leaf a tint, all inside a green band.
6. **The nest.**
   - `NestKit` (`resources/mission/nest_kit_default.tres`) holds the kinds and
     the layout.
   - `NestLayout` plans from a seed and a population, one pod per pickle.
   - `NestPlacer` is a second site chooser (`PlanetTerrain.MoreSiteChoosers`)
     that lays the nest on the planet.
   - `NestBuilder` is the level's builder over `SystemBuilder`.
   - `lvl_nest_lab.tscn` takes `pop=N`, `nest=SEED` and `horde=0` after `--`.
   - `bench.gd ... lvl_nest_lab.tscn nest pop=25000` benches it.

The 25,000 case, plugged in, measured on the user's laptop:
- 144 vines, 706 m from the heart. Load 20 s. Memory about 1.15 GB managed
  plus 0.7 GB in the engine.
- Ground 12.9 ms. Canopy 12.9 ms. 400 m above 16.5 ms (GPU 14.4 ms, 22 of
  152 frames over budget).

Engine fixes found on the way (all committed):
- **Structure layer.** Static voxel bodies are on World + `Structure`
  (layer 9), never Prop. The planet's gravity Area3D watches Prop, and watching
  64,000 static boxes cost Jolt 16 ms a tick.
- **Strain walk.** `TickStrain` no longer walks every chunk graph every frame.
- **Leaf shapes.** They are placed once per section, with the body out of the
  physics space.
- **ManipulatorBinding** unsubscribes from the level when it leaves the tree.

## What the user saw playing it: fix these first, in this order

Each is a separate commit. Show the user a screenshot of each visual fix.

1. **The city is not wanted.** The compound was only a destruction test, and
   nothing of it belongs with the nest or the vine generator.
   - Remove `CompoundSite` and `CompoundPlacer` from `lvl_nest_lab.tscn`.
   - Give `NestPlacer` its own heart: the flattest of a few candidates on the
     planet, not "beside the compound".
   - The horde's garrison was at the compound. Without one, the lab has no
     horde until step 7 makes the nest's pickles the horde. Decide with the
     user what `horde=1` means meanwhile; most likely a garrison at the heart.
   - `SystemBuilder` still expects a `Placer` (CompoundPlacer). Make that
     optional, or give the lab a builder that needs none.
2. **The nest must be one clump in one place, not spread over the planet.**
   At 25,000 it reaches 706 m on a 1,260 m planet, which is a third of a
   hemisphere. The geometry conflicts: about 144-250 vines at 85-110 m apart
   cannot be a clump.
   - Before coding, ask the user to choose between:
     - more pods per vine (bigger vines, more pods per ring; kinds' ranges);
     - closer spacing (50-70 m, still swingable with a 140 m grapple);
     - a hard nest radius such as 300-400 m;
     - or a mix.
   - `NestKit.MaxRadiusM`, `SpacingM` and the kinds' `PodsPerRing`,
     `RingCount` and `ArmsPerRing` ranges are the levers.
   - `NestLayout` already shifts to high-pod kinds when the population doesn't
     fit, and reports what is short.
   - Also confirm with a top-down shot that the bearing and arc mapping in
     `NestPlacer.Along` gives a disc round the heart.
   - Save the answer to memory: it is design direction.
3. **Leaves are not attached to the vine.** In the user's screenshot, stems end
   in the air under the canopy.
   - Nothing checks it today. `vinecheck` only compares the factory's set to
     the baked set. Add a check first: for every leaf of every baked and
     in-memory vine, the attach point must be within a voxel of a solid cell in
     the model's grid, in model space.
   - Suspects, in order:
     - `VineLeafSet.Of`'s voxel-to-model mapping (the foot shift, `ToGodot`'s
       Y to -Z);
     - the arm-leaf attach point in `PlaceLeaves` (`at.P + outward * at.Radius`
       may sit off the arm's surface);
     - whether MultiMesh instances get the same `world_vertex_coords` bend as
       the voxel model.
   - Compare against a vine in `lvl_horde_flats` up close before blaming the
     nest.
4. **Shadows must be consistent.** Leaves cast shadows at a distance where the
   vine itself does not; its shadow appears only up close.
   - The vine far off is drawn by `VoxelBody`'s coarse 2x/4x copies
     (`AddCoarse(..., bool shadows)`, `CoarseFromMetres` in
     `voxel_destruction_vine.tres`), which probably cast none.
   - Make leaves and stalk cast shadows over the same range: either the coarse
     copies cast them, or the leaf MultiMeshes stop casting at the same
     distance.
5. **A shot leaf falls at once, and a falling leaf can push the player through
   the ground forever.**
   - Leaves should take bullet holes and fall only when badly holed or shot
     through the stem. The user wants to see the damage. Options:
     - holes cut in a per-leaf copy of the blade;
     - a decal or shader mask per leaf (MultiMesh custom data has room);
     - more health with a stem hit zone.
   - Propose one to the user.
   - Loose leaves are `RigidBody3D` on Prop, 120 kg, and the player's mask
     includes Prop. A leaf landing on the player shoves the CharacterBody
     through the planet's ground collider.
     - Loose leaves must not push the player: put them on a layer the player
       does not collide with, and lighten them.
     - Add a guard: a player found below the ground (radius less than surface
       minus a few metres) is put back on the surface.
6. **The sky is wrong in `lvl_nest_lab`.** The lab dropped the contract
   level's `SolarSystem`, which drives `mat_planet_sky` (the sun, the planet as
   seen from space, the bands). Either keep `SolarSystem` with no ship and no
   descent (it may need its ship and bike exports to be optional), or set the
   sky's parameters for a ground start. Compare with `lvl_contract` after
   landing.
7. **A bazooka swings a vine far too much.** `flex_vine.tres` has
   `KickPerDamage = 200`.
   - Lower it and check the flex bench's "shaking" and "ringing" phases at 40 m.
   - Consider capping the tip's offset from one impulse.
   - The user judges by eye: show before and after.
8. **A vine whose ground is blown away must fall.**
   - The planet is deformable (`PlanetTerrain.Crater`, signal
     `TerrainDeformed(centre, radius)`).
   - A vine is anchored by its own bottom voxel layer, not by the terrain.
   - On `TerrainDeformed` near a vine's foot, sample the ground under the
     foot. If it dropped below the foot by more than the vine's `SinkM` plus a
     margin, release the vine: carve its foot (as `PlantFlex.BenchCarveFoot`
     does) so the integrity check and give-way fell it, or add an explicit
     "unrooted" path.
   - The flats are not deformable; test on the planet with a bench phase that
     craters a vine's pad.

## Then continue the original build order

- **Step 7, pods and hanging pickles (decision 0034).**
  - A bulk `PodField` (state, regrow timer, vine), never a node per pod.
  - Pickles hang through the horde's instancing and drop into `HordeManager`
    as a new `SpawnOrigin.Pod` when the colony is aggravated.
  - Regrowth numbers go in a resource. The win check, plus the bonus for
    killing every pickle.
  - A bench phase for 25,000 hanging pickles.
  - The nest's pickles become the lab's horde.
- **Step 8, the main-screen tool.** It is what the user has asked to see since
  the start: "generate the nest, see it, press one button to play it".
  - Nest tab: kit, seed, population, horde on/off, Generate into the editor
    viewport, metrics.
  - Play: hand off by a `user://` file to `lvl_nest_lab` through
    `EditorInterface.PlayCustomScene`.
  - After play: Good, Bad, a note, Save (Good only).
  - Range lab and the vine panel (today's dock).
  - Both JSON Lines logs, each starting with its `"about"` line.
  - Consider doing it before step 7 if the user agrees. They wanted to see
    nests now.
- **Step 9, decisions.**
  - Rewrite 0033 from the load numbers: generate at load is about 20 s at
    25,000; pre-generated saved nests is the alternative.
  - Write records for:
    - leaves as meshes;
    - nest sizing (with the user's answer on clumping);
    - the 1,260 m planet;
    - the Structure layer;
    - the deterministic re-roll gate;
    - the tool.

## Known and not yet done

- **Memory at 25,000 is about 1.9 GB.** Dense voxel grids, plus a chunk map at
  an int per grid cell, live in several copies per vine (factory data, node
  meta, `VoxelGrid`). Sparse or brick storage, or dropping copies, is the cure.
- **From above, about 720 draws.** Each vine has its own leaf MultiMeshes
  (three shapes plus stems). A nest-wide MultiMesh per shape, with the vine's
  flex row in instance custom data, would cut it.
- **Planting still costs about 50 ms per vine on the main thread**
  (`VoxelBody` setup and coarse copies meshed at `_Ready`). The rest of the
  load is under the planet's generation.
- **Leafy is 46 % valid on its own roll** (it sags). Fine for the re-roll, but
  a range-lab candidate.
- **`resources/ui/font_display.tres` is missing.** `PickleStyle` loads it, and
  the loading overlay errors on every load. This predates the vine work; ask
  the user where it went.
- **The tier's horde count is not the nest's population.** `pop=25000` sized
  the nest; the contract's tier still brought 12,000 agents to the compound.

## How to work here (learned the hard way)

- **Check the power before any frame number.** In PowerShell,
  `(Get-CimInstance Win32_Battery).BatteryStatus`: 2 is mains, 1 is battery.
  On battery every phase reads 16.6 ms.
- **Foreground `sleep` is blocked in this harness.** Cool the laptop with
  headless work between windowed benches instead.
- **Python edits with `\n` inside C# or GDScript strings write real newlines.**
  Use the Edit tool for those lines, or `chr(10)` with care.
- **Headless level loads are the fast way to measure a nest.** Load
  `lvl_nest_lab` in a small `--script` SceneTree, wait until
  `current_scene.Loading` is false, and read NestBuilder's `[nest]` lines.
  Physics per tick is `Performance.TIME_PHYSICS_PROCESS`. Switching nodes off
  in stages isolated both Jolt costs above.
- **A frame over 16.7 ms makes Godot run up to 8 physics ticks.** When a frame
  reads about 133 ms, look for a physics tick of about 17 ms.
- **Nothing that calls into the engine may run on the nest's worker threads.**
  That includes `Resource.Duplicate` and `GodotObject.Get` on resources. Read
  values on the main thread first (`VineGrower.Context`).
- **The root is not in the tree during a SceneTree script's `_initialize`.**
  Wait one `process_frame` first, or use local transforms.
- **Tools:**
  - `vinelook.gd -- <png> [elevation|leaves]` photographs the baked vines or
    the leaf shapes.
  - `bench.gd flex mem:<kind>:<seed> 40` benches a vine grown in memory.
  - The bench's warm-up now waits while a level is loading.
