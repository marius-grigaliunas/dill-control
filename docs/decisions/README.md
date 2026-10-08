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
