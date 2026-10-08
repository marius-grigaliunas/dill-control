# 0009 Voxel models from MagicaVoxel

*8 October 2026*

## Context

Some of the art is voxel art: made in MagicaVoxel (or a tablet editor that
writes `.vox`), or bought as `.vox` packs. The first is the Quadruped Tank from
Max Parata's Voxel Robot Collection (`credits.md`). 0005 covers Blender models;
it said nothing about `.vox`, and its one-palette rule does not fit files that
each carry their own 256 colours.

MagicaVoxel's OBJ export loses what matters: it merges every object into one
mesh, so nothing can move, and it drops the emissive materials, so a visor and
its lights arrive as flat paint.

## Decisions

**Godot imports `.vox` itself,** through our own scene importer in
`addons/vox_importer/` (Godot finds editor plugins only under `addons/`). A
`.vox` in `art/voxels/` behaves like a `.glb`: saved, reimported on focus,
instanced by a wrapper `.tscn` that is never overwritten. No OBJ, no Blender
step. The parser and mesher are runtime code in `scripts/voxel/`, so a
destructible body can be built from the same file later.

**The MagicaVoxel scene graph becomes nodes.** Groups become `Node3D`s and
objects become mesh nodes, named as in MagicaVoxel. That is what lets a mech's
legs and turret be moved by code. Two conventions carry what MagicaVoxel has no
field for:

- a voxel in **palette index 255** is not drawn, and its centre is the
  object's origin: the hinge a leg turns about. One per object; without one,
  the origin is the object's centre. The index is an import option, for packs
  that paint with 255.
- an object named **`@Something`** becomes a `Marker3D` named `Something` at the
  centre of its voxels and draws nothing: seats, muzzles, feet, jet nozzles.

**Colour per vertex, one material.** The importer writes each voxel's palette
colour into the vertices. Every voxel model shares `mat_voxel_toon.tres`
(`shaders/voxel_toon.gdshader`, the same toon lighting as `mat_palette_toon`)
whatever palette it was painted with, and nothing is remapped. A shared
MagicaVoxel palette was the alternative; it would have meant repainting every
bought model and losing colours to a nearest-match.

**Emission comes from MagicaVoxel's own materials.** A palette index set to
Emit in MagicaVoxel glows: strength is its weight times one plus its power,
written into `UV.x`. The shader draws an emissive voxel unlit, so it reads as a
light at night. This costs nothing: it is a colour, not a light.

**Scale is 0.1 m per voxel by default,** MagicaVoxel's own export scale and the
pack's; an import option per file. MagicaVoxel's front (`-Y`) arrives facing
Godot's `+Z`.

## Measured

The tank imports as one mesh of 28,399 voxels. The `look` bench shows it facing
the camera, 5.6 m tall against the 1.8 m stand-in pickle, colours as in the
pack, visor and lights emissive. GPU mean 6.2 ms for the whole pipeline level
on the target machine.

## What glow costs

The halo around a light is not the emission; it is the environment's glow, a
full-screen pass. The lab measured it at 2.6 ms of GPU (15-20 % of a fight
frame) on the target machine. Neon under the bike and lit windows on the night
side are emissive voxels and cost nothing; their bloom is that 2.6 ms, and is a
decision for when there is a night side to bench, not one taken here.

**Real lights are not cheap.** The bike's underglow was first one shadowless
`OmniLight3D` of 4.5 m range. Paired A/B in `lvl_horde_flats` (`bench.gd
mechsab`): 9.1 ms GPU with it, 7.3 without, 10.1 with it again. One positional
light puts the whole frame on the clustered lighting path. Light spilled onto
the ground is faked instead: an additive gradient quad under the source, which
costs its few pixels. A real light is a decision with a bench pair behind it.

## Still to do

- `.vox` to `VoxelModel` for destructible bodies, when voxel bodies are ported
  (M2). This changes plan.md's "voxel models stay in voxel-bench": authoring
  moves to MagicaVoxel, the bench stays for previewing destruction.
- Collision is the wrapper's job, as for a `.glb`: a few boxes, not the mesh.
