# 0006 The event bus and its signals

*8 October 2026, ported from the lab*

## Decision

One autoload, `EventBus`, carries traffic whose sender cannot name its
listener. Each signal on it is listed here with the reason it cannot be a local
signal or an `[Export]`. A new signal gets a paragraph here before it gets code.

## The signals

**`DamageDealt(target, info)`.** Emitted wherever damage lands: a `Hitbox`, a
direct `IDamageable` hit, the horde's own damage pass. Heard by hit markers,
damage numbers and debug readouts. A rifle cannot name the HUD, and the HUD
cannot know every weapon and every hitbox in the level.

**`AmmoChanged(inMagazine, magazineSize, reserve)`.** Emitted by whichever
weapon is in hand, heard by the HUD. The weapons live in the player scene and
the HUD may not; neither knows the other exists.

**`PlayerStateChanged(state)`.** Locomotion state (idle, sprinting, on the line
and so on), cast to `int` because a Godot signal cannot carry a C# enum. Heard
by debug readouts now, audio and weapon spread later. The player is instanced
at runtime by `LevelRoot`, so nothing authored in a level can hold a reference
to it.

**`AreaBlast(blast)`.** Emitted once, by `Blast.ApplyDamage`, for every
explosion. The horde has no collider per agent, so a blast's overlap query
cannot find it; a weapon that named the horde would make every weapon depend
on it. The horde listens and runs its own pass over the agents in the radius.
The player listens for camera shake.

**`ObstaclesChanged(centre, radius)`.** Emitted by a building's `VoxelPart`
when its collision changes (shot through, crushed, collapsed), at most twice a
second per part. The horde stamps what stands where into its flow field once,
when it is built; a building that came down stayed blocked in it for good, and
the pickles walked round a gap that was no longer there. A building cannot
name the horde, and the horde cannot know every building. The `HordeDirector`
listens and asks physics again for the flow cells in the radius. Loose pieces
and rubble heaps do not count as obstacles there: they are walked over.

**`MechNeedsRepair(mech)`.** Emitted by a damaged mech's `MechCare` every few
seconds while voxels are gone from it (0046). The mech cannot name its
repairer: storage bots come and go, are destroyed, are dropped in new, and
any of them may be the one free and near. Each living bot's `MechMender`
listens and the nearest free one takes the job. A mech's lost limb does not
go here: it orders its own part by pod.

## What is not on it

Anything parent-to-child or between siblings in one scene: those use local
signals. The grapple, the camera rig and the loadout all talk to the player
controller that way.
