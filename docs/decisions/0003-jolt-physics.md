# 0003 Jolt physics, set explicitly

*8 October 2026, carried from the lab*

## Decision

`physics/3d/physics_engine="Jolt Physics"` in `project.godot`, written out.

## Why explicit

**`DEFAULT` is not Jolt in Godot 4.7.2.** The lab verified this behaviourally: a
`RigidBody3D` set to 10,000 m/s keeps that speed under `DEFAULT` and
`GodotPhysics3D`, and is clamped to 500
(`physics/jolt_physics_3d/limits/max_linear_velocity`) only under
`Jolt Physics`.

## Why Jolt

The character controller relies on Jolt's `use_enhanced_internal_edge_removal`
(on by default, for both simulation and motion queries) to cross mesh seams
without snagging, and the voxel bodies and the horde's borrowed hit colliders
were benched under it.

## Two Jolt behaviours to remember

- `IntersectShape` returns one result **per triangle** of a
  `ConcavePolygonShape3D`, so one concave body can fill every result slot. The
  lab's `Blast.DistinctOverlaps` re-queries, excluding what it has already found;
  port it with `Blast`.
- Jolt does not populate Godot's body and pair monitors. They read zero.

If Jolt ever becomes unavailable, stop and raise it rather than falling back.
