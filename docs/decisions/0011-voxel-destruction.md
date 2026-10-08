# 0011 Every voxel model breaks the same way

*8 October 2026*

## Context

Destruction is meant to read like Red Faction Guerrilla: cut a structure and
the part above falls as a physical piece and breaks up. The lab's
`VoxelBuilding` does one grid per building, generated, and turns losses into
dust rather than rubble. The game now has voxel models of several kinds (mechs
of moving parts, bought robots, buildings) and the rule asked for is that all
of them break alike.

## Decision

**One system, `scripts/voxel/`, for every voxel thing.** The importer keeps each
part's voxels in its imported scene (0009); a `VoxelBody` in the wrapper turns
each part into a `VoxelPart`. A mech, a pillar and a loose chunk are the same
code, so they cannot drift apart.

- **A hit takes a sphere out, sized in metres** from its damage
  (`CarveBase + damage x CarvePerDamage`), the same for a 0.1 m mech and a
  0.5 m tower. A direct hit marches its ray into the voxels and bites where it
  first meets one; a blast bites at the nearest point of the body, as `Blast`
  already asks `IBlastSurface`. Sustained damage is banked and bites in steps.
  Weapons know none of this: they hit an `IDamageable`.
- **What holds, holds; the rest falls.** After a burst of hits a part floods
  its grid. A building hangs from the ground, a leg or turret from its hinge
  voxel, a body or a chunk from its biggest piece. Everything else is lifted
  out as a **`VoxelChunk`**, a rigid body with box collision, or dust if it is
  smaller than `MinChunkVoxels`.
- **Chunks are voxel parts too.** They can be shot apart, break on a hard
  landing (a crack of dust through the middle, both halves falling on), crush
  what they land on through the ordinary `Blast` (so the horde takes it), and
  after `SettleSeconds` asleep stop simulating and stay as breakable rubble.
- **Mechs lose parts.** A leg segment shot away takes the rest of its leg; a
  quadruped limps on three; the body at `BodyDeathFraction`, or too few legs,
  and the whole mech falls apart and throws its rider. Their guns stop when the
  part they sit on goes, and hold fire near the player and other allies.
- **The graybox compound is voxels** (`tools/vox-tools/make_graybox.py`, 0.5 m),
  so it can be cut down. The ground is not: terrain that changes shape comes
  with the planet (M4).

**Budgets.** Hits re-mesh only the 24-voxel section they land in, with a cap on
sections per frame and structural checks per frame world-wide
(`VoxelBudget`). Loose pieces are capped (`MaxChunks`, 48); the oldest settled
one turns to dust past it. Dust is the lab's pooled, merged `DustField`.

**Collision follows the voxels for static bodies only.** A building's parts get
greedy box collision, rebuilt with the section; you walk on what is left and
grapple to it. A mech moves on its hull and its hits are resolved into the
voxels by the ray march, because colliders on animated parts would be rebuilt
every frame for nothing.

## Measured

`bench.gd ... wreck` and `mechwreck` in `lvl_horde_flats`, with the horde held
off: 12-14 ms mean while rifles bore tunnels, rockets break pillars, strikes
break columns and a tank falls apart into chunks; GPU 9-11 ms. `mechs` with
every ally a voxel body: 11-13 ms, no worse than before.

## Not yet

- The horde's flow graph is built once, so a fallen pillar still blocks its
  routing until the planet's restamp arrives.
- Allies hurt by pickles: done, `0012`.
- A grapple anchored on voxels that are then shot away stays where it was.
