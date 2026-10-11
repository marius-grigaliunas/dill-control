# Decisions

One file per decision that had more than one defensible answer: what the
situation was, what was decided, what was measured, and what it costs. One page
at most. `CLAUDE.md` holds the rule and points here for the why.

A decision that is later reversed is not rewritten. Its file gets a
`Superseded by NNNN` line at the top, and the new one says what changed.

Number them in order, `NNNN-short-title.md`.

| # | Decision |
|---|---|
| [0001](0001-a-new-repository.md) | A new repository for the game; the prototype stays as the lab |
| [0002](0002-performance-target.md) | 60 fps at 1080p native on Iris Xe, and how it is measured |
| [0003](0003-jolt-physics.md) | Jolt physics, set explicitly |
| [0004](0004-authored-over-generated.md) | Authored over generated |
| [0005](0005-art-pipeline.md) | Blender, glTF binary, one palette, shader-animated pickles; voxel art beside it, animated by code |
| [0006](0006-event-bus.md) | The event bus, and why each of its signals is on it |
| [0007](0007-audio-director.md) | `AudioDirector` as a second autoload |
| [0008](0008-horde-ground.md) | The horde stands on an interface, and the flats come first |
| [0009](0009-voxel-models.md) | MagicaVoxel `.vox` imported directly: parts as nodes, colour per vertex, emission |
| [0010](0010-procedural-mech.md) | Mechs walk by code: stepping feet, our own two-bone IK, brains apart from walkers |
| [0011](0011-voxel-destruction.md) | Every voxel model breaks the same way: carve, structure, physics chunks, dust |
| [0012](0012-horde-attacks-allies.md) | The horde attacks the allies: targets by group, bites into voxels, guard and stomp |
| [0013](0013-buildings-carry-weight.md) | Buildings carry their own weight: load check, storey fracture, heavy rubble that crushes; armour as data |
| [0014](0014-buildings-fall-whole.md) | Buildings fall whole and break where they land: crush-through, a hinge that tears, snapping on landing |
| [0015](0015-destruction-off-the-frame.md) | Destruction off the frame: voxel work on low-priority workers, a body per section, pooled chunks, first times at load, the game above normal priority |
| [0016](0016-rubble-stays.md) | Rubble stays, and nothing blinks: crushed storeys heap up, pieces replace what they came from under dust, resting rubble joins the heap |
| [0017](0017-the-planet.md) | The planet: ported at the world origin, a far shell and chunk levels, sites where authored things meet generated ground, the horde's ground from a provider |
| [0018](0018-seeded-planets.md) | A contract is a seed; planets roll inside an authored archetype's ranges, biomes are authored presets the seed shifts; ranges capped by the bench |
| [0019](0019-system-and-arrival.md) | The system and the arrival: the ship waits in orbit, the bike starts ridden with its nose on the compound, zero-g past the well, other bodies as lit discs in the sky, the air and fog thin with height |
| [0020](0020-compound-from-clusters.md) | The compound is a seeded layout of authored clusters: the seed picks the flattest site and where each cluster stands; the kit and its scenes decide what; the horde from a tier |
| [0021](0021-session-and-loading.md) | `Session`, a third autoload, carries the contract (state only); the contract level builds itself in stages behind its own loading screen and lets the player in once frames settle |
| [0022](0022-destruction-that-recovers.md) | Destruction the machine recovers from: the cap decides before a body is made, heaps merge and have holes, only grounded pieces bake, resting pieces re-boxed finely, a throw-speed cap, buildings tell the horde |
| [0023](0023-the-horde-keeps-house.md) | The horde keeps to its nest until provoked (aggro range, kills, a leash) and roams it rather than piling into one cell; on per contract tier |
| [0024](0024-trees-hold-by-connection.md) | Trees hold by connection, not weight: `voxel_destruction_tree.tres` turns 0013's load check off; a stats resource reaches only how its own model splits |
