# 0013 Buildings carry their own weight

*8 October 2026. Builds on 0011.*

*"What falls fractures" is superseded by 0014: what falls stays whole and breaks where it lands.*

## Context

0011 made every voxel thing break alike, but a building only fell when it was
cut clean through, and what fell left as one rigid body that spun like a stick
and split once along a random plane. In play: "snaps the big building like
paper twigs." Skyscrapers were asked for, both to swing between and to bring
down, and a falling building should crush what is under it.

## Decision

**A standing building checks its load.** After a burst of hits, per layer of
the standing island: the share of its as-built voxels it still has, reduced
by how far the load above hangs off what is left (as a fraction of the
footprint's half width). It fails below `MinSupportFraction`, scaled by how
much is above it (`FullLoadLayers`). The lowest failing layer is crushed to
dust, a ring of clouds round the footprint, and everything above falls, tipping
about the remains of the support towards the side that went (`ToppleSpin`).
Per-layer sums, one pass; no flood beyond the existing one.

**What falls fractures.** A loose piece over `FractureVolume` (cubic metres,
so a mech's fine voxels never do) splits into storey-sized blocks with jagged,
stepped boundaries, at most `MaxFracturePieces`. They start moving as the
rigid whole would. A block that lands hard crumbles into halves of itself, or
cracks in two when there is no room under the cap.

**Heavy.** Concrete density, spin that falls with size, no bounce, full
friction, angular damping. A landing crushes everything within the block's
own size through `Blast`, lethal (`CrushDamageMax`); loose pieces ignore other
loose pieces, so a collapse leaves a pile, not a cloud.

**The cap gives up the least.** Past `MaxChunks`, the smallest settled piece
goes to dust first, then the smallest asleep, then the smallest of all. It
used to be the oldest, which in a collapse is the base of the tower: it
vanished in mid-air.

**Pieces are made over frames** (`ChunksPerFrame`), each where it would have
got to by then, so a collapse does not spend a frame making forty bodies.

**Armour is data.** `Armour` comes off every single hit; sustained damage (a
flame, a crowd chewing) and a beam's melt are not hits and are not reduced.
Small hits bank up to `MinCarveDamage`. With `CarveVolumePerDamage` set, a
bite's volume, not its radius, is proportional to damage: banking alone had
turned rifle fire into one 0.4 m bite every two rounds, and a Field Fighter
died to one magazine. Mechs read `resources/voxel/voxel_destruction_mech.tres`
(armour 28, 0.00025 m³ a point, a 1 m hinge so a leg goes only when most of a
segment is gone). By arithmetic, not yet by play: the fighter's body takes
about four magazines of the Brine Ripper or ten rockets. Same code; a second
resource of the same class.

**Skyscrapers** are graybox `.vox` towers from `tools/vox-tools/make_graybox.py`
(open column lobby, a slab every 4 m, a core, a glass facade), placed by hand
in `lvl_horde_flats` under `Props/Downtown`. One 32-voxel section wide, so
`SectionEdge` went from 24 to 32.

## Measured

Not yet on a quiet machine. Two `collapse` runs were taken while the machine
was in use and are not numbers. Seen during them: about 100 loose pieces in
motion, GPU 13 to 14 ms. To do: `collapse`, `wreck`, `mechwreck` and `horde
25000` paired against `ab9b9d8`.

## Not yet

- The horde's flow field is built once, so fallen towers do not reroute it.
- A grapple anchored on a tower that falls stays where it was.
