# 0010 Mechs walk by code, not clips

*8 October 2026*

## Context

The ally mech (Quadruped Tank, `credits.md`) has to walk in any direction,
turn on the spot fast, jump and jet, swing its turret all the way round and
carry the player. It is rigid voxel parts. Its pack sells walk clips, made for
another engine and one walk direction.

## Decision

**Every motion is procedural.** `MechWalker` moves a `CharacterBody3D` on a box.
Each foot stays planted until the body leaves it more than `StepThreshold`
behind, then steps to where its rest spot will be a moment later; diagonal
pairs step together and wait for the other pair to land. Because steps follow
the body, any direction, turning in place and stopping square all come from one
rule, and a new mech needs no animation work at all.

**IK is ours, on plain nodes.** Godot 4.6's `TwoBoneIK3D` works on a
`Skeleton3D`; these are rigid parts with no skin, so a skeleton would be there
only to be solved. `MechLeg` solves hip-knee-ankle in a few lines and sets each
part's transform. Leg lengths come from the model's pivot voxels (0009).

**What it does is separate from how it walks.** `MechWalker` takes a move
intent, a facing, a turret aim and the jump. `MechCompanion` sets them for the
ally: follow, keep out of the player's way, carry a rider. An enemy walker
would be another brain on the same walker.

**Riding stows the player,** as the lab's bike did, and carries them at the
seat so the horde chases the mech.

## Cost

A step can only be as long as the voxel legs reach (about 1.1 m from hip to
sole), so the body sinks at speed for reach and a fast walk is busy feet. No
terrain yet but the flats: feet raycast to the ground, the body does not tilt
to a slope beyond what its spring does.

## Measured

`bench.gd ... lvl_horde_flats.tscn mech`, with the horde arriving: 9.4-12.1 ms
mean through following, riding, strafing, turning, jumping and the seat view.

## Addendum: sprint, raised arms, slash

*8 October 2026*

The Field Fighter is a biped with arms, not a turret. Three things it needed
that the tank did not:

**Sprint is a second top speed, not a new gait.** `MechWalker.Sprint` swaps
`MaxSpeed` for `SprintSpeed` when that is higher. Step time, crouch and a
steady forward lean lerp on from the walk values by how far past `MaxSpeed`
it is actually going. Default `SprintSpeed` is 0, so the tank and the storage
bot are unchanged. The companion sprints on Shift while ridden, and on its
own while further than `FollowSprintDistance` from its spot. Unlimited.

**Arms aim, then slash, on top of the gait swing.** `AimArm` holds a world
point and raises over `ArmRaiseSeconds`; a gun fires once `ArmReady` (the
raise blend past `ArmReadyFraction`). `StrokeArm` is a per-frame override
that wins while a slash is in the air. A mount that is not an arm (the
tank's turret) is always ready and ignores both. No clips: the aim is the
shortest-arc rotation from the rest pointing direction (shoulder to muzzle)
onto the target, yaw-clamped so a shoulder cannot twist behind the back.

**The slash is `Blast`, like every other hit.** `MechSlasher` poses a
horizontal sweep and, in three slices, lays spheres along the arm through
`Blast.ApplyDamage`, `SourceRid` the walker, `NoCrater`. The horde hears it
on the bus; voxel props and buildings take it the same way. Slot 3 selects
it, then LMB swings; holding chains, alternating arms. On its own it slashes
when the gunner's target is inside `AutoRange` and `AutoConeDegrees`.

Numbers live in `mch_field_fighter_tuning.tres` and
`mch_field_fighter_slash.tres`. `bench.gd ... mechs` on the target machine,
quiet phases (no screenshot): sprint 14.3 ms mean, 3/141 over 16.7 ms; both
arms firing 16.7 ms mean, 22/150 over (the explosions); slash 16.1 ms mean,
10/125 over. Screenshot phases are ignored for worst-frame. The tank
(`mech`) still boards and fires.
