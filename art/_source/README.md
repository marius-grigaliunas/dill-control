# Art sources, and the two pipelines

Art comes two ways: **Blender** (`.blend` to `.glb`, mapped onto the palette;
the first half of this file) and **voxels** (MagicaVoxel `.vox`, imported
directly and animated by code; the second half). Pickles, weapons and smooth
props are Blender. Mechs, robots and blocky props are voxels.

This folder holds the files art is *made* in: `.blend` files, the palette's paint
source, reference images, and bought packs as downloaded. Godot never imports it (`.gdignore`) and Git LFS
stores it. What the game loads lives next door, in `art/models/`,
`art/textures/` and `art/materials/`.

The reasoning is in `docs/decisions/0005-art-pipeline.md`. This is the checklist.

## Setting up a new model in Blender

1. **Scale:** Scene Properties, Units: Metric, Unit Scale 1.0. One unit is one
   metre. A standard pickle is about 1.8 m tall; the player's eye is at 1.7 m.
2. **Facing:** the front of the model looks along Blender's **-Y**, which is what
   *Front* view (numpad 1) shows you. It arrives in Godot facing `+Z`.
3. **Origin:** at the feet for anything that stands, at the grip for a weapon,
   at the hinge for anything that rotates.
4. **Before export:** Object, Apply, All Transforms. A model with an unapplied
   scale imports at the wrong size and lights wrongly.

## Colour: the palette

Every model shares one texture, `art/textures/palette.png`: a 256x256 grid of
16x16 px swatches, one colour family per row, light on the left and dark on the
right.

1. Give the model one material named **`palette`**, with `palette.png` as its
   Base Color image. Its settings in Blender do not matter beyond preview; Godot
   replaces it.
2. Select faces, `U`, *Project from View* or *Unwrap*, then in the UV editor
   scale the island right down (`S 0`) and drop it in the middle of the swatch
   you want. A face's colour is the swatch it sits in.
3. For a gradient across a part, stretch its UVs along a row.

Repainting a swatch recolours every model that uses it. Keep the 16 px grid when
you repaint, or UVs land on the wrong colour.

## Export

*File, Export, glTF 2.0*:

| Setting | Value |
|---|---|
| Format | glTF Binary (`.glb`) |
| Include | Selected Objects (or a collection), not the whole scene |
| Transform | +Y Up (the default) |
| Data, Mesh | Apply Modifiers on; UVs on; Normals on |
| Data, Material | Export |
| Animation | Off, unless the model has actions |

Save to `art/models/<prefix>_<name>.glb`, for example `art/models/prp_spire_a.glb`.
Keep the `.blend` in `art/_source/` with the same name.

## In Godot

1. Godot imports the `.glb` on focus. Double-click it to open **Advanced Import
   Settings**.
2. Under *Materials*, select `palette`, tick *Use External*, and point it at
   `res://art/materials/mat_palette_toon.tres`. Reimport. Every model mapped this
   way shares one material, which is what lets thousands of them batch.
3. **Wrap it.** New scene, root a `Node3D` (or a body if it collides), instance
   the `.glb` under it, add scripts, hitboxes and collision there. Save as
   `scenes/props/<same name>.tscn`. Never edit the imported scene itself;
   re-exporting from Blender would wipe the edits.
4. **Collision**, if the prop needs it and is not a voxel body: either add a
   simple shape in the wrapper, or name a helper mesh in Blender with a
   `-colonly` (exact) or `-convcolonly` (convex, cheaper) suffix and Godot turns
   it into a collider on import.

## The first test asset (M0)

Make something small, a crate or a stand-in pickle, and take it through every
step above. Drop its wrapper scene under `Props/TestAsset` in
`scenes/levels/lvl_pipeline_test.tscn`, then:

```
"$GODOT" --path . --script scripts/debug/bench.gd -- res://scenes/levels/lvl_pipeline_test.tscn look
```

and look at `perflogs/look.png`. Check that it faces the camera (the camera looks
along `-Z`, so a model facing `+Z` faces it), that it is the size you modelled,
and that its colours are the swatches you picked. Then replace the two *Not yet
verified* lines in `CLAUDE.md` with what you found.

---

# Voxel models: from the tablet to a walking mech

The reasoning is in `docs/decisions/0009-voxel-models.md` (the importer) and
`0010-procedural-mech.md` (the animation). This is the checklist.

A `.vox` is never exported to anything. Godot imports it directly with our
importer (`addons/vox_importer/`), so a voxel model's working file lives in
`art/voxels/`, not here. This folder keeps bought packs exactly as downloaded.

**Nothing is animated by hand.** A model is parts with hinges, and code moves
the parts: legs step, knees bend, bodies bob and lean, turrets turn, arms
swing. What you make is the shape and where it bends.

## The flow

```
 Tablet                 PC, MagicaVoxel             Godot
 ------                 ---------------             -----
 model the shape  --->  split into parts,     --->  reimports on focus
 paint, set glow        name them, mark             wrapper scene: walker,
                        hinges and points            brain, guns, collision
                        save to art/voxels/          tune the .tres, bench
```

### 1. Model it (tablet or PC)

Any editor that saves `.vox`. On an Android tablet:

- **Goxel** (free, open source, layers, saves `.vox`).
- **Mega Voxels** (made for phones and tablets, `.vox` in and out).
- **FunVoxel** (`.vox` in and out; reported limit 64x64x64).
- Or MagicaVoxel itself, streamed from the PC with a remote desktop app.

Sync one folder from the tablet straight into `art/voxels/` (Syncthing or a
cloud folder), so a save on the tablet is a reimport in Godot.

- **Scale:** design at whatever voxel count reads well; the voxel size is set
  per file at import (step 4). Reference heights in the game: the player and
  the storage bot 1.8 m, the tank 5.6 m, the field fighter 6.5 m.
