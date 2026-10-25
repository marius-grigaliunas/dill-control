# 0046 Mechs fight on broken, mend themselves, and take their moves from Blender

*25 October 2026. Agreed with the user; proposed, not yet built. Amends 0005,
0010 and 0011.*

## Context

The mechs are support vehicles. Today a biped dies when it loses a leg
(`SpareLegs` 0), a lost arm just stops, and nothing shifts its weight. Their
motion is all procedural, tuned by build, run, look, tell: too slow for an
attack or a gait with character. Rigging the merged mesh in an auto-rigger
stretched the voxels between bones. The user wants Blender's IK and timeline
for moves, without giving up voxel destruction or frame time.

## Decision

**Only the core or the head kills a mech.** The walker's `Core` (today the
`Body` part) below `BodyDeathFraction`, or a `Head` part emptied or cut free.
Every limb can go and it fights on. None of the three mechs has a `Head`
part yet: the Field Fighter's is split out of `Body`; the tank and the
storage bot have none, so only their core counts.

**The importer bakes a mech's damage map**, beside the buildings' chunk
graph (0027). Per part: voxel count and mass, centre of mass, the voxels
that hold it to its parent at the hinge, its contacts (soles, fists, knees),
and a copy of its original cells. A hit then removes cells, subtracts mass
and checks the holding voxels: lookups, nothing scaling with voxels in the
frame (0015). The existing destruction is unchanged; no new layers or
interiors.

**The gait comes from what is left.** Balance is the remaining parts' baked
mass over whatever still touches the ground. All legs walk; fewer legs limp
or hop with a free arm as a crutch; no legs haul on the fists; no limbs at
all turn on the spot and still shoot. `SpareLegs` goes. Each gait is a
procedural rule the walker plans steps with, and any of them can take a
style loop from Blender.

**Mechs mend themselves, as AI.** A brain beside `MechCompanion` reads the
damage map and asks for help through one `EventBus` signal (a damaged mech
cannot name its repairer): light damage calls the mobile storage bot, which
regrows the lost cells from the copy, outward from the hinge; a lost limb is
dropped as a part in a pod, and the mech walks, limps or crawls to it to
have it fitted at its hinge; badly hurt, it falls back behind the player.
The storage bot is mainly storage and transforms to repair, and repairs the
other bots too. If every bot is gone, a supply drop brings more. Repair is
destruction played backward, on the same workers.

**Repair costs the material.** Each restored voxel, part and dropped bot is
priced in `resources/vehicles/mech_repair_default.tres` and entered in the
contract's ledger in `Session`. Once the fight cools, an owned mech's bill
is paid in the garage; a hired mech's is billed at the end of the contract.
There is no money yet: the ledger records, and the screens come with the
economy.

**Moves are posed in Blender and baked.** Our own add-on (`tools/blender/`,
Blender 5.1) imports a mech's `.vox` in its parts, each fixed whole to one
bone (rigid, never skinned), with IK handles on fists and feet. A move is an
action; timeline markers are its events (`hit`, `sfx:...`, `shake`); it
names the parts it claims. Export bakes each part's transform against its
rest to a `MechMove` `.tres`. The walker plays it as an override, relative
to the body: unclaimed parts keep walking, a missing part's keys are
skipped, a move whose key part is gone is mirrored or withheld. No IK runs
in the game beyond `MechLeg`'s feet. While posing, the add-on mirrors the
pose live into the Godot editor through the bridge, and a debug game
reloads changed moves without a restart.

**Two tools.** A mech lab in the Godot editor (`addons/mech_lab`, a main
screen beside Vines, and bridge commands): the damage map drawn, parts shot
off on purpose, balance and gait watched at any speed, repair played. And
the Blender add-on for moves and style. A headless `mechcheck.gd` holds
every mech `.vox` to the part rules; a `bench.gd` mode shoots limbs off one
by one and repairs them.

**Order**, a commit each: the damage map and its check; the lab with
balance and gaits (all three mechs); repair and resupply; the Blender
add-on and mirror; new attacks.

## What was not chosen

- **Skinning the voxel mesh to a skeleton**: it smears voxels and costs
  GPU skinning on every mech.
- **Clips in an `AnimationPlayer`**: the walker writes every part each
  frame, and a clip has no answer to a part that has been shot off.
- **Authoring moves in a Godot timeline**: possible, but Blender's IK,
  graph editor and timeline are what the user works in.
- **A hollow, layered armour remake**: the existing destruction already
  shows holes and lost parts.

## What it costs

- Crawling and one-legged gaits are new procedural work, unproven, and may
  look poor until styled.
- Splitting the fighter's head changes its model and its hinges.
- The storage bot's transform is not yet designed.
- Blender and the game must agree on pivots exactly; the add-on reads the
  same `.vox` with the same rules to guarantee it.
- Each stage is benched on the target machine before the next.
