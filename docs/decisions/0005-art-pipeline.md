# 0005 The art pipeline

*8 October 2026*

## Context

One person makes the art, in Blender, for a cel-shaded game that draws 25,000 of
its enemy on an integrated GPU sharing memory with the CPU.

## Decisions

**glTF binary (`.glb`), exported from Blender.** Godot's first-class import
format, and what Blender's own exporter writes best. Godot can also import
`.blend` files directly, which is a faster loop, but then every import needs
Blender installed and its path set, including the unattended bench on a fresh
clone; a Blender upgrade can change what gets imported; and a `.blend` carries
everything that was being tried. The choice is per file, so it can be revisited.

**Sources in `art/_source/`**, ignored by Godot and stored by LFS. Exports in
`art/models/`.

**One palette texture and one material.** Every model is UV-mapped onto swatches
of `art/textures/palette.png` and drawn with `mat_palette_toon.tres` (toon
diffuse and specular, nearest filtering), mapped as an external material in the
import dialog. One material batches, matches the toon-and-ink look, recolours
the game from one image, and costs almost no texture memory.

**Imported scenes are wrapped, never edited.** A `.tscn` instances the `.glb` and
owns everything added to it, so a re-export cannot wipe work.

**Pickles are animated in the vertex shader.** They have no limbs, so waddling,
wobbling, squashing, stretching and flopping are deformations along one axis,
driven by a phase and a state passed per instance through the multimesh's
`INSTANCE_CUSTOM`. No rig, no vertex-animation-texture bake, and one shader for
the whole horde. The lab had planned vertex animation textures; limbless pickles
make them unnecessary. Models need edge loops along their length to bend, and a
triangle budget the horde bench sets (start near 200; the lab's capsule was 72).

**First-person hands are the one rigged thing**, and come late (M7). Gun-only
viewmodels, moved by the procedural sway and recoil the lab already has, carry
the earlier milestones.

**Destructible structures stay voxel bodies,** authored in `voxel-bench` from
M2, because being carved and collapsing is what they are for.

**LFS from the first commit.**

## Added: voxel art, and animation by code

*8 October 2026. The decisions above stand for Blender art; this adds a second
pipeline beside it.*

**Two pipelines, chosen by what the thing is.** Pickles, weapons and smooth
props stay Blender, `.glb`, the palette and the shader-animated horde above.
Mechs, robots and blocky props are voxel art: made on a tablet or in
MagicaVoxel, saved as `.vox` straight into `art/voxels/`, imported by our own
importer with colour in the vertices and emission from MagicaVoxel's own
materials (0009). Bought voxel packs come in the same way.

**The tablet models; the PC finishes.** Shape and paint can be done in any
editor that saves `.vox`, on an Android tablet. Splitting into named parts,
marking hinges (palette index 255) and points (`@Seat`, `@Muzzle`) is done in
MagicaVoxel on the PC, because tablet editors do not reliably keep named
objects. That pass is what turns a model into something code can move.

**Nothing is animated by hand.** This extends the pickles' rule above to
everything voxel: walking, turning, jumping, leaning, aiming and arm swing are
procedural (`MechWalker`, two-bone IK in `MechLeg`, 0010). A model's whole
animation brief is where it bends. The first-person hands remain the one
rigged thing.

**Size is set at import, not in the model.** One voxel's size in metres is an
import option per file, so a model is built at whatever voxel count reads well
and sized to its role afterwards: the storage bot 1.8 m, the tank 5.6 m, the
field fighter 6.5 m.

The checklist is the second half of `art/_source/README.md`.

## Still to verify

Facing (`-Y` in Blender to `+Z` in Godot) and scale, with a Blender test asset.
Verified for voxel art: MagicaVoxel's `-Y` arrives facing `+Z`, at the voxel
size the import sets.
