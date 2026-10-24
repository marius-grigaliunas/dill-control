# 0039 Two physics layers: Structure for static voxel bodies, Litter for light loose things

*24 October 2026. Built. Builds on 0011, 0017 and 0022.*

## Context

Static voxel bodies (buildings, vines, rubble heaps) were on World and Prop.
The planet's gravity field is an Area3D the size of the planet that watches
Prop, so it can pull loose pieces down. A nest of 144 vines is about 64,000
static boxes, and the field watching them cost Jolt over 16 ms a tick, with
nothing moving.

Torn leaves came off on Prop at 120 kg. The player's mask includes Prop,
and a leaf landing on the player shoved it through the planet's ground,
where it fell for good.

## Decision

**Layer 9, `Structure`: what stands still and breaks.** A static voxel
body's sections and rubble heaps are on World and Structure, never Prop.
Every query that looks for props looks for Structure too (`GameLayers`:
the hitscan, explosion, direct-area and projectile masks).

**Layer 10, `Litter`: light loose things that fall and rest but never push
the player.** Torn leaves are on it, at 12 kg. The planet's gravity field
watches it; the player does not collide with it.

Also: a player found 3 m under the planet's ground is stood back on it.

## What it costs

- Two more layers to keep in sync by hand between `project.godot` and
  `GameLayers`.
- A new query that forgets Structure misses every building and vine.
- Litter cannot be stood on or pushed by the player.
