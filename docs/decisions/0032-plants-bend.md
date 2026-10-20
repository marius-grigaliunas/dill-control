# 0032 Plants bend: a spine of springs, collision that follows, and they fall over

*20 October 2026. Proposed. Builds on 0011, 0014, 0015 and 0024; a step toward 0029.*

## Context

A vine or a tree was a static `VoxelBody`: it broke well and never moved. A
rigid chunk cannot bend, and a hive of vines that never sway, never give under
a swing and never sag when shot does not sell vines. The game is still finding
its fun, and swinging through living vines is the bet.

A rope for every vine (points held by distance, solved each tick) bends best,
but cuts loose from the voxel destruction every other thing uses (0011): its
own skin, its own hit tests for every weapon, its own ground contact. A first
version bent only the drawing and left collision where the model was built.
Played, that failed three ways: a leaf seen swaying could not be stood on, a
swaying vine did not carry the player, and a vine cut deep never fell, it
leaned on its limit and held (and what did come loose stood up on its stump,
damped and crushing down it like a storey).

## Decision

**A plant keeps its `VoxelBody` and gains a `PlantFlex`: a spine of stiff
segments joined by bending springs (`Rod`). The shader bends the mesh by it,
its collision sections move with it, and cut weak enough it gives way and
falls over.**

- **The voxels make the spine.** At load `PlantFlex` counts the plant's voxels
  layer by layer. Each segment weighs its share of `MassKg`; each joint is as
  stiff as the thinnest layer near it, to the power `Taper`. Every joint is
  then scaled together, timed by swaying the rod, until the plant sways at
  `PeriodSeconds`. A rod shape is timed once and remembered.
