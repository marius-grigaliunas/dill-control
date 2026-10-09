# 0015 Destruction off the frame

*9 October 2026. Builds on 0011, 0013, 0014.*

## Context

0014's collapses were measured on a machine in use and written off as "not a
number". Asked for 60 fps at 1080p whether the machine is in use or not, a
frame-by-frame recording (`cut quiet`, below) of one tower cut through showed
12.7 % of frames over budget, the worst at 117 ms:

- one collision body per building: re-boxing a section after a hit cost
  30-60 ms (Godot renumbers every other shape on each removal; Jolt rebuilds
  the whole body's shape);
- the structural check, the split, and the falling piece's collision and
  meshes, all in one frame: 70-100 ms;
- re-meshing on the main thread, six 4-5 ms sections a frame;
- first-time costs: compiling methods (5-8 ms), binding a C# Godot class
  (first `VoxelChunk` 3.7 ms, first `DamageInfo` 2.4-3.1 ms), and every
  `VoxelChunk` made at all cost 0.7 ms (a plain `RigidBody3D` costs nothing);
- a blast weighing every section of a building, and all eighty pieces of
  rubble that ignore it anyway;
- the main thread pre-empted by other programs at equal priority: steps that
  take 0.2 ms measured 6-9 ms in no fixed place.

The bench was measuring itself too: a console line costs the frame it is
printed in 15-40 ms here, and `bench.gd` printed every half second.

## Decision

**Nothing that scales with a model's voxels runs in the frame.** Two
`VoxelWorkers` threads below normal priority do it from a copy of the cells:

- **Re-meshing and re-boxing** a section. The main thread hands results over
  within `ApplyMillisecondsPerFrame` (1.5 ms), one at least.
- **The structural check and the split** (`VoxelSplit`): flood, load check,
  the pieces, each piece's grid, collision and meshes, and the part's own
  changed sections as they will be after it. The part carries the plan out
  only if its grid's `Version` has not moved, else plans again. A crumble on
  landing goes the same way: a block lies where it landed a frame or two,
  then comes apart.

**The main thread does only what must be now:** removing cells, collision a
falling body must not pass through (`CrushSlab`, re-boxing what a piece left),
and making bodies. A released piece takes over the meshes of the sections it
takes whole (its grid starts on a section boundary); coarse collision boxes
shrink to their voxels so an aligned piece does not come up inside its stump.
Pieces are made on the next physics tick within `SpawnMillisecondsPerFrame`,
a hinge a tick after that.

**A collision body per section** of a building (`VoxelPart.SectionBody`),
shapes straight on the server, box shapes shared by size. Damage reaches the
building once through `IDamageRelay` and `DamageReceiver.Of`, which every
weapon now uses; a blast skips a receiver's other sections, does not count
them as its cover, and skips what `IDamageable.Ignores` its source.

**Chunks are pooled** (`VoxelDebris`): 32 made at load, topped up one a frame,
every spent chunk recycled. **First times happen at load:** `JitWarmup`
compiles the assembly on a background thread and makes one of every Godot
class; `VoxelRehearsal` runs a split, crush and break-up of a made-up tower on
a worker and makes one contact query.

**The game runs above normal priority** on Windows (`ProcessPriority`); its
own background work runs below normal. Not high: input and audio services
must keep up.

Smaller: a crush-through's blast skips the building it has just crushed; three
landing blasts a frame at most; vectorised scans for a grid's bounds and a
crush's slab; a slab's search box fitted to the slab.

## Measured

`cut quiet` and `cutside quiet` (new: no screenshots or status lines, every
frame recorded by `PerfRecorder`), target machine, editor open, after a 40-60 s
cool-down, the cut at 2 s, the fall and the rubble after it:

| | over 16.7 ms | worst | release frame | first crush |
|---|---|---|---|---|
| before (`cut`) | 12.7 % | 117 ms | 100 ms | 40 ms |
| after (`cut`, two runs) | 0.2-0.6 % | 18-21 ms | gone from the worst | 18 ms |
| after (`cutside`, two runs) | 0.2-0.4 % | 18-20 ms | gone from the worst | 16-19 ms |

The 2-12 s window of those runs is as good as the 0-2 s window before the cut
(0.3-0.5 % against 0-0.5 %): the collapse no longer shows. The over-budget
frames left are the level's own, mostly `horde update` and the renderer.

Runs taken hot, or with Chrome busy, came out at 2-30 % over **before the cut
too**: the level itself averages 9.5 ms at full clocks and 11-15 ms throttled
or shared, with the horde's 25 000 agents. Destruction cannot be what holds 60
there. The last pair, after the crush was fitted to the footprint, was taken
with Cursor and Chrome using a quarter to a third of the CPU: 4.6-8.5 % over
before the cut, 10.8 % during `cutside`, the worst frames `horde update` (up to
25 ms) and render waits. The table above is from the quiet runs before that
fix; a quiet re-run of the pair is owed.

One behaviour fix came out of it: a crush-through's blast used to carve the
stump as well, and with that gone (buildings ignore it) a flat landing's slab,
sized only round its few corner contacts, left the tower standing on the rest
of its footprint. Its own slab now spans its footprint at the contact, and
the stump is crushed under exactly what was crushed of it.

## Costs

- A piece appears a tick after its split, a hinge a tick after that, a
  crumble a frame or two after the landing that caused it, and a re-meshed
  section up to a few frames after the hit. None of it shows at 60 fps.
- About 1.6 s of extra work at level load (class warm-up 120 ms, compiling
  on a background thread, 32 chunks).
- Above-normal priority takes cores from whatever else the player runs.
- Splitting allocates (a grid copy per check, lists of cells): the managed
  heap goes from 40 to 75 MB in a collapse, and a gen-0/1 collection lands in
  some frames.

## Not yet

- The level's base cost: horde update spikes of 5-15 ms (one stage at a time,
  no fixed one) and the renderer, with the machine shared.
- The airstrike pushes every rigid body by the same velocity whatever its
  mass: rubble leaves at up to 245 m/s.
