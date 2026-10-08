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

## What is not on it

Anything parent-to-child or between siblings in one scene: those use local
signals. The grapple, the camera rig and the loadout all talk to the player
controller that way.