- **One texture bends every plant's drawing.** `FlexField` steps every rod
  each physics tick and writes a row per plant (foot, up, segment length, each
  point's move). `voxel_toon` moves each vertex by its height along the row
  its mesh's `flex_row` names. Everything else keeps `flex_row = -1`.
- **Collision follows.** Each collision section (a static body) is moved by
  the bend at its middle and given that speed as `constant_linear_velocity`,
  so a player standing on it rides it (checked: Jolt carries a character on a
  static body moved that way). Every tick within `FollowNearMetres` of the
  camera; further off only once a section is `FollowFarStep` behind. Sections
  shear, they do not turn. Hits, blast points, crushes and tears are taken
  back to where the voxels are (`IVoxelBend` on `VoxelBody`), so they bite
  where they landed.
- **What pushes it.** Wind (`WindStats`, gusts rolling downwind, no random
  numbers); an explosion, through `VoxelBody.Damaged`, `KickPerDamage` per
  point of its damage (only a detonation: `Blast.Settings.Explosive`, set by
  rockets and grenades; a round, a blade, a slam, a flame or a beam bites
  the voxels and leaves the plant still; checked, 64 rifle hits, no shake);
  the grapple line, with the player's weight and the swing's pull
  (`GrappleLoadKg`, `GrappleLoadMaxG`); and its own weight once it leans.
- **A cut goes soft, then gives way.** When the grid's `Version` moves, a
  voxel worker counts a copy of the cells again (0015): stiffness times
  (section left) ^ `Weakening`, mass from what remains. A joint left with less
  than `GiveWayBelow` of its stiffness that kinks past `GiveWayDegrees` (or
  holds the plant on `MaxLeanDegrees`) for `GiveWaySeconds` gives way: the
  layer is cut through and the voxels split as for any cut (0024).
- **What breaks off leaves as it was drawn.** A piece is turned and moved to
  its drawn pose, with the spine's speed (`IVoxelBend.Release`). A piece
  `SlenderMetres` long or more falls with `FallingAngularDamp` and does not
  crush through what it lands on (`VoxelChunk.CrushesThrough`): it tips over
  its stump and smashes where it lands. What gave way hangs on a hinge just
  off the middle of the cut, on the side the weight above it leans (centres
  measured at load), starting to turn at `ToppleSpin` with the matching
  speed, and tears free as a building's hinge does (0014).
- **Data.** `resources/foliage/flex_vine.tres`, `flex_tree.tres`,
  `wind_default.tres`. A wrapper gets a `Flex` node after its model with
  `Body` and `Stats`; nothing in a level changes.

## Consequences

- **A forest of 200** (`prp_vine_forest.tscn`, written by
  `tools/vine-forest/make_forest.py`, east of `lvl_horde_flats`' spawn) first
  ran at 7-11 fps: 45 meshes a vine (10-15k draws, 2 GB of video memory), and
  9,000 collision sections moved every tick. What made it run:
  - **Drawn whole until changed** (`VoxelBody.WholeUntilChanged`): an intact
    vine is its imported mesh, one draw, shared by every instance of that
    vine; its sections are meshed on the workers at its first change, and the
    whole mesh goes once they are up. Past `CoarseFromMetres` and
    `CoarsestFromMetres` it is drawn from 2x and 4x blocks of its own voxels
    (28k, ~9k, ~3k triangles), built once a kind. All of them bend.
  - **A material per plant**, not an instance uniform: every mesh of an
    instance-uniform shader takes a block of a fixed global buffer, and 9,000
    overflowed it.
  - **Distance tiers**: collision sections within `FollowSectionMetres` of
    the camera follow every tick (and carry the player); the rest catch up in
    `FollowFarStep` steps, a plant at a time; spines past `StepFarMetres`
    step every `StepFarEvery` ticks.
  Result, plugged in but warm: from spawn 14.8 ms; at its edge 17.0; in its
  middle 16.4 (33 of 366 frames over); swinging above it 20.4, GPU-bound at
  17 ms with all 200 in view. Video memory 286 MB, draws 160-350, plants
  1.6-3.5 ms of CPU. Not yet the target above the canopy.
- **20 m apart was too dense to play**: no room to swing, and one felled vine
  took its neighbours with it. Respaced 100 m apart (the script's third
  argument), only 85 fit on `lvl_horde_flats`' open ground, in a ring round
  its built-up middle; they run at 11.7-14.8 ms in every phase of `forest`,
  and a swing under one goes through a full arc to 65 m up. What makes a good
  nest (spacing, reach between anchors, what one falling vine may take with
  it) is the next thing to define.
- **Bodies are a budget.** Jolt holds a fixed number of bodies, and a static
  `VoxelBody` has one per collision section: a vine on 32-voxel sections was
  about 42. The 200-vine forest plus the level was about 9,400 before anything
  broke; destruction (chunks, rubble heaps) went past 10,240, a heap's body
  failed to be made and every hinge after it had nothing to hang on (45
  errors). Vines now use 64-voxel sections (`SectionEdge` in
  `voxel_destruction_vine.tres`: about 13 bodies a vine, the 85-vine level at
  1,955 bodies at rest), and the limit is 32,768
  (`physics/jolt_physics_3d/limits/max_bodies`). Larger sections shear more
  coarsely when collision follows the bend; standing on a vine's arm 46 m up
  still rides it.
- A bitten vine (28 % of its foot left) kinks, gives way, hangs on its hinge
  at 14 m/s, is 15 degrees over in 3 s and breaks up on the ground in about 5.
  The break-up of an 18,800-voxel piece costs frames (worst 140 ms, 43 over
  in 2 s): the debris system's, not the bend's, and the same for a felled tree.
- Within a section (16 m) the drawn shape and its boxes part by the bend
  across it: centimetres in wind, more at the top of a vine bent hard.
- Arms, leaves and tendrils move with the stalk at their height: they shear
  rather than swing.
- Bench: `bench.gd ... lvl_horde_flats.tscn flex` (wind, rockets, a swing,
  standing on an arm, a bite at the foot and the fall); headless:
  `flexcheck.gd`.
