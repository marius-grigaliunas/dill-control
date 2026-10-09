# 0016 Rubble stays, and nothing blinks

*9 October 2026. Builds on 0014 and 0015.*

## Context

Seen in play after 0015:

- A tower crushing down lost its bottom storeys to dust and nothing else:
  "the bottom pieces just disappeared, which makes it pretty unbelievable."
- At a split, the building went before its pieces came: "it disappears and
  for a second nothing happens, and only then a new model appears or many new
  destroyed building pieces, but not all of them, and there are no effects."
  The pieces were made on later ticks and drew on later frames still; past
  the loose-piece cap the smallest went to dust.

Recorded frame by frame (Godot's movie writer, `--write-movie` at 10 fps):
the building was gone for one to two frames at a release and at a crumble,
longer where the piece's mesh had to be built after it was made.

## Decision

**Nothing disappears before what replaces it is drawn.** A split's pieces are
one `SpawnBatch`. The part they came from keeps its old meshes until the last
piece is made (half a second at most); a loose block breaking up stays drawn,
frozen and without collision, until then. Pieces upload their meshes the
moment they are made, and the plan builds every section's mesh so a piece is
never made with one missing. Each piece of a break-up comes out of a cloud its
size (`CoverDust`): the moment of the change is in dust.

**What is crushed becomes rubble.** `RubbleShare` (70 %) of every crushed slab
and failed storey is heaped round the footprint by `RubblePile`: a static voxel
model per collapse site and palette, filled like sand (random points in a ring
thickest at the footprint, each voxel rolling down while the drop is over one
voxel), coloured in patches. A crush also raises a ring of storey-sized clouds
rolling out from the footprint (`CrushDustRing`) and throws a few small pieces
clear (`SpallPerCrush`).

**Pieces at rest join the heap.** A piece of up to `BakeMaxVoxels` lying still
for `BakeAfterSeconds`, or the one the cap needs room for, is written into the
heap voxel for voxel where it lies and lingers 0.3 s until the heap has drawn
it. Rubble no longer vanishes at the cap, and a hundred pieces stop being a
hundred bodies and a hundred draws. Bigger pieces stay bodies you can shoot.

**A heap is still voxels.** Its tiles relay damage to it like a building's
sections (`RubbleCollider`): shots and blasts carve it, what is cut loose
falls, and the collision follows. The gravity gun tears a lump off it where it
is aimed (`IPluckable`, `PluckRadius`) and lifts loose rubble up to
`MaxGrabVoxels` (`IGrabbable`: held rubble does not settle, freeze, join a heap
or go for the cap). Rubble landing on a heap does not dig it.

**A heap collides as heightfields, in 4 m tiles,** made only where there is
rubble, refreshed five times a second, four tiles a tick. Boxed voxel by voxel
a heap came to thousands of boxes a section (20-36 ms to re-box); one
heightfield over the whole heap lay under the tower crushing into it, and
Jolt re-tested its 15 000 triangles against the tower at every crush (20 ms
physics steps).

**The collector, fed less.** The mesher keeps its lists per thread and hands
pooled arrays to Godot through spans; section snapshots are pooled; a part
keeps one snapshot for its plans; a flood's working arrays are per thread; a
crush puts its part's structural check off until a second after the last
crush. Collections during a tower's fall: 34 to 7; the heap: 44-99 MB to
51-56 MB.

## Measured

`cut quiet`, same machine state (Chrome and Cursor using 10-20 % CPU), before
and after the collector work: fall window 20 % over budget at 14.8 ms mean, then
5.4 % at 12.2 ms. With the machine cooled: `cut` 7.3 % over in the fall,
`cutside` 1.5 %. Machine state moves more than these do from run to run (a run
taken hot or busy came out at 36 % over **before** the cut); read them paired.

## Costs

- A heap's collision is its surface only: what is under an overhang is not
  solid to walk under.
- Pieces linger, frozen, for up to half a second while their batch is made,
  and a baked piece for 0.3 s, overlapping their replacements under dust.
- More dust on screen: 24 clouds in the pool, not 12.

## Not yet

- A crush still costs 2-10 ms in its frame.
- The horde: its update spikes 5-19 ms on a shared machine, before anything
  falls.
