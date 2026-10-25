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
Every limb can go and it fights on. The Field Fighter's head is split out
of `Body` (its visor block, 42 voxels); the tank's head is its `Turret`; the
storage bot has none, so only its core counts. **A head is hardened**: it is
in its `VoxelBody`'s `Hardened`, and every hit's bite on it is
`HardenedCarveScale` (0.35 in `voxel_destruction_mech.tres`) of the radius,
so a head is not shot off before the rest. Measured by `mechcheck`: a rocket
takes 2 of the fighter's 42 head voxels (34 unhardened) and 4 of the
turret's 573 (56).

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

*Built (step 3):* `MechWalker.CurrentStance` is Walk, Limp (each leg its
own step group, `LimpSpeedPerLostLeg` a lost leg), Hop (one leg; the arm on
the lost side planted as a crutch, the body down so it reaches), Crawl (no
legs: belly down to the core's baked sole, the arms hauling in turn) or Sit
(nothing left: it turns and shoots). Legs and crutch arms are one list of
`MechStepper`s the gait plans over, in any number of groups. The body comes
down on a spring and the hull box shrinks with it; it sags toward each lost
leg. Numbers: `MechTuning`'s Broken group. Two faults found on the way and
fixed: a mech standing on the pieces of its own broken leg was flung at
200 m/s when it stepped off (platform velocity; only World and Structure
carry it now), and a body down among its pieces was boxed in (loose voxel
pieces it runs into are no longer collided with). `bench.gd ... limbs`
breaks every limb of the three allies in turn: means 11-14 ms, a few frames
to 21 ms as pieces spawn.

**Mechs mend themselves, as AI.** A brain beside `MechCompanion` reads the
damage map and asks for help through one `EventBus` signal (a damaged mech
cannot name its repairer): light damage calls the mobile storage bot, which
regrows the lost cells from the copy, outward from the hinge; a lost limb is
dropped as a part in a pod, and the mech walks, limps or crawls to it to
have it fitted at its hinge; badly hurt, it falls back behind the player.
The storage bot is mainly storage and transforms to repair, and repairs the
other bots too. If every bot is gone, a supply drop brings more. Repair is
destruction played backward, on the same workers.

*Built (step 4):* `MechCare` on every mech asks over
`EventBus.MechNeedsRepair` once `CallAtMissingVoxels` are gone, orders a
lost limb in the company's pod (`prp_drop_pod`, no landing blast) to its
side after `PodDelaySeconds`, walks to it (a companion `Errand`) and has
it fitted (`MechWalker.Refit`); with its core below `FallBackBelowCore` it
follows `FallBackDistance` behind the player. The storage bot's
`MechMender` takes the nearest asking mech, itself included, walks beside
it and regrows `RegrowPerSecond` voxels outward from each hinge
(`VoxelPart.Restore`, from the cells as built). Every voxel and part is
billed in `Session.Ledger`, cleared when a contract is accepted. Tried in
the asset lab: a bitten fighter mended by the bot; a lost leg dropped,
walked to and fitted, the bill 250 for the part and 623 voxels. Not built:
the bot's transform (not designed) and a supply drop of new bots. Regrown
cells cut off from their part fall away and are regrown again, and billed.

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
