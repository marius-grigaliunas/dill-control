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