- **Facing:** the front looks along MagicaVoxel's `-Y`. It arrives facing
  Godot's `+Z`. The tank's gun is a known-good reference: a model that arrives
  backwards is turned in MagicaVoxel, never in the wrapper.
- **Feet on the ground:** MagicaVoxel stands a model on its ground plane.
  Leave it there (do not lift it in the world view): that plane is the
  game's ground, and the wrapper's origin.
- **Colour:** any palette. Every voxel model shares one material whatever its
  palette; the colours are written into the mesh.
- **Light:** select a colour, set its material to **Emit** in the Render tab.
  **Weight** and **Power** set how bright. It glows in the game the same way
  (a visor, a neon strip, a lit window) and costs nothing. A halo around it is
  the level's glow setting, and light spilling onto the ground is faked (0009).
- **Palette index 255 is reserved** (the last swatch). Do not paint with it.
- **Legs that bend:** model them standing, slightly bent if you can. A leg
  modelled dead straight still works; its tuning says which way the knee goes
  (`KneeBias`).

### 2. Split it into parts (PC, MagicaVoxel)

Tablet editors do not all keep named objects, so this pass is done in
MagicaVoxel. Anything that moves on its own is a separate **object** in the
world view (Tab).

- **Name every object** in the outliner. The name becomes the node's name in
  Godot, and the walker finds its parts by these names in the wrapper.
- **Mark each hinge:** one voxel in palette index 255 where the part turns.
  It is not drawn; the part's origin goes there. Put it in the part's own empty
  space, just beyond the part (a thigh's pivot sits one voxel above the thigh),
  so it never leaves a hole. A part without one turns about its centre.
- **Mark points:** a one-voxel object named `@Something` draws nothing and
  becomes a marker at that voxel.
- Hidden objects and hidden layers are not imported: keep reference there.

The names a walking mech uses:

| Part | Name | Hinge (index 255 voxel) |
|---|---|---|
| Everything that does not move | `Body` | none |
| Each leg, top to bottom | `ThighFL` `ShinFL` `FootFL` ... | hip, knee, ankle |
| Turret, if any | `Turret` | the ring it turns on |
| Arms, if any | `ArmR` `ArmL` | the shoulder |

Leg suffixes: `FL` `FR` `BL` `BR` for four legs, `L` `R` for two. Left is the
model's own left, which is MagicaVoxel's `+X` side seen from the front.

| Marker | What it is |
|---|---|
| `@Seat` | where the rider's eyes go |
| `@Muzzle` | where the main gun's shots leave |
| `@TurretMuzzle`, `@Cannon` | a second gun |

A bought model that comes as one merged object (as all three Max Parata robots
did) can be split by region with the scripts in `tools/vox-tools/`, which also
rebuild a `.vox` from a pack's OBJ when the `.vox` is missing.

### 3. Save into `art/voxels/`

`art/voxels/<prefix>_<name>.vox`, for example `mch_field_fighter.vox`. Godot
reimports on focus. If it is not ours, add it to `credits.md` now, and keep the
download untouched in `art/_source/<Name>/`.

### 4. Size it (Godot)

Select the `.vox`, open the Import dock, set **voxel/size** to the metres one
voxel should be, Reimport. The size the model must come out at is the job; the
voxel size is whatever gets it there (height in voxels times voxel size).

### 5. Wrap it (Godot)

A new scene in `scenes/vehicles/`, same name. Copy the nearest existing one:

| Like | Copy | Has |
|---|---|---|
| A rideable walker with a turret | `mch_quadruped_tank.tscn` | walker, companion, gunner, seat camera |
| A rideable biped with arms | `mch_field_fighter.tscn` | as above, arms instead of a turret |
| A follower that carries something | `mch_mobile_storage.tscn` | walker, companion, supply |

Point it at the new `.vox`, re-wire the part lists (`Thighs`, `Shins`, `Feet`,
`Arms`, `Turret`, `Mounted`/`MountedOn` for markers that ride a moving part),
and size the `Hull` box to the body. Give it its own tuning `.tres`.

Every wrapper has a `Voxels` node (`VoxelBody`): that is what makes the model
break the way every voxel model breaks (`docs/decisions/0011`). For a building
or a prop tick **Static** and give the root no collision of its own; for a mech
list its moving parts under **Hinged**. Nothing else: no hit points, no damage
code. A prop's wrapper is just the model and that node
(`scenes/props/prp_graybox_pillar.tscn`).

### 6. Tune it and bench it

Everything about how it moves is in its `MechTuning` `.tres`: speed, turn rate,
step length and height, how far the body sinks and leans, the knee direction,
how far the arms swing, where it follows. Its guns are in `MechGunStats`.
Changing them needs no rebuild.

```
"$GODOT" --path . --script scripts/debug/bench.gd -- res://scenes/levels/lvl_horde_flats.tscn mechs
"$GODOT" --path . --script scripts/debug/bench.gd -- res://scenes/levels/lvl_horde_flats.tscn mechsab
```

`mechs` walks, rides and fights with the allies and saves screenshots;
`mechsab` measures what they cost to draw. A new mech goes into both.

## What animation exists, and where it comes from

| Motion | Made by |
|---|---|
| Walking in any direction, turning on the spot, stopping | feet that step when the body leaves them behind (`MechWalker`) |
| Knees bending, feet flat on the ground | two-bone IK per leg (`MechLeg`) |
| Bob, sink at speed, lean, squash on landing, recoil | a spring on the body |
| Jump wind-up, jets, legs hanging in the air | the walker |
| Turret tracking, arm swing | the walker |
| Choosing targets, firing | `MechGunner` |

None of it is keyframed, and MagicaVoxel's animation frames are ignored.
