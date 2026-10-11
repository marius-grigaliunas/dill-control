# 0024 Trees hold by connection, not by weight

*11 October 2026. Builds on 0011 and 0013.*

## Context

The first tree, `prp_tree_tall_a` (0.5 m voxels, 40 x 37 x 72 m, 30,352
voxels), was a static `VoxelBody` on the default stats. Any hit anywhere on
it, a branch included, cut the whole tree through about 25 m up and dropped
everything above.

That is 0013's load check doing what it was written for. It asks each layer
whether it still carries what is above it, and counts a load that hangs off
the layer's centre against it (`offCentre`, against half the layer's narrower
side). A trunk is a few metres wide and the canopy hangs off it by more than
that. Run on the untouched model, every trunk layer from 50 up already fails,
mostly with an off-centre of 1.0. The first hit asks for a check of the whole
connected tree, and the lowest failure, layer 50, comes down.

## Decision

**Trees get their own stats resource, `voxel_destruction_tree.tres`, with
`MinSupportFraction = 0`**, which turns the weight check off. A tree then
holds by connection alone: a branch shot free falls, a trunk cut through
brings down everything above the cut, and nothing else moves. Whatever comes
loose still lands, crushes and snaps like any other piece.

**Nothing else is in it, because nothing else made a difference.** A stats
resource reaches only what decides how its own model splits: carving,
structure, collapse, fracture and the section size. Loose pieces are made with
`VoxelDebris.Stats`, the default, so density, settling, breaking on landing
and collision boxes per piece cannot be per tree. The per-frame budgets are
world-wide and stay in the default. Two settings were tried and dropped:

| Tried | Result |
|---|---|
| `SectionEdge = 16` (default 32) | Draw calls ~510 to ~1,490 (each section is a mesh); frames over budget 5.7% to 15.3%; worst 62.8 ms |
| `MinChunkVoxels = 16` (default 6) | Within run-to-run noise: 5.7% and 14.2% over budget at 16, 13.6% at 6 |

A trunk chewed most of the way through still stands on what is left until it
is cut all the way through. A tree mode of the load check that counts how much
trunk is left and ignores the lean would fix that, if play asks for it.

## Measured

`bench.gd ... lvl_horde_flats.tscn tree quiet`: rockets into the canopy of
`prp_tree_tall_a2`, then a slit 2 m up the trunk, horde held off, plugged in,
22.5 s recorded. Mean 12.7-13.9 ms; 10-41 frames over 25 ms; worst 42-51 ms.

What the spikes are made of, in every run: **re-boxing a tree section's
collision on the main thread, 10-19 ms**, because diagonal branches and leaf
pads come out at 240-290 boxes per section; then splitting off a branch
(`voxel release`, up to 40 ms), and branches breaking where they land. The
first is shared code and the place to work next: coarser collision for
sections past a box count, as loose pieces already have (`MaxChunkBoxes`).
