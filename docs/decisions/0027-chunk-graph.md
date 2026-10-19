# 0027 Buildings fall by a chunk graph

*13 October 2026. Builds on 0011, 0014, 0015 and 0016.*

## Context

The structural check and the re-mesh were already off the frame (0015). On
the 11 October `cut quiet` recordings the check peaked at 0.8 ms and the
re-mesh at 2.6 ms. What missed the frame was handing collision shapes to Jolt
(`slow-rebox`, handing 24.6 ms on 346 boxes), the heap's deposit (9-14 ms) and
one ~90 ms `voxel apply` in every run that also had a full (gen-2) collection.
`cut quiet` that evening: mean 15.3 ms, 25.6% of frames over 16.7 ms, one
gen-2.

## Decision

**A building, and the pieces it sheds, keep a chunk graph baked by the `.vox`
importer.** Chunks are about `voxel/chunk_size` voxels (default 8), jittered,
and never cross a MagicaVoxel material type. Each is one connected island;
a sliver joins a neighbour until that neighbour is a full chunk, and no
further, so a thin wall does not become one chunk the height of the building.
The graph stores as-built face contact, greedy boxes and per-layer sums.

What falls is decided on the chunks. A chunk a cut passes through is flooded
on its own cells, so the cut stays where it went. An edge holds while it keeps
`MinContactShare` of the faces it was built with (default 0: one face holds).
An overloaded edge that has already lost `StressLimit` of its faces weakens
for `StressSeconds` (default 1.4; 0 is 0014 exactly), creaks, sheds dust and
can drop one small chunk, then the load check takes over. A cut with nothing
left holding does not wait. Pieces break and crush as whole chunks and carry
their own sub-graph. The standing building keeps its greedy section boxes.

The gravity gun tears the chunk under the aim out of a building, or off a
loose piece too big to lift. The pull lasts with the chunk's remaining
contact, up to `TearMaxSeconds`; past that, or past `MaxGrabVoxels`, it is
held fast. Heaps still pluck voxels. The laser still carves voxels; the graph
only decides what falls. `bench.gd` mode `lasercut` sweeps the bike's beam
through `Tower11`. `B` tints chunks.

Mechs, trees (0024) and rubble have no graph. A model imported before this
warns and runs the cell flood.

Section bodies leave the physics space while their shapes are replaced, so
Jolt builds the body once. A heap's roll-down runs on a worker; the main
thread only fills the cells a frame or two later.

**Not done, because the numbers did not ask.** Closed chunk meshes would
double the faces at every chunk boundary, and re-meshing was already on the
workers. Settled debris stays real rubble (0016): after the worker, `rubble
deposit` still peaked at 5.8 ms and a heap mesh apply at 10.6 ms, which is
the fill, not the roll-down.

## What it costs

The graph scales with chunks, and a cut chunk's flood with that chunk's
cells, so 0015 still holds. A tear makes one body. `StressSeconds` delays an
overloaded, already-damaged edge; a severed cut does not. `Tower11`'s imported
scene is 219 KB (source 165 KB). Its voxels are 0.5 m, so a chunk of 8 is 4 m
across, not the 0.8 m a 0.1 m model gets.

## Measured

Plugged in, 11th-gen i5, Iris Xe, 1920x1080. One cooled `cut quiet` after the
sliver cap (`perf_2026-10-13_22-54-53`): mean 11.3 ms, 2.9% of frames over
16.7 ms, one frame over 25 ms. The tower dropped as one piece, crushed, and
broke on landing. `voxel collision` peaked at 1.1 ms (the handing spike did
not recur). `voxel graph` mean 0.016 ms, worst 12.0 ms, on the one decide
after the cut (`graph-decide`, 404 chunks). That is worse than the old
check's 0.8 ms, and it is the frame the graph was supposed to cheapen.
`graph-upkeep` on the cut itself was 6.5 ms. The gen-2 event fired once, on
a 21.7 ms frame, not a 90 ms `voxel apply`.

A second `cut quiet` taken before the machine had cooled (mean 16.6 ms, 40%
over) is not a measurement. `cutside quiet`, `collapse`, `wreck`, `lasercut`,
`arsenal` and the movie frames were not run. The 0.5 ms check and the paired
gates are not met.

## Follow-up, 19 October 2026

Pieces sank through the ground and "glitched away". `SinkProbe` (bench
words `sink`, `nodust`, `shapes`) followed every loose piece's boxes and
found four causes, none of them the crush:

- **A piece's collision stood above its voxels.** `Extract` shifted baked
  boxes by `ToGodot(min) * voxel` as well as by the piece's offset: a
  piece's boxes stood as high above its voxels as it had stood in the model
  (37 m for a branch). The drawn piece fell through everything. The shift
  is the offset alone.
- **Pieces flew with one sliver of collision.** The per-chunk fallback in
  `TryBoxes` took each chunk's first baked box, not their bounds, and a
  re-split or shot chunk kept boxes over cells it no longer had. A piece now
  takes the baked boxes only when every chunk is as baked and they fit the
  cap; otherwise it keeps the worker's boxes, made from its cells.
- **Pieces were made inside the ground.** A late piece's catch-up move
  (`Make`) is cut short at the ground, and crush spall is thrown level
  and dropped onto the surface under it, not along a contact normal that
  leaned by tens of degrees.
- **Fast or pinned pieces went through it.** Loose pieces sweep
  (`ContinuousCd`): a toppling tower's end reached 100 m/s, nearly the
  ground's thickness per step. A small piece pressed through by a tower on
  top of it, more than `SunkDepth` down, goes to dust and its rubble to the
  heap there; a big one is set back on the surface.

Cooled `cut quiet`, plugged in: mean 10.8 and 11.0 ms, 2.5% and 2.8% over
16.7 ms. `collapse`, `cut`, `cutside`, `tree`, `wreck` and `lasercut` with
the probe: nothing lost, no piece whose boxes and voxels disagree.
