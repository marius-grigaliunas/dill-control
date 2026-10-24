# 0035 A vine's leaves are meshes on its section bodies, not voxels; tendrils are gone

*24 October 2026. Built. Builds on 0011, 0029 and 0032.*

## Context

A vine's leaves, stems and tendrils were voxels in its model. They were about
20 % of a Fruiting vine's voxels but about 50 % of its faces and collision
boxes, and added bodies (7 against 4). On a Leafy vine they were 70 % of the
voxels, 88 % of the faces and 89 % of the boxes. At 117 unique vines a nest,
that was the difference between a nest that draws and one that does not.
Voxel leaves also read as green slabs, not leaves.

## Decision

**Leaves are a few heart-shaped meshes per kind, drawn as MultiMeshes and
bent by the same shader as the stalk; each has one thin box on its section's
existing body. Tendrils are removed.**

- **Look.** `LeafMesh` makes a few blade shapes a kind: broad, lobed with
  pointed teeth, cupped, darker veins in vertex colour, a stem running into
  the blade's notch (`LeafMesh.StemJoin`). `VineLeaves` scales, turns and
  tilts them per leaf (12-25 degrees, so their faces show from below), 10-18
  m across, about 70-80 a vine, in `mat_leaf_toon` with the ink outline.
  Blades have LODs: without them the leaf overhaul cost about 6 ms of GPU
  on the ground at 25,000; with them nothing measurable.
- **Bending.** `shaders/flex_bend.gdshaderinc` moves leaves exactly as
  `voxel_toon` moves the voxels. No sag of their own.
- **Collision.** One thin box per leaf, and one walkable beam per stem
  (`StemWalkWidthM`, wider than drawn), added to its section's body with
  `VoxelPart.AddExtraShape`: no new bodies, and they follow the bend. A leaf
  can be stood on and hooked; the pull goes into the arm.
- **Damage.** Through `VoxelBody.Intercept`. A hit punches a hole where it
  lands (a per-vine texture read by `mat_leaf_toon`); a leaf badly holed, or
  shot where the stem joins (`StemZoneShare`), tears off whole, under the
  loose-piece cap, as a light body on Litter (0039). A cut arm takes its
  leaves.
- **Checks.** `vinecheck` holds every stem to a solid cell and every stem
  end inside its blade, baked and in memory.
- Numbers: `resources/foliage/leaf_default.tres`. A baked vine's leaves are
  `art/voxels/prp_vine_<kind>_<seed>_leaves.tres`.

## What it costs

- Leaves are a second system beside the voxels: their own drawing, hits and
  tearing, not 0011's carve-and-split.
- Leaves held near level so they are platforms read thinner from the side
  than a real cucumber leaf. Drooping leaves that cannot be stood on were
  offered and not chosen.
- Each vine has its own leaf MultiMeshes: a large share of a nest's draws
  from above (about 900). A nest-wide MultiMesh per shape would cut them.
