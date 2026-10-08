# Art sources and the Blender pipeline

This folder holds the files art is *made* in: `.blend` files, the palette's paint
source, reference images. Godot never imports it (`.gdignore`) and Git LFS
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

# Voxel models (MagicaVoxel)

The reasoning is in `docs/decisions/0009-voxel-models.md`. A `.vox` is not
exported to anything: Godot imports it directly, with our importer
(`addons/vox_importer/`). So a voxel model's working file lives in
`art/voxels/`, not here. This folder keeps bought packs exactly as downloaded.

## Making one

- **Scale:** one voxel is 0.1 m. A standard pickle is 18 voxels tall; the
  player's eye is at 17. Another size is the import option `voxel/size`.
- **Facing:** the front looks along MagicaVoxel's `-Y`. It arrives facing
  Godot's `+Z`. The tank's gun is a known-good reference: a model that arrives
  backwards is turned in MagicaVoxel, never in the wrapper.
- **Colour:** any palette. Every voxel model shares one material whatever its
  palette; the colours are written into the mesh.
- **Light:** to make a colour glow, select it in the palette and set its
  material to **Emit** in the Render tab. **Weight** and **Power** set how bright
  it is. It glows in the game the same way: a visor, a neon strip, a lit window.
  The glow costs nothing; a halo around it is the level's glow setting.
- **Palette index 255 is reserved** (the last swatch). Do not paint with it;
  it marks pivots (below).

## Parts that move

Anything that moves on its own (a leg, a turret, a hatch) is a separate
**object** in MagicaVoxel's world view (Tab), not part of one big model.

- **Name every object** (the outliner on the right). The name becomes the
  node's name in Godot, and the code that moves a part finds it by that name
  in the wrapper: `Body`, `Turret`, `Gun`, `HipFL`, `ShinFL`, ...
- **Mark the hinge:** paint one voxel in palette index 255 where the part
  turns: the hip of an upper leg, the knee of a shin, the ring of a turret.
  That voxel is not drawn, and the part's origin goes there. Put it inside the
  part, where the hole it leaves cannot be seen. A part without one turns
  about its centre.
- **Mark points:** a small object whose name starts with `@` draws nothing and
  becomes a marker: `@Seat` (where the rider's eyes go), `@Muzzle` (where shots
  leave), `@FootFL` (where a foot touches the ground), `@Jet` (a nozzle). One
  voxel is enough; the marker goes to its centre.
- **Groups** come across as groups, so `LegFL` holding `HipFL` and `ShinFL` is
  tidy, but nothing depends on it.
- Hidden objects and hidden layers are not imported: keep reference models
  there.

No keyframes. MagicaVoxel's animation frames are ignored; every part is moved
by code (`docs/decisions/0009`).

## Into Godot

1. Save as `art/voxels/<prefix>_<name>.vox`, for example
   `art/voxels/mch_quadruped_tank.vox`. Godot reimports on focus.
2. Wrap it, as for a `.glb`: a new scene, its root a body or a `Node3D`, the
   `.vox` instanced under it, collision as a few boxes, scripts. Save as
   `scenes/<kind>/<same name>.tscn`.
3. If it is not ours, add it to `credits.md` now.

## On the tablet

MagicaVoxel runs on Windows and macOS only. On an Android tablet, any editor
that saves `.vox` works for modelling, then the parts, names and pivots are
checked on the PC in MagicaVoxel:

- **Goxel** (free, open source, has layers, saves `.vox`).
- **Mega Voxels** (made for phones and tablets, imports and exports `.vox`).
- **FunVoxel** (`.vox` in and out; reported limit 64x64x64).
- Or MagicaVoxel itself, streamed from the PC to the tablet with a remote
  desktop app, which keeps the real tool and the pen.

To move files, sync one folder from the tablet straight into `art/voxels/`
(Syncthing, or a cloud folder), so a save on the tablet is a reimport in
Godot.
